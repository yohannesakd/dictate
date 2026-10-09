// Shared UI state and the dictation flow: hold fn to talk, double-tap fn or fn+Space for hands-free.

import AppKit
import AVFoundation
import ServiceManagement
import SwiftUI

struct HistoryItem: Codable, Identifiable {
    var id = UUID()
    let text: String
    let date: Date
    /// The transcript before cleanup and the dictionary. Nil when nothing changed.
    var raw: String?
    /// Why smart cleanup did not change the text, for example a timeout.
    var note: String?
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
    @Published var cleanupProvider = CleanupProvider(rawValue: UserDefaults.standard.string(forKey: "cleanupProvider") ?? "") ?? .deepseek {
        didSet { UserDefaults.standard.set(cleanupProvider.rawValue, forKey: "cleanupProvider") }
    }
    @Published var deepseekKeySaved = false
    @Published var chatgptSignedIn = false
    @Published var signingIn = false
    /// The last smart cleanup error. A successful request clears it.
    @Published var smartIssue: String?
    @Published var openAtLogin = SMAppService.mainApp.status == .enabled {
        didSet {
            guard openAtLogin != (SMAppService.mainApp.status == .enabled) else { return }
            try? openAtLogin ? SMAppService.mainApp.register() : SMAppService.mainApp.unregister()
            // Show the real state, also when macOS refused the change.
            DispatchQueue.main.async { self.openAtLogin = SMAppService.mainApp.status == .enabled }
        }
    }
    @Published var showIdleBar = AppModel.stored("showIdleBar", default: true) { didSet { store("showIdleBar", showIdleBar) } }
    @Published var dictionary: [DictionaryEntry] = AppModel.decoded("dictionary") ?? [] { didSet { encode("dictionary", dictionary) } }
    @Published private(set) var history: [HistoryItem] = AppModel.decoded("history") ?? []
    @Published private(set) var usage: Usage = AppModel.decoded("usage") ?? Usage()

    var needsSetup: Bool { !micAuthorized || !accessibilityTrusted || !keyboardReady || !fnKeyFree }
    var cleanupRules: TextCleanup.Rules { .init(fillers: removeFillers, repeats: removeRepeats, scratchThat: scratchThat) }

    func addHistory(_ text: String, raw: String, note: String?) {
        history.insert(HistoryItem(text: text, date: Date(), raw: raw == text ? nil : raw, note: note), at: 0)
        history = Array(history.prefix(50))
        Self.encode("history", history)
    }

    func recordDictation(words: Int, seconds: Double) {
        usage.addDictation(words: words, seconds: seconds)
        Self.encode("usage", usage)
    }

