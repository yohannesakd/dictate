// Shared UI state and the dictation flow: hold fn to talk, double-tap fn or fn+Space for hands-free.

import AppKit
import AVFoundation
import SwiftUI

struct HistoryItem: Codable, Identifiable {
    var id = UUID()
    let text: String
    let date: Date
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
    @Published var fnKeyFree = false
    @Published var removeFillers = UserDefaults.standard.object(forKey: "removeFillers") as? Bool ?? true {
        didSet { UserDefaults.standard.set(removeFillers, forKey: "removeFillers") }
    }
    @Published var showIdleBar = UserDefaults.standard.object(forKey: "showIdleBar") as? Bool ?? true {
        didSet { UserDefaults.standard.set(showIdleBar, forKey: "showIdleBar") }
    }
    @Published private(set) var history: [HistoryItem] =
        (UserDefaults.standard.data(forKey: "history").flatMap { try? JSONDecoder().decode([HistoryItem].self, from: $0) }) ?? []

    var needsSetup: Bool { !micAuthorized || !accessibilityTrusted || !fnKeyFree }

    func addHistory(_ text: String) {
        history.insert(HistoryItem(text: text, date: Date()), at: 0)
        history = Array(history.prefix(50))
        UserDefaults.standard.set(try? JSONEncoder().encode(history), forKey: "history")
    }
}

final class DictationController {
    let model: AppModel
    let keyboard = KeyboardMonitor()
    private let engine = Engine()
    private let recorder = Recorder()
    private let startSound = NSSound(named: "Tink")

    private enum Mode { case idle, holding(Date), awaitingSecondTap, handsFree, processing }
    private var mode = Mode.idle
    private var secondTapTimeout: DispatchWorkItem?
    private var autoStop: DispatchWorkItem?
    private var recording: Stopwatch?
    private var permissionTimer: Timer?

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
        keyboard.isDictationActive = { [weak self] in self?.isRecording ?? false }
        recorder.onLevel = { [weak self] rms in DispatchQueue.main.async { self?.pushLevel(rms) } }
    }

    func start() {
        refreshPermissions()
        engine.load { [weak self] error in
            self?.model.modelStatus = error.map { .failed($0.localizedDescription) } ?? .ready
        }
        NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil,
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

    // MARK: Flow

    private func begin(handsFree: Bool) {
        refreshPermissions()
        guard model.micAuthorized else {
            notice("Allow microphone access in Dictate")
            return
        }
        startSound?.play()
        do { try recorder.start() } catch {
            notice("Microphone error: \(error.localizedDescription)")
            return
        }
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

    private var isRecording: Bool {
        switch mode {
        case .holding, .awaitingSecondTap, .handsFree: return true
        case .idle, .processing: return false
        }
    }

    func finish() {
        guard isRecording else { return }
        let pcm = recorder.stop()
        let recordCpuMs = recording?.cpuMs ?? 0
        clearTimers()
        guard pcm.count >= minSpeechSamples else {
            reset()
            return
        }
        mode = .processing
        model.handsFree = false
        model.phase = .processing
        let audioSeconds = Double(pcm.count) / 16000
        let transcription = Stopwatch()
        engine.transcribe(pcm) { [weak self] result in
            guard let self else { return }
            self.mode = .idle
            switch result {
            case .success(let raw):
                self.model.modelStatus = .ready
                let transcribeMs = transcription.wallMs, transcribeCpuMs = transcription.cpuMs
                let cleanup = Stopwatch()
                let text = self.model.removeFillers ? TextCleanup.clean(raw) : raw
                MetricsLog.write([
                    "audio_s": (audioSeconds * 10).rounded() / 10,
                    "record_cpu_ms": recordCpuMs, "record_cpu_pct": Int(Double(recordCpuMs) / 10 / max(audioSeconds, 0.1)),
                    "transcribe_ms": transcribeMs, "transcribe_cpu_ms": transcribeCpuMs,
                    "cleanup_ms": cleanup.wallMs, "chars": text.count, "chars_removed": raw.count - text.count,
                ])
                if text.isEmpty {
                    self.notice("No speech detected")
                } else {
                    self.model.addHistory(text)
                    self.deliver(text)
                }
            case .failure(let error):
                self.notice(error.localizedDescription)
            }
        }
    }

    func cancel() {
        guard isRecording else { return }
        _ = recorder.stop()
        clearTimers()
        reset()
    }

    func pasteLast() {
        if let last = model.history.first { deliver(last.text) }
    }

    private func deliver(_ text: String) {
        if AXIsProcessTrusted() {
            typeText(text)
            model.phase = .idle
        } else {
            notice("Allow Accessibility in Dictate to insert text")
        }
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
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.5) { [weak self] in
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
        if trusted, !keyboard.isRunning { keyboard.start() }
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
