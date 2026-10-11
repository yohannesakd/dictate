// macOS adapters for the native service. Electron owns the desktop window.
import AppKit
import AVFoundation
import Security
import ServiceManagement

public typealias LoopCallback = @convention(c) () -> Void
public typealias DispatchCallback = @convention(c) (UnsafeMutableRawPointer) -> Void
@_cdecl("dictate_run_loop") public func runLoop(_ started: LoopCallback) {
    let app = NSApplication.shared
    app.setActivationPolicy(.prohibited)
    DispatchQueue.main.async { started() }
    app.run()
}
@_cdecl("dictate_stop_loop") public func stopLoop() {
    NSApplication.shared.stop(nil)
    if let event = NSEvent.otherEvent(with: .applicationDefined, location: .zero, modifierFlags: [], timestamp: 0,
                                     windowNumber: 0, context: nil, subtype: 0, data1: 0, data2: 0) {
        NSApplication.shared.postEvent(event, atStart: true)
    }
}
@_cdecl("dictate_post") public func post(_ callback: DispatchCallback, _ context: UnsafeMutableRawPointer) {
    DispatchQueue.main.async { callback(context) }
}

private func json(_ object: [String: Any]) -> UnsafeMutablePointer<CChar> {
    let data = try! JSONSerialization.data(withJSONObject: object)
    return strdup(String(data: data, encoding: .utf8)!)!
}
private func string(_ pointer: UnsafePointer<CChar>) -> String { String(cString: pointer) }
private final class NativeTarget: NSObject {
    let value = Insertion.captureTarget()
}
private final class NativeCorrection: NSObject {
    let value: Insertion.Snapshot
    init(_ value: Insertion.Snapshot) { self.value = value }
}
private final class NativeOperation: NSObject { let cancellation = OperationCancellation() }
private func retain(_ value: AnyObject) -> UnsafeMutableRawPointer { Unmanaged.passRetained(value).toOpaque() }
private func object<T: AnyObject>(_ pointer: UnsafeMutableRawPointer, _ type: T.Type) -> T {
    Unmanaged<T>.fromOpaque(pointer).takeUnretainedValue()
}
@_cdecl("dictate_release") public func releaseObject(_ pointer: UnsafeMutableRawPointer) { Unmanaged<AnyObject>.fromOpaque(pointer).release() }
@_cdecl("dictate_free") public func freeBuffer(_ pointer: UnsafeMutableRawPointer) { free(pointer) }
@_cdecl("dictate_operation") public func operation() -> UnsafeMutableRawPointer { retain(NativeOperation()) }
@_cdecl("dictate_cancel") public func cancelOperation(_ pointer: UnsafeMutableRawPointer) { object(pointer, NativeOperation.self).cancellation.cancel() }
@_cdecl("dictate_target") public func captureTarget() -> UnsafeMutableRawPointer { retain(NativeTarget()) }
@_cdecl("dictate_target_name") public func targetName(_ pointer: UnsafeMutableRawPointer) -> UnsafeMutablePointer<CChar>? {
    let target = object(pointer, NativeTarget.self).value
    return target.app?.bundle.map { strdup($0)! }
}
public typealias ResultCallback = @convention(c) (UnsafePointer<CChar>, UnsafeMutableRawPointer) -> Void
@_cdecl("dictate_insert") public func insertText(_ pointer: UnsafeMutableRawPointer, _ text: UnsafePointer<CChar>, _ raw: UnsafePointer<CChar>,
                                         _ operation: UnsafeMutableRawPointer, _ callback: ResultCallback, _ context: UnsafeMutableRawPointer) {
    let target = object(pointer, NativeTarget.self).value
    let cancellation = object(operation, NativeOperation.self).cancellation
    Insertion.insert(string(text), raw: string(raw), target: target, cancellation: cancellation) { result in
        var response: [String: Any] = ["outcome": result.outcome.rawValue, "posted_units": result.postedUnits]
        if let evidence = result.verification { response["evidence"] = evidence.fields }
        if let snapshot = result.snapshot { response["correction"] = UInt(bitPattern: retain(NativeCorrection(snapshot))) }
        let buffer = json(response)
        callback(buffer, context)
        free(buffer)
    }
}
@_cdecl("dictate_correct") public func correctText(_ pointer: UnsafeMutableRawPointer, _ restore: Int32, _ operation: UnsafeMutableRawPointer,
                                           _ callback: ResultCallback, _ context: UnsafeMutableRawPointer) {
    let snapshot = object(pointer, NativeCorrection.self).value
    let cancellation = object(operation, NativeOperation.self).cancellation
    Insertion.correct(snapshot, restoreRaw: restore != 0, cancellation: cancellation) { result in
        let buffer = json(["verified": result == .verified])
        callback(buffer, context)
        free(buffer)
    }
}
@_cdecl("dictate_selected_text") public func selectedText() -> UnsafeMutablePointer<CChar>? {
    guard let element = Insertion.focusedElement() else { return nil }
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(element, kAXSelectedTextAttribute as CFString, &value) == .success, let text = value as? String else { return nil }
    return strdup(text)
}