    func recordLatency(_ ms: Int) {
        usage.addLatency(ms)
        Self.encode("usage", usage)
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
    private let startSound = NSSound(named: "Tink")

    private enum Mode { case idle, holding(Double), awaitingSecondTap, handsFree, processing }
    private var mode = Mode.idle
    private var secondTapTimeout: DispatchWorkItem?
    private var autoStop: DispatchWorkItem?
    private var recording: Stopwatch?
    private var permissionTimer: Timer?
    /// Identifies the current dictation, so a result that arrives after Esc is dropped.
    private var requestID = 0
    private var operation = OperationCancellation()
    private var snapshotCancellation: OperationCancellation?
    private var lastInsertion: Insertion.Snapshot?
    private var trace = Trace()

    var canCorrectLast: Bool {
        guard !isActive, let snapshot = lastInsertion else { return false }
        return Monotonic.now - snapshot.created <= Insertion.correctionLifetime
    }
    var canRestoreRaw: Bool {
        guard canCorrectLast, let snapshot = lastInsertion else { return false }
        let inserted = (snapshot.state.value as NSString).substring(with: snapshot.range)
        return !inserted.utf16.elementsEqual(snapshot.rawReplacement.utf16)
    }

    // Transcription during recording. The Neural Engine slows down after about 2 s of idle time: 5.6 s of audio
    // took 22 ms warm and 66-80 ms after 2-30 s idle. Transcribing the audio so far keeps it warm, and a transcript
    // taken in the pause before fn release starts smart cleanup early. The final transcript reuses that request
    // when it is the same.
    private var snapshotRunning = false
    private var lastSnapshot = -Double.infinity
    private var lastSpeech = -Double.infinity
    private var spokeSinceSnapshot = false
    private var speculation: SmartCleanup.Pending?
    private var activeCleanup: SmartCleanup.Pending?
    private let snapshotInterval = 1.0       // Keeps the Neural Engine warm while the user speaks without pauses.
    private let pauseForSnapshot = 0.2       // Silence after speech that starts a speculative cleanup.
    private let speechLevelDB: Float = -45
    private let maxSnapshotSamples = 30 * 16000  // Longer audio is transcribed in windows; snapshots would be slow.

    private let maxRecordingSeconds = 300.0  // Ends a hands-free session that was left running.
    private let minSpeechSamples = 8000      // 0.5 s at 16 kHz.
    private let holdThreshold = 0.3          // A shorter fn press counts as a tap.
    private let doubleTapWindow = 0.35

    init(model: AppModel) {
        self.model = model
        keyboard.onFnDown = { [weak self] observed in self?.fnDown(observed: observed) }
        keyboard.onFnUp = { [weak self] observed in self?.fnUp(observed: observed) }
        keyboard.onFnSpace = { [weak self] observed in self?.fnSpace(observed: observed) }
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
        model.deepseekKeySaved = smart.deepseek.hasKey
        model.chatgptSignedIn = smart.chatgpt.auth.isSignedIn
        // Open at login by default. The switch in the Hub changes it later.
        if !UserDefaults.standard.bool(forKey: "openAtLoginSet") {
            UserDefaults.standard.set(true, forKey: "openAtLoginSet")
            model.openAtLogin = true
        }
        NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil,
                                               queue: .main) { [weak self] _ in self?.refreshPermissions() }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil,
                                                          queue: .main) { [weak self] _ in self?.refreshPermissions() }
    }

    // MARK: Keyboard events

    private func fnDown(observed: Double) {
        switch mode {
        case .idle: begin(handsFree: false, pressed: observed)
        case .awaitingSecondTap: enterHandsFree()
        case .handsFree: finish(observed: observed)
        case .holding, .processing: break
        }
    }

    private func fnUp(observed: Double) {
        guard case .holding(let started) = mode else { return }
        if observed - started >= holdThreshold {
            finish(observed: observed)
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

    private func fnSpace(observed: Double) {
        switch mode {
        case .idle: begin(handsFree: true, pressed: observed)
        case .holding, .awaitingSecondTap: enterHandsFree()
        case .handsFree: finish(observed: observed)
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

    private func begin(handsFree: Bool, pressed: Double) {
        refreshPermissions()
        guard model.micAuthorized else {
            notice("Allow microphone access in Dictate")
            return
        }
        requestID += 1
        operation.cancel()
        operation = OperationCancellation()
        snapshotCancellation?.cancel()
        snapshotRunning = false
        lastInsertion = nil
        if let activeCleanup { smart.cancel(activeCleanup) }
        activeCleanup = nil
        do { try recorder.start() } catch {
            notice("Microphone error: \(error.localizedDescription)")
            return
        }
        trace = Trace(pressed: pressed)
        trace.mark("mic_ready")
        startSound?.play()  // After the microphone runs, so the sound means "speak now".
        if model.smartCleanup { smart.client(model.cleanupProvider).warm() }
        recording = Stopwatch()
        lastSnapshot = Monotonic.now
        spokeSinceSnapshot = false
        mode = handsFree ? .handsFree : .holding(Monotonic.now)
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
    func finish(interruption: String? = nil, observed: Double? = nil) {
        guard isRecording else { return }
        let released = Stopwatch(started: observed ?? Monotonic.now)
        trace.mark("release", at: released.started)
        let queueDelay = released.wallMs
        let shutdown = Stopwatch()
        let pcm = recorder.stop()
        let shutdownMs = shutdown.wallMs
        snapshotCancellation?.cancel()
        snapshotRunning = false
        let recordingStarted = recording?.started ?? Monotonic.now
        let recordCpuMs = recording?.cpuMs ?? 0
        clearTimers()
        let speculative = speculation
        speculation = nil
        guard pcm.count >= minSpeechSamples else {
            if let speculative { smart.cancel(speculative) }
            reset()
            if let interruption { notice(interruption) }
            return
        }
        let request = requestID
        mode = .processing
        let audioSeconds = Double(pcm.count) / 16000
        let handsFree = model.handsFree
        var trace = self.trace
        trace.mark("recorder_stopped")
        // Transcription runs while the main thread looks up the focus. Its result arrives on the main queue after this.
        let context = ReleaseContext()
        engine.transcribe(pcm, cancellation: operation) { [weak self] result, timing in
            guard let self, request == self.requestID else { return }  // Cancelled with Esc.
            switch result {
            case .success(let raw):
                self.model.modelStatus = .ready
                trace.mark("transcript")
                var dictation = Dictation(raw: raw, app: context.target.app?.bundle, audioSeconds: audioSeconds, target: context.target, released: released)
                dictation.metrics = [
                    "audio_s": (audioSeconds * 10).rounded() / 10,
                    "record_cpu_ms": recordCpuMs, "record_cpu_pct": Int(Double(recordCpuMs) / 10 / max(audioSeconds, 0.1)),
                    "recognition_pipeline_ms": released.wallMs - queueDelay - shutdownMs,
                    "engine_queue_ms": timing.queueMs, "inference_ms": timing.inferenceMs,
                    "model_prepare_ms": timing.preparationMs,
                    "recognition_callback_ms": timing.callbackMs,
                    "release_queue_ms": queueDelay, "shutdown_ms": shutdownMs,
                ]
                if let interruption { dictation.metrics["interrupted"] = interruption }
                dictation.metrics["hands_free"] = handsFree
                let provider = self.model.cleanupProvider
                let pending = self.model.smartCleanup && !raw.isEmpty
                    ? self.smart.request(raw, entries: self.model.dictionary, provider: provider) : nil
                if let speculative, speculative !== pending { self.smart.cancel(speculative) }
                // The rules and the dictionary give the text when smart cleanup is off, not needed, slow, or rejected.
                dictation.ruled = PersonalDictionary.apply(TextCleanup.clean(raw, rules: self.model.cleanupRules),
                                                           entries: self.model.dictionary)
                trace.mark("rules_done")
                guard let pending else {
                    if self.model.smartCleanup { dictation.metrics["smart"] = "not needed" }
                    dictation.trace = trace
                    return self.complete(dictation, interruption: interruption)
                }
                self.activeCleanup = pending
                dictation.metrics["smart_cache_hit"] = pending.result?.text != nil && pending.started < recordingStarted
                // How long the request had already run when the final transcript was ready.
                dictation.metrics["smart_head_start_ms"] = pending === speculative && pending.started >= recordingStarted
                    ? Monotonic.milliseconds(since: pending.started) : 0
                // A cached reply started in an earlier dictation; its times do not belong to this timeline.
                if pending.started >= recordingStarted { trace.mark("llm_start", at: pending.started) }
                let waited = Stopwatch()
                self.smart.wait(for: pending, seconds: 3) { [weak self] smart in
                    guard let self, request == self.requestID else { return }
                    dictation.smart = smart
                    dictation.provider = provider
                    dictation.metrics["smart"] = smart.outcome
                    dictation.metrics["smart_wait_ms"] = waited.wallMs
                    if pending.started >= recordingStarted {
                        if let finished = pending.finished { trace.mark("llm_end", at: finished) }
                        dictation.metrics.merge(pending.timing.fields) { $1 }
                    }
                    dictation.trace = trace
                    if smart.outcome == "used" { self.model.smartIssue = nil }
                    if let error = smart.error { self.model.smartIssue = error.localizedDescription }
                    self.complete(dictation, interruption: interruption)
                }
            case .failure(let error):
                if let speculative { self.smart.cancel(speculative) }
                self.mode = .idle
                self.notice(error.localizedDescription)
            }
        }
        // The field that has focus now is where the text belongs.
        context.target = Insertion.captureTarget()
        model.handsFree = false
        model.phase = .processing
    }

    private final class ReleaseContext {
        var target = Insertion.Target(app: nil, element: nil)
    }

    /// Transcribes the audio so far during recording. Called with each level update.
    private func snapshotIfUseful(levelDB: Float) {
        guard isRecording else { return }
        let now = Monotonic.now
        if levelDB > speechLevelDB {
            lastSpeech = now
            spokeSinceSnapshot = true
            trace.mark("speech_start", at: now)
            trace.update("speech_end", at: now)
        }
        let paused = spokeSinceSnapshot && (now - lastSpeech) >= pauseForSnapshot
        guard !snapshotRunning, paused || (now - lastSnapshot) >= snapshotInterval else { return }
        let pcm = recorder.snapshot()
        guard pcm.count >= minSpeechSamples, pcm.count <= maxSnapshotSamples else { return }
        snapshotRunning = true
        lastSnapshot = now
        trace.snapshots += 1
        spokeSinceSnapshot = false
        let recording = requestID
        let cancellation = OperationCancellation()
        snapshotCancellation = cancellation
        engine.transcribe(pcm, cancellation: cancellation) { [weak self] result, _ in
            guard let self, recording == self.requestID else { return }
            self.snapshotRunning = false
            // Only a transcript taken in a pause is likely to be final. Mid-speech snapshots only keep the engine warm.
            guard paused, self.isRecording, recording == self.requestID, self.model.smartCleanup,
                  case .success(let raw) = result, !raw.isEmpty else { return }
            let next = self.smart.request(raw, entries: self.model.dictionary, provider: self.model.cleanupProvider)
            if let previous = self.speculation, previous !== next { self.smart.cancel(previous) }
            if let next, next !== self.speculation { self.trace.speculativeRequests += 1 }
            self.speculation = next
        }
    }

    /// One dictation on its way from the transcript to the focused field.
    private struct Dictation {
        let raw: String
        let app: String?
        let audioSeconds: Double
        let target: Insertion.Target
        let released: Stopwatch
        var ruled = ""
        var smart: SmartCleanup.Result?
        var provider: CleanupProvider?
        var metrics: [String: Any] = [:]
        var trace = Trace()

        var text: String { smart?.text ?? ruled }

        /// Explains in the history why smart cleanup did not change the text.
        var note: String? {
            switch smart?.outcome {
            case "timeout": return "Rules used: the model did not reply within 3 seconds."
            case "rejected": return "Rules used: the model reply was not a cleanup of the transcript."
            case "error": return "Rules used: \(smart?.error?.localizedDescription ?? "the model failed.")"
            default: return nil
            }
        }
    }

    private func complete(_ dictation: Dictation, interruption: String?) {
        let text = dictation.text
        var metrics = dictation.metrics
        var trace = dictation.trace
        trace.mark("text_ready")
        metrics.merge(trace.fields) { $1 }
        metrics["chars"] = text.count
        metrics["chars_changed"] = dictation.raw.count - text.count
        var log: [String: Any?] = [
            "app": dictation.app, "audio_s": (dictation.audioSeconds * 10).rounded() / 10, "raw": dictation.raw,
            "rules": dictation.ruled, "provider": dictation.provider?.rawValue, "smart": dictation.smart?.outcome,
            "model_reply": dictation.smart?.reply, "text": text, "inserted": false,
        ]
        guard !text.isEmpty else {
            reset()
            activeCleanup = nil
            MetricsLog.write(metrics)
            TranscriptLog.write(log)
            notice(interruption ?? "No speech detected")
            return
        }
        let request = requestID
        let released = dictation.released
        deliver(text, raw: dictation.raw, target: dictation.target) { [weak self] result in
            metrics["delivery_outcome"] = result.outcome.rawValue
            metrics["verification_ms"] = result.verificationMs
            metrics["posted_utf16_units"] = result.postedUnits
            if let posted = result.postedMs, let postedAt = result.postedAt {
                trace.mark("events_posted", at: postedAt)
                metrics["delivery_to_posted_events_ms"] = posted
                // The callback can arrive after verification. Use the measured event-posting timestamp.
                metrics["release_to_posted_events_ms"] = Int(max(0, (postedAt - released.started) * 1000))
            }
            if let verifiedAt = result.verifiedAt {
                trace.mark("text_verified", at: verifiedAt)
                let verified = Int(max(0, (verifiedAt - released.started) * 1000))
                metrics["release_to_verified_text_ms"] = verified
                if request == self?.requestID { self?.model.recordLatency(verified) }
            }
            metrics.merge(trace.fields) { $1 }
            log["inserted"] = result.outcome == .verified
            log["delivery_outcome"] = result.outcome.rawValue
            MetricsLog.write(metrics)
            TranscriptLog.write(log)
            if request == self?.requestID, result.outcome == .verified, let interruption {
                self?.notice("\(interruption). Recording stopped early.")
            }
        }
        model.addHistory(text, raw: dictation.raw, note: dictation.note)
        model.recordDictation(words: text.split(whereSeparator: \.isWhitespace).count, seconds: dictation.audioSeconds)
    }

    // MARK: Smart cleanup accounts

    func saveDeepSeekKey(_ key: String) {
        let key = key.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { return }
        do {
            try smart.deepseek.setKey(key)
            model.deepseekKeySaved = true
            model.smartIssue = nil
        } catch { model.smartIssue = error.localizedDescription }
    }

    func removeDeepSeekKey() {
        do {
            try smart.deepseek.setKey(nil)
            model.deepseekKeySaved = false
            model.smartIssue = nil
        } catch { model.smartIssue = error.localizedDescription }
    }

    func signInChatGPT() {
        model.signingIn = true
        Task {
            let error: Error?
            do { try await smart.chatgpt.auth.signIn(); error = nil } catch let failure { error = failure }
            await MainActor.run {
                model.signingIn = false
                model.chatgptSignedIn = smart.chatgpt.auth.isSignedIn
                model.smartIssue = error?.localizedDescription
                NSApp.activate(ignoringOtherApps: true)
            }
        }
    }

    func signOutChatGPT() {
        Task {
            await smart.chatgpt.auth.signOut()
            await MainActor.run { model.chatgptSignedIn = false }
        }
    }

    /// Esc: drops the recording, or the result that is still being transcribed.
    func cancel() {
        switch mode {
        case .holding, .awaitingSecondTap, .handsFree:
            _ = recorder.stop()
            clearTimers()
            requestID += 1  // Drops a transcript of this recording that is still on its way.
            if let speculation { smart.cancel(speculation) }
            speculation = nil
        case .processing:
            requestID += 1
        case .idle:
            return
        }
        operation.cancel()
        snapshotCancellation?.cancel()
        snapshotRunning = false
        lastInsertion = nil
        if let activeCleanup { smart.cancel(activeCleanup) }
        activeCleanup = nil
        reset()
    }

    func pasteLast() {
        guard case .idle = mode, let last = model.history.first else { return }
        requestID += 1
        operation = OperationCancellation()
        deliver(last.text, raw: last.raw ?? last.text, target: Insertion.captureTarget())
    }

    private func deliver(_ text: String, raw: String, target: Insertion.Target,
                         completion: @escaping (Insertion.Result) -> Void = { _ in }) {
        let request = requestID
        mode = .processing
        model.phase = .processing
        lastInsertion = nil
        guard AXIsProcessTrusted() else {
            reset()
            notice("Allow Accessibility in Dictate to insert text")
            completion(.init(outcome: .permissionDenied, postedMs: nil, postedAt: nil, verifiedAt: nil, postedUnits: 0, verifiedMs: nil, verificationMs: 0, snapshot: nil))
            return
        }
        Insertion.insert(text, raw: raw, target: target, cancellation: operation) { [weak self] result in
            if let self, request == self.requestID {
                self.reset()
                self.activeCleanup = nil
                self.lastInsertion = result.snapshot
                switch result.outcome {
                case .verified: break
                case .targetChanged: self.notice(result.postedUnits == 0
                    ? "The target changed. The dictation is in History."
                    : "The target changed during delivery. Check the field before inserting again.")
                case .unchanged: self.notice("The field did not change. The dictation is in History.")
                case .unexpected: self.notice("Text delivery was incomplete or changed. Check the field before inserting again.")
                case .unavailable: self.notice(result.postedUnits == 0
                    ? "The target could not be read. The dictation is in History."
                    : "Text was sent. Dictate cannot verify delivery. Check the field before inserting again.")
                case .cancelled: break
                case .eventFailed: self.notice("Text delivery stopped. Check the field before inserting again.")
                case .permissionDenied: break
                }
            }
            completion(result)
        }
    }

    func undoLastDictation() { correctLast(restoreRaw: false) }
    func restoreRawTranscript() { correctLast(restoreRaw: true) }

    private func correctLast(restoreRaw: Bool) {
        guard case .idle = mode, let snapshot = lastInsertion else { return }
        requestID += 1
        let request = requestID
        operation = OperationCancellation()
        mode = .processing
        model.phase = .processing
        Insertion.correct(snapshot, restoreRaw: restoreRaw, cancellation: operation) { [weak self] outcome in
            guard let self, request == self.requestID else { return }
            self.reset()
            if outcome != .noWrite { self.lastInsertion = nil }
            switch outcome {
            case .verified: break
            case .noWrite: self.notice("The field changed or does not support correction. No text was replaced.")
            case .uncertain: self.notice("The correction could not be verified. Check the field before editing.")
            }
            MetricsLog.write(["correction_outcome": outcome.rawValue, "restore_raw": restoreRaw])
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
        DispatchQueue.main.asyncAfter(deadline: .now() + 3) { [weak self] in
            if self?.model.phase == .notice(text) { self?.model.phase = .idle }
        }
    }

    private func pushLevel(_ rms: Float) {
        guard model.phase == .listening else { return }
        let db = 20 * log10(max(rms, 1e-6))
        trace.mark("first_audio")
        let level = CGFloat(min(1, max(0, (db + 55) / 40)))
        model.levels = Array(model.levels.dropFirst()) + [level]
        snapshotIfUseful(levelDB: db)
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
