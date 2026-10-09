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

    func transcribe(_ pcm: [Float], completion: @escaping (Result<String, Error>) -> Void) {
        queue.async {
            let result = Result {
                try self.loadedTranscriber().transcribe(pcm).text.trimmingCharacters(in: .whitespacesAndNewlines)
            }
            DispatchQueue.main.async { completion(result) }
        }
    }

    private func loadedTranscriber() throws -> Transcriber {
        if let transcriber { return transcriber }
        guard let model = Bundle.main.url(forResource: "Phonon-2-CoreML", withExtension: nil) else {
            throw NSError(domain: "Dictate", code: 2, userInfo: [NSLocalizedDescriptionKey: "Phonon-2 model is missing from the app"])
        }
        var options = Transcriber.Options()
        options.backgroundLoad = true  // Prepare the longer encoder windows after the first one is ready.
        // Phonon 1.1.2 asks for frames * 0.01 + 0.02 s of encoder, so a 34.99–35.0 s recording finds no 35 s encoder.
        // Below 34.9 s it stays a single pass; longer audio uses the windowed path.
        options.singleShotMaxSeconds = 34.9
        let loaded = try Transcriber(bundle: model, options: options)
        transcriber = loaded
        return loaded
    }
}
