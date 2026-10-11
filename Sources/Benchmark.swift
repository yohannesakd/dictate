// A reproducible recognition benchmark that runs before the app starts:
//   Dictate --benchmark phonon|whistle|whisper FILE.wav [--iterations N] [--cancel-after-ms MS]
// It calls the same Engine as dictation, on the main dispatch queue. It starts no controller, window, history,
// preferences, or network cleanup. It prints one JSON object per iteration on standard output, and a short
// message on standard error when the arguments, the audio, or the model fail.
//
// Each row separates the upfront model load (load_ms, the same value on every row) from one transcription
// (wall_ms is the time until the Engine callback; the Engine fields split it). The app_* fields describe this
// process only. The children_* fields come from the operating system's accounting of finished child processes,
// so they cover Whistle's needle tool. They are not a process-tree resident-memory sample.
//
// --cancel-after-ms starts each transcription, cancels it after MS milliseconds, and starts the measured one
// on the same Engine at once. Its wall_ms then includes freeing the Engine queue. The row reports
// cancelled_callback (true is a failure: the cancelled request delivered a result) and cancel_missed (the
// first request finished before the cancel, so nothing was cancelled).

import AVFoundation
import Darwin
import Foundation

enum Benchmark {
    private static let usage = "Usage: Dictate --benchmark phonon|whistle|whisper FILE.wav [--iterations N] [--cancel-after-ms MS]"

    private struct Options {
        let model: SpeechModel
        let audio: URL
        var iterations = 1
        var cancelAfterMs: Int?

        init?(_ arguments: [String]) {
            guard arguments.count >= 2, let model = SpeechModel(rawValue: arguments[0]) else { return nil }
            self.model = model
            audio = URL(fileURLWithPath: arguments[1])
            var rest = arguments.dropFirst(2)
            while let flag = rest.popFirst() {
                guard let text = rest.popFirst(), let value = Int(text), value > 0 else { return nil }
                switch flag {
                case "--iterations": iterations = value
                case "--cancel-after-ms": cancelAfterMs = value
                default: return nil
                }
            }
        }
    }

    /// Runs the benchmark and exits the process. `arguments` follow `--benchmark`.
    static func run(_ arguments: [String]) -> Never {
        guard let options = Options(arguments) else { fail(usage, code: 2) }
        let pcm: [Float]
        do { pcm = try decode(options.audio) } catch { fail("Cannot read \(options.audio.path): \(error.localizedDescription)", code: 2) }
        Session(options: options, pcm: pcm).start()
        dispatchMain()
    }

    private static func fail(_ message: String, code: Int32) -> Never {
        FileHandle.standardError.write(Data((message + "\n").utf8))
        exit(code)
    }