private let recorder = Recorder()
public typealias LevelCallback = @convention(c) (Float) -> Void
public typealias NoticeCallback = @convention(c) (UnsafePointer<CChar>) -> Void
@_cdecl("dictate_audio_start") public func startAudio(_ level: LevelCallback, _ notice: NoticeCallback) -> UnsafeMutablePointer<CChar>? {
    guard AVCaptureDevice.authorizationStatus(for: .audio) == .authorized else { return strdup("Allow microphone access in Dictate Settings.") }
    recorder.onLevel = { level($0) }
    recorder.onInterrupted = { message in message.withCString { notice($0) } }
    do { try recorder.start(); return nil } catch { return strdup(error.localizedDescription) }
}
@_cdecl("dictate_audio_stop") public func stopAudio(_ count: UnsafeMutablePointer<Int32>) -> UnsafeMutablePointer<Float>? {
    let samples = recorder.stop()
    count.pointee = Int32(samples.count)
    guard !samples.isEmpty else { return nil }
    let pointer = malloc(samples.count * MemoryLayout<Float>.stride)!.assumingMemoryBound(to: Float.self)
    samples.withUnsafeBufferPointer { pointer.initialize(from: $0.baseAddress!, count: samples.count) }
    return pointer
}

private let keyboard = KeyboardMonitor()
private var active = false
public typealias KeyCallback = @convention(c) (Int32) -> Void
@_cdecl("dictate_keyboard_start") public func startKeyboard(_ callback: KeyCallback) -> Int32 {
    keyboard.onFnDown = { _ in callback(0) }
    keyboard.onFnUp = { _ in callback(1) }
    keyboard.onFnSpace = { _ in callback(2) }
    keyboard.onEscape = { callback(3) }
    keyboard.onPasteLast = { callback(4) }
    keyboard.onCaptureWord = { callback(5) }
    keyboard.onOtherKeyWithFn = { callback(6) }
    keyboard.onTapReset = { callback(3) }
    keyboard.isDictationActive = { active }
    return keyboard.start() ? 1 : 0
}
@_cdecl("dictate_keyboard_stop") public func stopKeyboard() { keyboard.stop(); active = false }
@_cdecl("dictate_keyboard_active") public func setActive(_ value: Int32) { active = value != 0 }
@_cdecl("dictate_permissions") public func permissions(_ request: Int32) -> UnsafeMutablePointer<CChar> {
    if request != 0 {
        AVCaptureDevice.requestAccess(for: .audio) { _ in }
        _ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary)
    }
    return json(["microphone": AVCaptureDevice.authorizationStatus(for: .audio) == .authorized,
                 "accessibility": AXIsProcessTrusted(), "keyboard": keyboard.isRunning,
                 "fn_free": (CFPreferencesCopyAppValue("AppleFnUsageType" as CFString, "com.apple.HIToolbox" as CFString) as? Int) == 0])
}

private func accountQuery(_ account: String) -> [String: Any] {
    [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "dev.local.dictate", kSecAttrAccount as String: account]
}
@_cdecl("dictate_secret_read") public func readSecret(_ account: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar> {
    var query = accountQuery(string(account))
    query[kSecReturnData as String] = true
    query[kSecMatchLimit as String] = kSecMatchLimitOne
    var result: CFTypeRef?
    let status = SecItemCopyMatching(query as CFDictionary, &result)
    if status == errSecItemNotFound { return json([:]) }
    guard status == errSecSuccess, let data = result as? Data, let value = String(data: data, encoding: .utf8) else { return json(["error": "Keychain could not read the credential (\(status))."]) }
    return json(["value": value])
}
@_cdecl("dictate_secret_write") public func writeSecret(_ account: UnsafePointer<CChar>, _ value: UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    var query = accountQuery(string(account))
    let status: OSStatus
    if let value {
        let attributes = [kSecValueData as String: Data(string(value).utf8)]
        let update = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if update == errSecItemNotFound {
            query[kSecValueData as String] = attributes[kSecValueData as String]
            status = SecItemAdd(query as CFDictionary, nil)
        } else { status = update }
    } else { status = SecItemDelete(query as CFDictionary) }
    guard status == errSecSuccess || (value == nil && status == errSecItemNotFound) else { return strdup("Keychain could not save the credential (\(status)).") }
    return nil
}
@_cdecl("dictate_legacy_settings") public func legacySettings() -> UnsafeMutablePointer<CChar> {
    let defaults = UserDefaults.standard
    func flag(_ key: String, _ fallback: Bool) -> Bool { defaults.object(forKey: key) as? Bool ?? fallback }
    var settings: [String: Any] = [
        "rules": ["fillers": flag("removeFillers", true), "repeats": flag("removeRepeats", true), "scratch_that": flag("scratchThat", true)],
        "smart_cleanup": flag("smartCleanup", true), "provider": defaults.string(forKey: "cleanupProvider") ?? "groq",
        "speech_engine": defaults.string(forKey: "speechModel") == "whisper" ? "whisper" : "whistle",
        "open_at_login": SMAppService.mainApp.status == .enabled
    ]
    if let dictionary = defaults.data(forKey: "dictionary"), let entries = try? JSONSerialization.jsonObject(with: dictionary) { settings["dictionary"] = entries }
    return json(settings)
}
