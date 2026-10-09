// Microphone capture, converted to 16 kHz mono float32 for the model.

import AVFoundation

final class Recorder {
    private var engine: AVAudioEngine?
    private var converter: AVAudioConverter?
    private var configurationObserver: NSObjectProtocol?
    private var interrupted = false
    private let target = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false)!
    private let lock = NSLock()
    private var samples: [Float] = []
    /// Called on the audio thread with the RMS level of each converted buffer.
    var onLevel: ((Float) -> Void)?
    /// Called on the main queue, at most once per recording, when capture stops early (device change or conversion error).
    var onInterrupted: ((String) -> Void)?

    func start() throws {
        lock.lock(); samples = []; interrupted = false; lock.unlock()
        // A new engine each time picks up the current default input device.
        let engine = AVAudioEngine()
        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        guard let converter = AVAudioConverter(from: format, to: target) else {
            throw NSError(domain: "Dictate", code: 1, userInfo: [NSLocalizedDescriptionKey: "Unsupported input format \(format)"])
        }
        converter.downmix = true
        self.converter = converter
        input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in self?.append(buffer) }
        // A device or format change stops the engine. Report it; the controller ends the recording with what was captured.
        configurationObserver = NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine,
                                                                       queue: nil) { [weak self] _ in
            self?.interrupt("The microphone changed")
        }
        engine.prepare()
        try engine.start()
        self.engine = engine
    }

    /// Stops capture and hands over the samples. The recorder keeps no copy.
    func stop() -> [Float] {
        if let configurationObserver { NotificationCenter.default.removeObserver(configurationObserver) }
        configurationObserver = nil
        engine?.inputNode.removeTap(onBus: 0)
        engine?.stop()
        engine = nil
        converter = nil
        lock.lock(); defer { lock.unlock() }
        let captured = samples
        samples = []
        return captured
    }

    /// A copy of the audio so far, for transcription during recording.
    func snapshot() -> [Float] {
        lock.lock(); defer { lock.unlock() }
        return samples
    }

    private func interrupt(_ reason: String) {
        lock.lock()
        let first = !interrupted
        interrupted = true
        lock.unlock()
        // Teardown happens on the main queue, outside the audio and notification callbacks.
        if first { DispatchQueue.main.async { self.onInterrupted?(reason) } }
    }

    private func append(_ buffer: AVAudioPCMBuffer) {
        guard let converter else { return }
        let capacity = AVAudioFrameCount(Double(buffer.frameLength) * target.sampleRate / buffer.format.sampleRate) + 32
        guard let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
        var supplied = false
        var error: NSError?
        converter.convert(to: out, error: &error) { _, status in
            if supplied { status.pointee = .noDataNow; return nil }
            supplied = true
            status.pointee = .haveData
            return buffer
        }
        if error != nil {
            interrupt("Microphone audio could not be read")
            return
        }
        guard let channel = out.floatChannelData else { return }
        let chunk = Array(UnsafeBufferPointer(start: channel[0], count: Int(out.frameLength)))
        let rms = chunk.isEmpty ? 0 : sqrt(chunk.reduce(0) { $0 + $1 * $1 } / Float(chunk.count))
        lock.lock(); samples.append(contentsOf: chunk); lock.unlock()
        onLevel?(rms)
    }
}