    /// The file as 16 kHz mono float samples, converted with AVFoundation.
    private static func decode(_ url: URL) throws -> [Float] {
        let file = try AVAudioFile(forReading: url)
        let source = file.processingFormat
        guard file.length > 0,
              let target = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false),
              let converter = AVAudioConverter(from: source, to: target),
              let input = AVAudioPCMBuffer(pcmFormat: source, frameCapacity: AVAudioFrameCount(file.length)),
              let output = AVAudioPCMBuffer(
                pcmFormat: target, frameCapacity: AVAudioFrameCount(Double(file.length) * 16000 / source.sampleRate) + 1024)
        else { throw NSError(domain: "Dictate", code: 3, userInfo: [NSLocalizedDescriptionKey: "empty or unsupported audio"]) }
        try file.read(into: input)
        var supplied = false
        var conversionError: NSError?
        let status = converter.convert(to: output, error: &conversionError) { _, inputStatus in
            if supplied { inputStatus.pointee = .endOfStream; return nil }
            supplied = true
            inputStatus.pointee = .haveData
            return input
        }
        if status == .error { throw conversionError ?? NSError(domain: "Dictate", code: 3) }
        guard let samples = output.floatChannelData else { throw NSError(domain: "Dictate", code: 3) }
        return Array(UnsafeBufferPointer(start: samples[0], count: Int(output.frameLength)))
    }

    /// Runs on the main queue: the Engine delivers its callbacks there.
    private final class Session {
        let options: Options
        let pcm: [Float]
        let engine = Engine()
        var loadMs = 0
        var baseline = Counters.now

        init(options: Options, pcm: [Float]) {
            self.options = options
            self.pcm = pcm
        }

        func start() {
            let loading = Monotonic.now
            engine.load(options.model) { error in
                self.loadMs = Monotonic.milliseconds(since: loading)
                if let error {
                    Benchmark.emit(["speech_model": self.options.model.rawValue, "event": "load_failed", "load_ms": self.loadMs,
                                    "error": error.localizedDescription])
                    exit(1)
                }
                self.iterate(1)
            }
        }

        private func iterate(_ index: Int) {
            guard index <= options.iterations else { exit(0) }
            baseline = Counters.now
            guard let cancelAfterMs = options.cancelAfterMs else {
                measure(index) { [:] }
                return
            }
            let victim = OperationCancellation()
            var cancelIssued = false
            var missed = false
            var cancelledDelivered = false
            engine.transcribe(pcm, selection: options.model, cancellation: victim) { _, _ in
                if cancelIssued { cancelledDelivered = true } else { missed = true }
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + .milliseconds(cancelAfterMs)) {
                cancelIssued = true
                victim.cancel()
                self.measure(index) {
                    ["cancel_after_ms": cancelAfterMs, "cancel_missed": missed, "cancelled_callback": cancelledDelivered]
                }
            }
        }

        /// Starts the measured request. `extra` runs when the row is written, so it sees any late callback.
        private func measure(_ index: Int, extra: @escaping () -> [String: Any]) {
            let submitted = Monotonic.now
            engine.transcribe(pcm, selection: options.model, cancellation: OperationCancellation()) { result, timing in
                let wall = Monotonic.milliseconds(since: submitted)
                let usage = Counters.now
                var row: [String: Any] = [
                    "speech_model": self.options.model.rawValue, "iteration": index,
                    "audio_s": (Double(self.pcm.count) / 16000 * 100).rounded() / 100,
                    "load_ms": self.loadMs, "wall_ms": wall, "inference_ms": timing.inferenceMs,
                    "preparation_ms": timing.preparationMs, "queue_ms": timing.queueMs, "callback_ms": timing.callbackMs,
                    "app_footprint_mb": (usage.footprintMB * 10).rounded() / 10,
                    "app_peak_footprint_mb": (usage.peakFootprintMB * 10).rounded() / 10,
                    "app_cpu_ms": Int((usage.appCPU - self.baseline.appCPU) * 1000),
                    "children_cpu_ms": Int((usage.childrenCPU - self.baseline.childrenCPU) * 1000),
                    "children_max_rss_mb": (usage.childrenMaxRSSMB * 10).rounded() / 10,
                    "leftover_temp_dirs": Self.leftoverDirectories(),
                ]
                row.merge(extra()) { $1 }
                switch result {
                case .success(let text): row["text"] = text
                case .failure(let error): row["error"] = error.localizedDescription
                }
                Benchmark.emit(row)
                if case .failure = result { exit(1) }
                if row["cancelled_callback"] as? Bool == true { exit(1) }
                self.iterate(index + 1)
            }
        }

        /// Counts shared temporary folders. Concurrent app runs can contribute to this diagnostic.
        private static func leftoverDirectories() -> Int {
            let entries = (try? FileManager.default.contentsOfDirectory(atPath: FileManager.default.temporaryDirectory.path)) ?? []
            return entries.filter { $0.hasPrefix("dictate-whistle-") }.count
        }
    }

    /// Process counters at one moment. Child counters include only children that have exited and been reaped.
    private struct Counters {
        let appCPU: Double
        let childrenCPU: Double
        let childrenMaxRSSMB: Double
        let footprintMB: Double
        let peakFootprintMB: Double

        static var now: Counters {
            var children = rusage()
            getrusage(RUSAGE_CHILDREN, &children)
            func seconds(_ t: timeval) -> Double { Double(t.tv_sec) + Double(t.tv_usec) / 1_000_000 }
            let memory = ProcessStats.footprintMB()
            return Counters(appCPU: ProcessStats.cpuSeconds(),
                         childrenCPU: seconds(children.ru_utime) + seconds(children.ru_stime),
                         childrenMaxRSSMB: Double(children.ru_maxrss) / 1_048_576,  // Bytes on macOS.
                         footprintMB: memory.current, peakFootprintMB: memory.peak)
        }
    }

    private static func emit(_ row: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: row, options: [.sortedKeys]) else { return }
        FileHandle.standardOutput.write(data + Data("\n".utf8))
    }
}
