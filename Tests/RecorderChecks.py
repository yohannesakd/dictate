#!/usr/bin/env python3
"""Compile Recorder with controlled audio objects. Open no audio device."""
from pathlib import Path
import subprocess

repo = Path(__file__).resolve().parents[1]
root = Path('/tmp/dictate-sol-fixes/recorder-checks')
root.mkdir(exist_ok=True)
source = (repo / 'Sources/Recorder.swift').read_text().replace('import AVFoundation', 'import Foundation')
source = source.replace('NotificationCenter.default', 'CaptureNotifications.shared')
(root / 'Recorder.swift').write_text(source)
(root / 'AudioStubs.swift').write_text(r'''
import Foundation

typealias AVAudioFrameCount = UInt32
enum AudioFormat { case pcmFormatFloat32 }
enum ConverterInputStatus { case noDataNow, haveData }
final class AVAudioFormat {
    let sampleRate: Double
    init?(commonFormat: AudioFormat, sampleRate: Double, channels: Int, interleaved: Bool) { self.sampleRate = sampleRate }
}
final class AVAudioPCMBuffer {
    let format: AVAudioFormat
    let frameLength: AVAudioFrameCount = 0
    var floatChannelData: UnsafePointer<UnsafeMutablePointer<Float>>? { nil }
    init?(pcmFormat: AVAudioFormat, frameCapacity: AVAudioFrameCount) { format = pcmFormat }
}
final class AVAudioConverter {
    static var failCreation = false
    static var failBuffer = false
    var downmix = false
    init?(from: AVAudioFormat, to: AVAudioFormat) { if Self.failCreation { return nil } }
    func convert(to: AVAudioPCMBuffer, error: inout NSError?, withInputFrom: (Int, UnsafeMutablePointer<ConverterInputStatus>) -> AVAudioPCMBuffer?) {
        if Self.failBuffer { error = NSError(domain: "test", code: 1) }
    }
}
final class InputNode {
    var tap: ((AVAudioPCMBuffer, Any?) -> Void)?
    func outputFormat(forBus: Int) -> AVAudioFormat { AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48000, channels: 1, interleaved: false)! }
    func installTap(onBus: Int, bufferSize: Int, format: AVAudioFormat, block: @escaping (AVAudioPCMBuffer, Any?) -> Void) { tap = block }
    func removeTap(onBus: Int) { tap = nil }
}
final class AVAudioEngine: NSObject {
    static var instances: [AVAudioEngine] = []
    static var failStart = false
    let inputNode = InputNode()
    var stopped = false
    override init() { super.init(); Self.instances.append(self) }
    func prepare() {}
    func start() throws { if Self.failStart { throw NSError(domain: "test", code: 2) } }
    func stop() { stopped = true }
}
extension Notification.Name { static let AVAudioEngineConfigurationChange = Notification.Name("test.audio.change") }
final class CaptureNotifications {
    static let shared = CaptureNotifications()
    var observers: [(NSObject, Any?, (Notification) -> Void)] = []
    func addObserver(forName: Notification.Name, object: Any?, queue: OperationQueue?, using: @escaping (Notification) -> Void) -> NSObjectProtocol {
        let token = NSObject()
        observers.append((token, object, using))
        return token
    }
    func removeObserver(_ token: NSObjectProtocol) { observers.removeAll { $0.0 === (token as AnyObject) } }
    func post(_ engine: AVAudioEngine) {
        for observer in observers where (observer.1 as? AVAudioEngine) === engine {
            observer.2(Notification(name: .AVAudioEngineConfigurationChange, object: engine))
        }
    }
}
''')
(root / 'Checks.swift').write_text(r'''
import Foundation
@main struct Checks {
    @MainActor static func main() async throws {
        let recorder = Recorder()
        var interruptions = 0
        recorder.onInterrupted = { _ in interruptions += 1 }
        AVAudioEngine.failStart = true
        for _ in 0..<3 {
            do { try recorder.start(); preconditionFailure("expected failure") } catch {}
            precondition(CaptureNotifications.shared.observers.isEmpty)
            precondition(AVAudioEngine.instances.last!.inputNode.tap == nil)
            precondition(AVAudioEngine.instances.last!.stopped)
        }
        AVAudioEngine.failStart = false
        AVAudioConverter.failCreation = true
        do { try recorder.start(); preconditionFailure("expected converter failure") } catch {}
        precondition(CaptureNotifications.shared.observers.isEmpty)
        AVAudioConverter.failCreation = false
        try recorder.start()
        let old = AVAudioEngine.instances.last!
        let oldTap = old.inputNode.tap!
        CaptureNotifications.shared.post(old)
        _ = recorder.stop()
        try recorder.start()
        let current = AVAudioEngine.instances.last!
        try await Task.sleep(nanoseconds: 20_000_000)
        precondition(interruptions == 0)
        precondition(CaptureNotifications.shared.observers.count == 1)
        AVAudioConverter.failBuffer = true
        oldTap(AVAudioPCMBuffer(pcmFormat: old.inputNode.outputFormat(forBus: 0), frameCapacity: 0)!, nil)
        CaptureNotifications.shared.post(old)
        try await Task.sleep(nanoseconds: 20_000_000)
        precondition(interruptions == 0)
        CaptureNotifications.shared.post(current)
        CaptureNotifications.shared.post(current)
        try await Task.sleep(nanoseconds: 20_000_000)
        precondition(interruptions == 1)
        _ = recorder.stop()
        precondition(CaptureNotifications.shared.observers.isEmpty && current.inputNode.tap == nil)
        print("Passed failed-start cleanup, converter failure, stale interruption, stale tap, and single-interruption checks")
    }
}
''')
subprocess.run(['xcrun', 'swiftc', '-swift-version', '5', '-parse-as-library', str(root / 'Recorder.swift'),
                str(root / 'AudioStubs.swift'), str(root / 'Checks.swift'), '-o', str(root / 'checks')], check=True)
subprocess.run([str(root / 'checks')], check=True)
