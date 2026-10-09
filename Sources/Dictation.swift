// Shared UI state and the dictation flow: hold fn to talk, double-tap fn or fn+Space for hands-free.

import AppKit
import AVFoundation
import Combine
import SwiftUI

struct HistoryItem: Codable, Identifiable {
    var id = UUID()
    let text: String
    let date: Date
    /// The transcript before cleanup and the dictionary. Nil when nothing changed.
    var raw: String?
}

enum ModelStatus: Equatable { case loading, ready, failed(String) }

final class AppModel: ObservableObject {
    enum Phase: Equatable { case idle, listening, processing, notice(String) }

    @Published var phase = Phase.idle
    @Published var handsFree = false
    @Published var levels = [CGFloat](repeating: 0, count: 11)
    @Published var modelStatus = ModelStatus.loading
    @Published var micAuthorized = false
    @Published var accessibilityTrusted = false
    @Published var keyboardReady = false
    @Published var fnKeyFree = false
    @Published var removeFillers = AppModel.stored("removeFillers", default: true) { didSet { store("removeFillers", removeFillers) } }
    @Published var removeRepeats = AppModel.stored("removeRepeats", default: true) { didSet { store("removeRepeats", removeRepeats) } }
    @Published var scratchThat = AppModel.stored("scratchThat", default: true) { didSet { store("scratchThat", scratchThat) } }
    @Published var smartCleanup = AppModel.stored("smartCleanup", default: true) { didSet { store("smartCleanup", smartCleanup) } }
    @Published var smartStatus = SmartCleanup.Status.off
    @Published var showIdleBar = AppModel.stored("showIdleBar", default: true) { didSet { store("showIdleBar", showIdleBar) } }
    @Published var dictionary: [DictionaryEntry] = AppModel.decoded("dictionary") ?? [] { didSet { encode("dictionary", dictionary) } }
    @Published private(set) var history: [HistoryItem] = AppModel.decoded("history") ?? []

    var needsSetup: Bool { !micAuthorized || !accessibilityTrusted || !keyboardReady || !fnKeyFree }
    var cleanupRules: TextCleanup.Rules { .init(fillers: removeFillers, repeats: removeRepeats, scratchThat: scratchThat) }

    func addHistory(_ text: String, raw: String) {
        history.insert(HistoryItem(text: text, date: Date(), raw: raw == text ? nil : raw), at: 0)
        history = Array(history.prefix(50))
        Self.encode("history", history)
    }

    private static func stored(_ key: String, default value: Bool) -> Bool {
        UserDefaults.standard.object(forKey: key) as? Bool ?? value
    }
    private func store(_ key: String, _ value: Bool) { UserDefaults.standard.set(value, forKey: key) }
    private static func decoded<T: Decodable>(_ key: String) -> T? {
        UserDefaults.standard.data(forKey: key).flatMap { try? JSONDecoder().decode(T.self, from: $0) }
    }
    private func encode<T: Encodable>(_ key: String, _ value: T) { Self.encode(key, value) }
    private static func encode<T: Encodable>(_ key: String, _ value: T) {
        UserDefaults.standard.set(try? JSONEncoder().encode(value), forKey: key)
    }
}

final class DictationController {
    let model: AppModel
    let keyboard = KeyboardMonitor()
    private let engine = Engine()
    private let recorder = Recorder()
    private let smart = SmartCleanup()
    private var smartToggle: AnyCancellable?
    private let startSound = NSSound(named: "Tink")

    private enum Mode { case idle, holding(Date), awaitingSecondTap, handsFree, processing }
    private var mode = Mode.idle
    private var secondTapTimeout: DispatchWorkItem?
    private var autoStop: DispatchWorkItem?
    private var recording: Stopwatch?
    private var permissionTimer: Timer?
    /// Identifies the current dictation, so a result that arrives after Esc is dropped.
    private var requestID = 0

    private let maxRecordingSeconds = 300.0  // Ends a hands-free session that was left running.
    private let minSpeechSamples = 8000      // 0.5 s at 16 kHz.
    private let holdThreshold = 0.3          // A shorter fn press counts as a tap.
    private let doubleTapWindow = 0.35

    init(model: AppModel) {
        self.model = model
        keyboard.onFnDown = { [weak self] in self?.fnDown() }
        keyboard.onFnUp = { [weak self] in self?.fnUp() }
        keyboard.onFnSpace = { [weak self] in self?.fnSpace() }
        keyboard.onOtherKeyWithFn = { [weak self] in self?.otherKeyWithFn() }
        keyboard.onEscape = { [weak self] in self?.cancel() }
        keyboard.onPasteLast = { [weak self] in self?.pasteLast() }
        keyboard.onTapReset = { [weak self] in self?.tapReset() }
        keyboard.isDictationActive = { [weak self] in self?.isActive ?? false }
        recorder.onLevel = { [weak self] rms in DispatchQueue.main.async { self?.pushLevel(rms) } }
        recorder.onInterrupted = { [weak self] reason in self?.finish(interruption: reason) }
    }

