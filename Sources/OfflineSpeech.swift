// Runs the bundled Whistle or Whisper helper for one recording.
// Process ownership allows cancellation and releases the model when inference ends.

import Foundation

enum OfflineSpeech {
    struct Assets {
        let executable: URL
        let model: URL
        let selection: SpeechModel

        /// The tool and the model inside the app bundle.
        static func bundled(_ selection: SpeechModel, _ bundle: Bundle = .main) throws -> Assets {
            let helper = selection == .whistle ? "needle" : "whisper-cli"
            let executable = bundle.bundleURL.appendingPathComponent("Contents/Helpers/" + helper)
            let relative = selection == .whistle ? "Whistle/whistle.cact" : "Whisper/ggml-base.bin"
            let model = bundle.bundleURL.appendingPathComponent("Contents/Resources/" + relative)
            guard FileManager.default.isExecutableFile(atPath: executable.path),
                  FileManager.default.isReadableFile(atPath: model.path) else { throw Failure.missing }
            return Assets(executable: executable, model: model, selection: selection)
        }
    }

    enum Failure: LocalizedError {
        case missing
        case launchFailed(String)
        case exited(String, diagnostics: String)
        case invalidOutput(String)
        case timedOut(seconds: Int)

        var errorDescription: String? {
            switch self {
            case .missing:
                return "The selected speech engine is missing from the app. Reinstall Dictate."
            case .launchFailed(let reason):
                return "The speech engine did not start: \(reason)"
            case .exited(let status, let diagnostics):
                return diagnostics.isEmpty ? "The speech engine failed (\(status))." : "The speech engine failed (\(status)): \(diagnostics)"
            case .invalidOutput(let output):
                return "The speech engine returned invalid output: \(output)"
            case .timedOut(let seconds):
                return "The speech engine did not finish within \(seconds) seconds."
            }
        }
    }

    /// Writes mono PCM16 WAV audio shared by both native engines.
    static func wav(_ pcm: [Float]) -> Data {
        let sampleBytes = pcm.count * 2
        var data = Data(capacity: 44 + sampleBytes)
        func tag(_ text: String) { data.append(contentsOf: Array(text.utf8)) }
        func u16(_ value: UInt16) { withUnsafeBytes(of: value.littleEndian) { data.append(contentsOf: $0) } }
        func u32(_ value: UInt32) { withUnsafeBytes(of: value.littleEndian) { data.append(contentsOf: $0) } }
        tag("RIFF"); u32(UInt32(36 + sampleBytes)); tag("WAVE")
        tag("fmt "); u32(16); u16(1); u16(1); u32(16000); u32(32000); u16(2); u16(16)
        tag("data"); u32(UInt32(sampleBytes))
        for sample in pcm { u16(UInt16(bitPattern: Int16(max(-32768, min(32767, sample * 32768))))) }
        return data
    }

