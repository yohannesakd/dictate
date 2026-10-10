#!/usr/bin/env python3
"""Run the controller with controlled capture, recognition, delivery, and storage."""
from pathlib import Path
import subprocess
repo = Path(__file__).resolve().parents[1]
root = Path('/tmp/dictate-sol-fixes/controller-checks')
root.mkdir(exist_ok=True)
source = (repo / 'Sources/Dictation.swift').read_text().split('final class DictationController {', 1)[1]
source = 'import AppKit\nfinal class DictationController {' + source
source = source.replace('AXIsProcessTrusted()', 'true').replace('startSound?.play()', '')
a = source.index('    func refreshPermissions() {')
b = source.index('    func requestMicrophone()', a)
source = source[:a] + '    func refreshPermissions() { model.micAuthorized = true }\n\n' + source[b:]
# Setup methods are not exercised. Remove them to exclude real permission and preference services.
source = source[:source.index('    func requestMicrophone()')] + '}\n'
(root / 'Controller.swift').write_text(source)
(root / 'Stubs.swift').write_text(r'''
import AppKit
struct HistoryItem { var text: String; var raw: String? }
enum ModelStatus: Equatable { case loading, ready, failed(String) }
final class AppModel {
    enum Phase: Equatable { case idle, listening, processing, notice(String) }
    var phase = Phase.idle, handsFree = false
    var levels = [CGFloat](repeating: 0, count: 11)
    var micAuthorized = true, accessibilityTrusted = true, keyboardReady = true, fnKeyFree = true
    var savedKeys: Set<CleanupProvider> = [], chatgptSignedIn = false, signingIn = false, openAtLogin = false
    var smartCleanup = false, cleanupProvider = CleanupProvider.deepseek
    var smartIssue: String?, modelStatus = ModelStatus.ready
    var dictionary: [DictionaryEntry] = []
    var history = [HistoryItem(text: "Generated text", raw: "Generated raw text")]
    var needsSetup: Bool { false }
    var cleanupRules: TextCleanup.Rules { .init() }
    func addHistory(_ text: String, raw: String, note: String?) { history.insert(.init(text: text, raw: raw), at: 0) }
    func recordDictation(words: Int, seconds: Double) {}
    func recordLatency(_ ms: Int) {}
}
final class KeyboardMonitor {
    var onFnDown: (Double) -> Void = { _ in }, onFnUp: (Double) -> Void = { _ in }, onFnSpace: (Double) -> Void = { _ in }
    var onOtherKeyWithFn: () -> Void = {}, onEscape: () -> Void = {}, onPasteLast: () -> Void = {}, onTapReset: () -> Void = {}
    var isDictationActive: () -> Bool = { false }
    var isRunning: Bool { true }
    func start() -> Bool { true }
}
final class Recorder {
    static var starts = 0
    var onLevel: ((Float) -> Void)?, onInterrupted: ((String) -> Void)?
    func start() throws { Self.starts += 1 }
    func stop() -> [Float] { [Float](repeating: 0.1, count: 8000) }
    func snapshot() -> [Float] { [] }
}
final class Engine {
    struct Timing { var queueMs = 0, inferenceMs = 0, preparationMs = 0, callbackMs = 0 }
    static var pending: [(OperationCancellation, (Result<String, Error>, Timing) -> Void)] = []
    func load(completion: @escaping (Error?) -> Void) {}
    func transcribe(_ pcm: [Float], cancellation: OperationCancellation, completion: @escaping (Result<String, Error>, Timing) -> Void) {
        Self.pending.append((cancellation, completion))
    }
}
struct Stopwatch {
    let started: Double
    init(started: Double = Monotonic.now) { self.started = started }
    var wallMs: Int { Monotonic.milliseconds(since: started) }
    var cpuMs: Int { 0 }
}
enum MetricsLog { static func write(_ fields: [String: Any]) {} }
enum TranscriptLog { static func write(_ fields: [String: Any?]) {} }
final class APIKeyClient: CleanupClient {
    init(_ provider: CleanupProvider) {}
    var hasKey: Bool { false }
    func setKey(_ key: String?) throws {}
    func complete(instructions: String, message: String, timing: RequestTiming) async throws -> String { throw CancellationError() }
    func warm() {}
}
final class ChatGPTClient: CleanupClient {
    let auth = Auth()
    func complete(instructions: String, message: String, timing: RequestTiming) async throws -> String { throw CancellationError() }
    func warm() {}
    final class Auth {
        var isSignedIn: Bool { false }
        func signIn() async throws {}
        func signOut() async {}
    }
}
enum Insertion {
    static let correctionLifetime = 300.0
    struct Identity { let bundle = "test.receiver" }
    struct Target { var app: Identity?, element: AXUIElement? }
    struct FieldState { let value: String }
    struct Snapshot { let state: FieldState; let range: NSRange; let rawReplacement: String; let created: Double }
    enum Outcome: String { case verified, unchanged, unexpected, unavailable, targetChanged, cancelled, eventFailed, permissionDenied }
    struct Result {
        var outcome: Outcome; var postedMs: Int?; var postedAt: Double?; var verifiedAt: Double?
        var postedUnits: Int; var verifiedMs: Int?; var verificationMs: Int; var snapshot: Snapshot?
    }
    enum MutationOutcome: String { case noWrite, uncertain, verified }
    static var pending: [(OperationCancellation, (Result) -> Void)] = []
    static func captureTarget() -> Target { .init(app: .init(), element: nil) }
    static func insert(_ text: String, raw: String, target: Target, cancellation: OperationCancellation, completion: @escaping (Result) -> Void) {
        Self.pending.append((cancellation, completion))
    }
    static func correct(_ snapshot: Snapshot, restoreRaw: Bool, cancellation: OperationCancellation, completion: @escaping (MutationOutcome) -> Void) {}
}
''')
(root / 'Checks.swift').write_text(r'''
import AppKit
@main struct Checks {
    static func main() {
        let model = AppModel()
        let controller = DictationController(model: model)
        let result = Insertion.Result(outcome: .verified, postedMs: 1, postedAt: Monotonic.now, verifiedAt: Monotonic.now,
                                      postedUnits: 14, verifiedMs: 1, verificationMs: 0, snapshot: nil)
        controller.pasteLast()
        precondition(model.phase == .processing && Insertion.pending.count == 1)
        controller.pasteLast()
        controller.keyboard.onFnDown(Monotonic.now)
        precondition(Insertion.pending.count == 1 && Recorder.starts == 0)
        controller.cancel()
        precondition(Insertion.pending[0].0.isCancelled && model.phase == .idle)
        controller.keyboard.onFnDown(Monotonic.now)
        precondition(model.phase == .listening && Recorder.starts == 1)
        Insertion.pending[0].1(result)
        precondition(model.phase == .listening)
        controller.finish()
        precondition(model.phase == .processing && Engine.pending.count == 1)
        controller.cancel()
        precondition(Engine.pending[0].0.isCancelled)
        controller.keyboard.onFnDown(Monotonic.now)
        Engine.pending[0].1(.success("stale generated transcript"), .init())
        precondition(model.phase == .listening && Insertion.pending.count == 1)
        controller.finish()
        Engine.pending[1].1(.success("Hello."), .init())
        precondition(model.phase == .processing && Insertion.pending.count == 2)
        controller.cancel()
        controller.pasteLast()
        precondition(model.phase == .processing && Insertion.pending.count == 3)
        Insertion.pending[1].1(result)
        precondition(model.phase == .processing)
        Insertion.pending[2].1(result)
        precondition(model.phase == .idle)
        print("Passed retry serialization, busy phase, cancellation, and stale recognition/delivery ownership checks")
    }
}
''')
subprocess.run(['xcrun', 'swiftc', '-swift-version', '5', '-parse-as-library', str(repo / 'Sources/Operation.swift'),
                str(repo / 'Sources/TextCleanup.swift'), str(repo / 'Sources/PersonalDictionary.swift'),
                str(repo / 'Sources/SmartCleanup.swift'), str(root / 'Controller.swift'), str(root / 'Stubs.swift'),
                str(root / 'Checks.swift'), '-o', str(root / 'checks')], check=True)
subprocess.run([str(root / 'checks')], check=True)
