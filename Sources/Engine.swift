// Speech recognition with Phonon-2 on the Neural Engine. The model ships inside the app bundle.

import Foundation
import PhononCoreML

final class Engine {
    private let queue = DispatchQueue(label: "dictate.engine", qos: .userInitiated)
    private var transcriber: Transcriber?

    func load(completion: @escaping (Error?) -> Void) {
        queue.async {
            let result: Error?
            do { _ = try self.loadedTranscriber(); result = nil } catch { result = error }
            DispatchQueue.main.async { completion(result) }
        }
    }

    struct Timing {
        let queueMs: Int
        let inferenceMs: Int
        let preparationMs: Int
        let callbackMs: Int
    }

    func transcribe(_ pcm: [Float], cancellation: OperationCancellation,
                    completion: @escaping (Result<String, Error>, Timing) -> Void) {
        let queued = Monotonic.now
        queue.async {
            guard !cancellation.isCancelled else { return }
            let queueMs = Monotonic.milliseconds(since: queued)
            let preparation = Monotonic.now
            let loaded = Result { try self.loadedTranscriber() }
            let preparationMs = Monotonic.milliseconds(since: preparation)
            let inference = Monotonic.now
            let result = loaded.flatMap { transcriber in
                Result { try transcriber.transcribe(pcm).text.trimmingCharacters(in: .whitespacesAndNewlines) }
            }
            let inferenceMs = Monotonic.milliseconds(since: inference)
            let completed = Monotonic.now
            DispatchQueue.main.async {
                guard !cancellation.isCancelled else { return }
                completion(result, Timing(queueMs: queueMs, inferenceMs: inferenceMs, preparationMs: preparationMs,
                                          callbackMs: Monotonic.milliseconds(since: completed)))
            }
        }
    }

    private func loadedTranscriber() throws -> Transcriber {
        if let transcriber { return transcriber }
        guard let model = Bundle.main.url(forResource: "Phonon-2-CoreML", withExtension: nil) else {
            throw NSError(domain: "Dictate", code: 2, userInfo: [NSLocalizedDescriptionKey: "Phonon-2 model is missing from the app"])
        }
        var options = Transcriber.Options()
        options.backgroundLoad = true  // Prepare the longer encoder windows after the first one is ready.
        // Keep the audited 34.9 s window boundary while larger-boundary quality tests remain incomplete.
        // Below 34.9 s it stays a single pass; longer audio uses the windowed path.
        options.singleShotMaxSeconds = 34.9
        let loaded = try Transcriber(bundle: model, options: options)
        transcriber = loaded
        return loaded
    }
}