    /// The text of the tool's JSON output. Anything else is an error, not an empty transcript.
    static func parse(_ output: Data) throws -> String {
        guard let object = (try? JSONSerialization.jsonObject(with: output)) as? [String: Any],
              let text = object["text"] as? String else {
            throw Failure.invalidOutput(excerpt(output) ?? "no output")
        }
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// The end of a process log: diagnostics come last.
    fileprivate static func excerpt(_ data: Data, characters: Int = 400) -> String? {
        let text = String(decoding: data, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
        return text.isEmpty ? nil : String(text.suffix(characters))
    }

    struct Run {
        let result: Result<String, Error>
        /// Time before the process starts: the temporary files.
        let preparationMs: Int
        /// Process start to its parsed output.
        let inferenceMs: Int
    }

    /// Runs the tool. Call it from one worker queue; the engine's serial queue does.
    final class Runner {
        private let lock = NSLock()
        private var running: (process: Process, directory: URL)?
        private var isShutDown = false
        private let outputLimit = 1 << 20

        func transcribe(_ pcm: [Float], assets: Assets, cancellation: OperationCancellation) -> Run {
            let started = Monotonic.now
            var launched: Double?
            let result = Result<String, Error> {
                try Self.requireActive(cancellation)
                let directory = FileManager.default.temporaryDirectory
                    .appendingPathComponent("dictate-whistle-\(UUID().uuidString)", isDirectory: true)
                try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false,
                                                        attributes: [.posixPermissions: 0o700])
                defer { try? FileManager.default.removeItem(at: directory) }
                let audio = directory.appendingPathComponent("audio.wav")
                let output = directory.appendingPathComponent("stdout.json")
                let diagnostics = directory.appendingPathComponent("stderr.txt")
                try OfflineSpeech.wav(pcm).write(to: audio)
                let outputHandle = try Self.createFile(output)
                let diagnosticsHandle = try Self.createFile(diagnostics)
                defer { try? outputHandle.close(); try? diagnosticsHandle.close() }

                let process = Process()
                process.executableURL = assets.executable
                if assets.selection == .whistle {
                    process.arguments = ["--model", assets.model.path, "--audio", audio.path, "--threads", "4"]
                } else {
                    process.arguments = ["--model", assets.model.path, "--file", audio.path, "--threads", "4",
                                         "--no-gpu", "--language", "auto", "--output-txt",
                                         "--output-file", directory.appendingPathComponent("transcript").path]
                }
                var environment = ProcessInfo.processInfo.environment
                environment["NEEDLE_TELEMETRY"] = "0"
                environment["DO_NOT_TRACK"] = "1"
                process.environment = environment
                process.currentDirectoryURL = directory
                process.standardInput = FileHandle.nullDevice
                process.standardOutput = outputHandle
                process.standardError = diagnosticsHandle
                // The output stays on disk until the process has exited, so a full pipe cannot block it.
                // Bound inference time for recordings up to five minutes. This is not a speed target.
                let budget = 30 + Int(Double(pcm.count) / 16000 * 4)
                try Self.requireActive(cancellation)
                launched = Monotonic.now
                try self.execute(process, in: directory, deadline: Monotonic.now + Double(budget),
                                 budget: budget, cancellation: cancellation)

                let log = try self.read(diagnostics)
                guard process.terminationReason == .exit, process.terminationStatus == 0 else {
                    let how = process.terminationReason == .exit
                        ? "exit status \(process.terminationStatus)" : "signal \(process.terminationStatus)"
                    throw Failure.exited(how, diagnostics: OfflineSpeech.excerpt(log) ?? "")
                }
                if assets.selection == .whistle { return try OfflineSpeech.parse(try self.read(output)) }
                return String(decoding: try self.read(directory.appendingPathComponent("transcript.txt")), as: UTF8.self)
                    .replacingOccurrences(of: "[BLANK_AUDIO]", with: "")
                    .trimmingCharacters(in: .whitespacesAndNewlines)
            }
            let inferenceStart = launched ?? Monotonic.now
            return Run(result: result, preparationMs: Int(max(0, inferenceStart - started) * 1000),
                       inferenceMs: launched == nil ? 0 : Monotonic.milliseconds(since: inferenceStart))
        }

        /// Kills a running process and removes its files. Dictate calls this when it quits, so the process cannot
        /// outlive the app. No later request launches.
        func shutdown() {
            lock.lock(); defer { lock.unlock() }
            isShutDown = true
            guard let running else { return }
            if running.process.isRunning { kill(running.process.processIdentifier, SIGKILL) }
            try? FileManager.default.removeItem(at: running.directory)
        }

        private static func requireActive(_ cancellation: OperationCancellation) throws {
            if cancellation.isCancelled { throw CancellationError() }
        }

        private static func createFile(_ url: URL) throws -> FileHandle {
            guard FileManager.default.createFile(atPath: url.path, contents: nil) else {
                throw Failure.launchFailed("could not create \(url.lastPathComponent)")
            }
            return try FileHandle(forWritingTo: url)
        }

        private func read(_ url: URL) throws -> Data {
            let handle = try FileHandle(forReadingFrom: url)
            defer { try? handle.close() }
            return try handle.read(upToCount: outputLimit) ?? Data()
        }

        /// Returns after the process exits. Cancellation and the deadline end it and wait for the exit.
        private func execute(_ process: Process, in directory: URL, deadline: Double, budget: Int,
                             cancellation: OperationCancellation) throws {
            let exited = DispatchSemaphore(value: 0)
            process.terminationHandler = { _ in exited.signal() }
            // The last cancellation check is under the lock, so a request cancelled while its files were
            // written never launches, and shutdown cannot miss a process that is about to start.
            lock.lock()
            guard !isShutDown, !cancellation.isCancelled else { lock.unlock(); throw CancellationError() }
            do { try process.run() } catch {
                lock.unlock()
                throw Failure.launchFailed(error.localizedDescription)
            }
            running = (process, directory)
            lock.unlock()
            defer { lock.lock(); running = nil; lock.unlock() }

            while exited.wait(timeout: .now() + .milliseconds(20)) == .timedOut {
                let reason: Error
                if cancellation.isCancelled { reason = CancellationError() }
                else if Monotonic.now > deadline { reason = Failure.timedOut(seconds: budget) }
                else { continue }
                process.terminate()
                if exited.wait(timeout: .now() + 2) == .timedOut {
                    kill(process.processIdentifier, SIGKILL)
                    exited.wait()
                }
                throw reason
            }
        }
    }
}
