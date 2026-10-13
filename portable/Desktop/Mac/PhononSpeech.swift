import Foundation
import CoreML
import PhononCoreML

final class PhononSpeech {
    static let shared = PhononSpeech()
    private let queue = DispatchQueue(label: "dictate.phonon")
    private var transcriber: Transcriber?
    private var cpuOnly: Bool?
    private var hardware = "CPU"
    private var fallback: String?

    func unload() {
        queue.sync {
            transcriber?.waitForBackgroundLoads()
            transcriber = nil
            cpuOnly = nil
        }
    }

    private func load(directory: String, units: MLComputeUnits) throws -> Transcriber {
        var options = Transcriber.Options()
        options.computeUnits = units
        options.backgroundLoad = true
        options.singleShotMaxSeconds = 34.9
        return try Transcriber(bundle: URL(fileURLWithPath: directory), options: options)
    }

    private func prepare(directory: String, cpuOnly: Bool) throws -> Transcriber {
        if self.cpuOnly == cpuOnly, let transcriber { return transcriber }
        transcriber?.waitForBackgroundLoads()
        transcriber = nil
        self.cpuOnly = nil
        fallback = nil
        let neural = MLModel.availableComputeDevices.contains { device in
            if case .neuralEngine = device { return true }
            return false
        }
        let choices: [MLComputeUnits] = cpuOnly ? [.cpuOnly] : neural ? [.cpuAndNeuralEngine, .cpuAndGPU, .cpuOnly] : [.cpuAndGPU, .cpuOnly]
        for (index, units) in choices.enumerated() {
            do {
                let loaded = try load(directory: directory, units: units)
                transcriber = loaded
                self.cpuOnly = cpuOnly
                hardware = units == .cpuAndNeuralEngine ? "Neural Engine + CPU" : units == .cpuAndGPU ? "GPU + CPU" : "CPU"
                if index > 0 { fallback = "Phonon-2 could not use the preferred accelerator. Using \(hardware)." }
                return loaded
            } catch {
                if (error as NSError).domain != MLModelErrorDomain || index == choices.count - 1 { throw error }
            }
        }
        fatalError("The compute choices always include CPU.")
    }

    func run(directory: String, samples: [Float]?, cpuOnly: Bool, cancellation: OperationCancellation,
             completion: @escaping ([String: Any]) -> Void) {
        queue.async {
            do {
                if cancellation.isCancelled { completion(["cancelled": true]); return }
                let loaded = try self.prepare(directory: directory, cpuOnly: cpuOnly)
                if cancellation.isCancelled { completion(["cancelled": true]); return }
                let text = try samples.map { try loaded.transcribe($0).text.trimmingCharacters(in: .whitespacesAndNewlines) }
                if cancellation.isCancelled { completion(["cancelled": true]); return }
                var result: [String: Any] = ["hardware": self.hardware]
                if let text { result["text"] = text }
                if let fallback = self.fallback { result["fallback"] = fallback }
                completion(result)
            } catch { completion(["error": error.localizedDescription]) }
        }
    }
}