    func start() {
        refreshPermissions()
        engine.load { [weak self] error in
            self?.model.modelStatus = error.map { .failed($0.localizedDescription) } ?? .ready
        }
        smart.onStatus = { [weak self] status in self?.model.smartStatus = status }
        smartToggle = model.$smartCleanup.removeDuplicates().sink { [weak self] on in self?.smart.setEnabled(on) }
        NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil,
                                               queue: .main) { [weak self] _ in self?.refreshPermissions() }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil,
                                                          queue: .main) { [weak self] _ in self?.refreshPermissions() }
    }

    // MARK: Keyboard events

    private func fnDown() {
        switch mode {
        case .idle: begin(handsFree: false)
        case .awaitingSecondTap: enterHandsFree()
        case .handsFree: finish()
        case .holding, .processing: break
        }
    }

    private func fnUp() {
        guard case .holding(let started) = mode else { return }
        if Date().timeIntervalSince(started) >= holdThreshold {
            finish()
            return
        }
        // A short tap: keep recording briefly in case a second tap starts hands-free mode.
        mode = .awaitingSecondTap
        let work = DispatchWorkItem { [weak self] in
            if case .awaitingSecondTap = self?.mode { self?.cancel() }
        }
        secondTapTimeout = work
        DispatchQueue.main.asyncAfter(deadline: .now() + doubleTapWindow, execute: work)
    }

    private func fnSpace() {
        switch mode {
        case .idle: begin(handsFree: true)
        case .holding, .awaitingSecondTap: enterHandsFree()
        case .handsFree: finish()
        case .processing: break
        }
    }

    /// fn combined with another key (fn+arrow, fn+Delete) is not dictation.
    private func otherKeyWithFn() {
        if case .holding = mode { cancel() }
    }

    /// The fn release may have been missed. A hold-to-talk recording cannot know when to stop, so drop it.
    private func tapReset() {
        switch mode {
        case .holding, .awaitingSecondTap: cancel()
        case .idle, .handsFree, .processing: break
        }
        refreshPermissions()
    }

    // MARK: Flow

    private var isRecording: Bool {
        switch mode {
        case .holding, .awaitingSecondTap, .handsFree: return true
        case .idle, .processing: return false
        }
    }

    private var isActive: Bool {
        if case .idle = mode { return false }
        return true
    }

    private func begin(handsFree: Bool) {
        refreshPermissions()
        guard model.micAuthorized else {
            notice("Allow microphone access in Dictate")
            return
        }
        do { try recorder.start() } catch {
            notice("Microphone error: \(error.localizedDescription)")
            return
        }
        startSound?.play()  // After the microphone runs, so the sound means "speak now".
        recording = Stopwatch()
        mode = handsFree ? .handsFree : .holding(Date())
        model.handsFree = handsFree
        model.levels = model.levels.map { _ in 0 }
        model.phase = .listening
        let work = DispatchWorkItem { [weak self] in self?.finish() }
        autoStop = work
        DispatchQueue.main.asyncAfter(deadline: .now() + maxRecordingSeconds, execute: work)
    }

    private func enterHandsFree() {
        secondTapTimeout?.cancel()
        mode = .handsFree
        model.handsFree = true
    }

    /// Ends the recording and inserts the text. `interruption` explains a recording that stopped early.
    func finish(interruption: String? = nil) {
        guard isRecording else { return }
        let pcm = recorder.stop()
        let recordCpuMs = recording?.cpuMs ?? 0
        clearTimers()
        guard pcm.count >= minSpeechSamples else {
            reset()
            if let interruption { notice(interruption) }
            return
        }
        // The field that has focus now is where the text belongs.
        let target = Insertion.focusedElement()
        requestID += 1
        let request = requestID
        mode = .processing
        model.handsFree = false
        model.phase = .processing
        let audioSeconds = Double(pcm.count) / 16000
        let released = Stopwatch()
        engine.transcribe(pcm) { [weak self] result in
            guard let self, request == self.requestID else { return }  // Cancelled with Esc.
            switch result {
            case .success(let raw):
                self.model.modelStatus = .ready
                var metrics: [String: Any] = [
                    "audio_s": (audioSeconds * 10).rounded() / 10,
                    "record_cpu_ms": recordCpuMs, "record_cpu_pct": Int(Double(recordCpuMs) / 10 / max(audioSeconds, 0.1)),
                    "transcribe_ms": released.wallMs, "transcribe_cpu_ms": released.cpuMs,
                ]
                if let interruption { metrics["interrupted"] = interruption }
                // The rules and the dictionary give the fallback text when smart cleanup is off, slow, or rejected.
                let ruled = PersonalDictionary.apply(TextCleanup.clean(raw, rules: self.model.cleanupRules),
                                                     entries: self.model.dictionary)
                guard self.model.smartCleanup, !raw.isEmpty else {
                    return self.complete(text: ruled, raw: raw, target: target, released: released,
                                         metrics: metrics, interruption: interruption)
                }
                let smartStarted = Stopwatch()
                self.smart.clean(raw, entries: self.model.dictionary) { [weak self] text, outcome in
                    guard let self, request == self.requestID else { return }
                    metrics["smart"] = outcome
                    metrics["smart_ms"] = smartStarted.wallMs
                    self.complete(text: text ?? ruled, raw: raw, target: target, released: released,
                                  metrics: metrics, interruption: interruption)
                }
            case .failure(let error):
                self.mode = .idle
                self.notice(error.localizedDescription)
            }
        }
    }

    private func complete(text: String, raw: String, target: AXUIElement?, released: Stopwatch,
                          metrics: [String: Any], interruption: String?) {
        mode = .idle
        var metrics = metrics
        metrics["chars"] = text.count
        metrics["chars_changed"] = raw.count - text.count
        guard !text.isEmpty else {
            MetricsLog.write(metrics)
            notice(interruption ?? "No speech detected")
            return
        }
        model.addHistory(text, raw: raw)
        // Transcription alone takes about 25 ms. A slow first run or smart cleanup leaves time to switch fields.
        if released.wallMs > 1000, let current = Insertion.focusedElement(), let target, !Insertion.isSame(current, target) {
            MetricsLog.write(metrics)
            notice("The focused field changed. Press ⌃⌘V to insert.")
            return
        }
        deliver(text) { typeMs in
            metrics["type_ms"] = typeMs
            metrics["release_to_text_ms"] = released.wallMs
            MetricsLog.write(metrics)
        }
        if let interruption { notice("\(interruption). Recording stopped early.") }
    }

    /// Esc: drops the recording, or the result that is still being transcribed.
    func cancel() {
        switch mode {
        case .holding, .awaitingSecondTap, .handsFree:
            _ = recorder.stop()
            clearTimers()
        case .processing:
            requestID += 1
        case .idle:
            return
        }
        reset()
    }

    func pasteLast() {
        guard case .idle = mode, let last = model.history.first else { return }
        deliver(last.text)
    }

    private func deliver(_ text: String, completion: @escaping (Int) -> Void = { _ in }) {
        guard AXIsProcessTrusted() else {
            notice("Allow Accessibility in Dictate to insert text")
            return
        }
        Insertion.insert(text, completion: completion)
        model.phase = .idle
    }

    private func reset() {
        mode = .idle
        model.handsFree = false
        model.phase = .idle
    }

    private func clearTimers() {
        secondTapTimeout?.cancel()
        autoStop?.cancel()
    }

    private func notice(_ text: String) {
        model.handsFree = false
        model.phase = .notice(text)
        DispatchQueue.main.asyncAfter(deadline: .now() + 3) { [weak self] in
            if self?.model.phase == .notice(text) { self?.model.phase = .idle }
        }
    }

    private func pushLevel(_ rms: Float) {
        guard model.phase == .listening else { return }
        let db = 20 * log10(max(rms, 1e-6))
        let level = CGFloat(min(1, max(0, (db + 55) / 40)))
        model.levels = Array(model.levels.dropFirst()) + [level]
    }

    // MARK: Permissions

    func refreshPermissions() {
        let mic = AVCaptureDevice.authorizationStatus(for: .audio) == .authorized
        let trusted = AXIsProcessTrusted()
        CFPreferencesAppSynchronize("com.apple.HIToolbox" as CFString)
        // 0 means "Press fn key to: Do Nothing", so macOS does not open the emoji picker or Apple Dictation.
        let fnFree = (CFPreferencesCopyAppValue("AppleFnUsageType" as CFString, "com.apple.HIToolbox" as CFString) as? Int) == 0
        if model.micAuthorized != mic { model.micAuthorized = mic }
        if model.accessibilityTrusted != trusted { model.accessibilityTrusted = trusted }
        if model.fnKeyFree != fnFree { model.fnKeyFree = fnFree }
        let keys = trusted && keyboard.start()
        if model.keyboardReady != keys { model.keyboardReady = keys }
        // Poll only while setup is incomplete, so the Hub updates as permissions are granted. Idle otherwise.
        if model.needsSetup, permissionTimer == nil {
            permissionTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.refreshPermissions() }
        } else if !model.needsSetup {
            permissionTimer?.invalidate()
            permissionTimer = nil
        }
    }

    func requestMicrophone() {
        if AVCaptureDevice.authorizationStatus(for: .audio) == .notDetermined {
            AVCaptureDevice.requestAccess(for: .audio) { _ in DispatchQueue.main.async { self.refreshPermissions() } }
        } else {
            openSettings("x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone")
        }
    }

    func requestAccessibility() {
        _ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue(): true] as CFDictionary)
        openSettings("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
    }

    func openKeyboardSettings() {
        openSettings("x-apple.systempreferences:com.apple.Keyboard-Settings.extension")
    }

    private func openSettings(_ url: String) {
        if let url = URL(string: url) { NSWorkspace.shared.open(url) }
    }
}
