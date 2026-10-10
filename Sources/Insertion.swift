// Delivery uses marked Unicode events. Read-back supplies evidence; uncertain delivery is never retried automatically.

import AppKit
import ApplicationServices

// The marker is shared with KeyboardMonitor, which must ignore Dictate's own events.
let dictateEventMarker: Int64 = 0x44494354415445

enum Insertion {
    private static let queue = DispatchQueue(label: "dictate.typing", qos: .userInitiated)
    static let verificationSeconds = 0.15
    static let correctionLifetime = 300.0
    static let maximumSnapshotUnits = 1_000_000

    struct AppIdentity: Equatable {
        let pid: pid_t
        let launched: Date?
        let bundle: String?
    }
    struct Target {
        let app: AppIdentity?
        let element: AXUIElement?
    }
    struct FieldState: Equatable {
        let value: String
        let selection: NSRange
        static func == (lhs: Self, rhs: Self) -> Bool {
            lhs.selection == rhs.selection && lhs.value.utf16.elementsEqual(rhs.value.utf16)
        }
    }
    enum Outcome: String {
        /// `sent`: every event was posted, but the field cannot be read back, as in most Electron apps.
        case verified, sent, unchanged, unexpected, unavailable, targetChanged, cancelled, eventFailed, permissionDenied
    }
    struct Result {
        let outcome: Outcome
        let postedMs: Int?
        let postedAt: Double?
        let verifiedAt: Double?
        let postedUnits: Int
        let verifiedMs: Int?
        let verificationMs: Int
        let snapshot: Snapshot?
        var verification: Verification? = nil
    }
    /// Read-back evidence excludes field text, which can contain unrelated private content.
    struct Verification {
        let fields: [String: Any]

        init(expected: FieldState, observed: FieldState) {
            let valueMatches = expected.value.utf16.elementsEqual(observed.value.utf16)
            let selectionMatches = expected.selection == observed.selection
            fields = [
                "verification_mismatch": valueMatches ? "selection" : selectionMatches ? "value" : "value_and_selection",
                "expected_utf16_units": expected.value.utf16.count,
                "observed_utf16_units": observed.value.utf16.count,
                "expected_selection_location": expected.selection.location,
                "expected_selection_length": expected.selection.length,
                "observed_selection_location": observed.selection.location,
                "observed_selection_length": observed.selection.length,
            ]
        }
    }
    struct Snapshot {
        let target: Target
        let state: FieldState
        let range: NSRange
        let rawReplacement: String
        let created: Double
    }

    static func focusedElement() -> AXUIElement? {
        var value: CFTypeRef?
        let system = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(system, 0.05)
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &value) == .success,
              let value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        let element = value as! AXUIElement
        AXUIElementSetMessagingTimeout(element, 0.05)
        return element
    }

    static func captureTarget() -> Target {
        let app = NSWorkspace.shared.frontmostApplication.map {
            AppIdentity(pid: $0.processIdentifier, launched: $0.launchDate, bundle: $0.bundleIdentifier)
        }
        return Target(app: app, element: focusedElement())
    }

    static func isSame(_ a: AXUIElement?, _ b: AXUIElement?) -> Bool {
        guard let a, let b else { return false }
        return CFEqual(a, b)
    }

    static func matches(_ target: Target, _ current: Target) -> Bool {
        guard let app = target.app, app == current.app else { return false }
        // An unknown field is not a mismatch. Known fields must match, even during fast delivery.
        if let original = target.element, let focused = current.element { return isSame(original, focused) }
        return true
    }

    static func insert(_ text: String, raw: String, target: Target, cancellation: OperationCancellation,
                       completion: @escaping (Result) -> Void) {
        queue.async {
            let result = perform(text, raw: raw, target: target, cancellation: cancellation,
                                 currentTarget: captureTarget, readState: { target.element.flatMap(readState) },
                                 context: { target.element.flatMap(cursorContext) }, post: post)
            DispatchQueue.main.async { completion(result) }
        }
    }

    /// The delivery boundary also accepts controlled readers and an event sink for isolated harness checks.
    static func perform(_ text: String, raw: String, target: Target, cancellation: OperationCancellation,
                        currentTarget: () -> Target, readState: () -> FieldState?,
                        context: () -> (before: String?, after: String?)?, post: ([UniChar], Bool) -> Bool) -> Result {
        let start = Monotonic.now
        var postedMs: Int?
        var postedAt: Double?
        var postedUnits = 0
        var verificationStart = Monotonic.now
        var verification: Verification?
        func result(_ outcome: Outcome, _ snapshot: Snapshot? = nil) -> Result {
            Result(outcome: outcome, postedMs: postedMs, postedAt: postedAt,
                   verifiedAt: outcome == .verified ? Monotonic.now : nil, postedUnits: postedUnits,
                   verifiedMs: outcome == .verified ? Monotonic.milliseconds(since: start) : nil,
                   verificationMs: postedMs == nil ? 0 : Monotonic.milliseconds(since: verificationStart), snapshot: snapshot,
                   verification: verification)
        }
        func validationFailure() -> Outcome? {
            let current = currentTarget()
            guard target.app != nil, current.app != nil else { return .unavailable }
            return matches(target, current) ? nil : .targetChanged
        }
        guard !cancellation.isCancelled else { return result(.cancelled) }
        if let failure = validationFailure() { return result(failure) }
        let before = readState()
        let surroundings = before.map { Self.context(in: $0) } ?? context()
        let payload = adapt(text, before: surroundings?.before, after: surroundings?.after)
        let expected = before.flatMap { replacing($0, with: payload) }
        let units = Array(payload.utf16)
        var offset = 0
        while offset < units.count {
            guard !cancellation.isCancelled else { return result(.cancelled) }
            if let failure = validationFailure() { return result(failure) }
            var end = min(offset + 64, units.count)
            if end < units.count, UTF16.isLeadSurrogate(units[end - 1]) { end -= 1 }
            let chunk = Array(units[offset..<end])
            guard post(chunk, true) else { return result(.eventFailed) }
            postedUnits += chunk.count
            // Always release a posted key-down, even if cancellation arrives between the pair.
            guard post(chunk, false) else { return result(.eventFailed) }
            offset = end
            if offset < units.count { usleep(2000) }
        }
        postedAt = Monotonic.now
        postedMs = Monotonic.milliseconds(since: start)
        verificationStart = Monotonic.now
        guard let before, let expected else { return result(.sent) }
        var observed: FieldState?
        repeat {
            guard !cancellation.isCancelled else { return result(.cancelled) }
            if let failure = validationFailure() { return result(failure) }
            observed = readState()
            if observed == expected {
                let snapshot: Snapshot?
                if before.selection.length == 0, target.element != nil, target.app?.launched != nil {
                    snapshot = Snapshot(target: target, state: expected,
                                        range: NSRange(location: before.selection.location, length: payload.utf16.count),
                                        rawReplacement: adapt(raw, before: surroundings?.before, after: surroundings?.after),
                                        created: Monotonic.now)
                } else { snapshot = nil }
                return result(.verified, snapshot)
            }
            if observed == nil { return result(.sent) }
            if Monotonic.now - verificationStart >= verificationSeconds { break }
            usleep(20_000)
        } while true
        if let observed, observed != before {
            verification = Verification(expected: expected, observed: observed)
        }
        return result(observed == before ? .unchanged : .unexpected)
    }

    static func replacing(_ state: FieldState, with text: String) -> FieldState? {
        let value = state.value as NSString
        guard valid(state.selection, length: value.length) else { return nil }
        return FieldState(value: value.replacingCharacters(in: state.selection, with: text),
                          selection: NSRange(location: state.selection.location + text.utf16.count, length: 0))
    }

    private static func valid(_ range: NSRange, length: Int) -> Bool {
        range.location >= 0 && range.length >= 0 && range.location <= length && range.length <= length - range.location
    }

    static func readState(in element: AXUIElement) -> FieldState? {
        guard let selection = selection(in: element) else { return nil }
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXValueAttribute as CFString, &value) == .success,
              let string = value as? String, (string as NSString).length <= maximumSnapshotUnits,
              valid(selection, length: (string as NSString).length) else { return nil }
        return FieldState(value: string, selection: selection)
    }

    private static func selection(in element: AXUIElement) -> NSRange? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &value) == .success,
              let value, CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
        var range = CFRange()
        guard AXValueGetValue(value as! AXValue, .cfRange, &range), range.location >= 0, range.length >= 0 else { return nil }
        return NSRange(location: range.location, length: range.length)
    }

    private static func context(in state: FieldState) -> (before: String?, after: String?) {
        let value = state.value as NSString
        return (value.substring(with: NSRange(location: max(0, state.selection.location - 24), length: min(24, state.selection.location))),
                value.substring(with: NSRange(location: NSMaxRange(state.selection), length: min(2, value.length - NSMaxRange(state.selection)))))
    }

    private static func cursorContext(in element: AXUIElement) -> (before: String?, after: String?)? {
        guard let range = selection(in: element), range.location <= Int.max - range.length else { return nil }
        let before = NSRange(location: max(0, range.location - 24), length: min(24, range.location))
        let after = NSRange(location: NSMaxRange(range), length: 2)
        return (slice(before, in: element), slice(after, in: element))
    }

    private static func slice(_ range: NSRange, in element: AXUIElement) -> String? {
        if range.length == 0 { return "" }
        var cfRange = CFRange(location: range.location, length: range.length)
        if let argument = AXValueCreate(.cfRange, &cfRange) {
            var text: CFTypeRef?
            if AXUIElementCopyParameterizedAttributeValue(element, kAXStringForRangeParameterizedAttribute as CFString,
                                                         argument, &text) == .success, let string = text as? String { return string }
        }
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXValueAttribute as CFString, &value) == .success,
              let string = value as? NSString, range.location <= string.length else { return nil }
        // NSString slices UTF-16 directly, without allocating a whole-document code-unit array.
        return string.substring(with: NSRange(location: range.location, length: min(range.length, string.length - range.location)))
    }

    static func adapt(_ text: String, before: String?, after: String?) -> String {
        guard let first = text.first, let last = text.last else { return text }
        var result = text
        if let previous = before?.last, !previous.isWhitespace, !"([{\"'“‘/-".contains(previous), !first.isPunctuation {
            result = " " + result
        }
        if let next = after?.first, !next.isWhitespace, !next.isPunctuation, !last.isWhitespace,
           !"([{\"'“‘/-".contains(last) { result += " " }
        return result
    }

    private static func post(_ units: [UniChar], keyDown: Bool) -> Bool {
        guard let event = CGEvent(keyboardEventSource: CGEventSource(stateID: .hidSystemState), virtualKey: 0, keyDown: keyDown) else { return false }
        event.flags = []
        event.setIntegerValueField(.eventSourceUserData, value: dictateEventMarker)
        units.withUnsafeBufferPointer { event.keyboardSetUnicodeString(stringLength: units.count, unicodeString: $0.baseAddress) }
        event.post(tap: .cghidEventTap)
        return true
    }

    enum MutationOutcome: String { case noWrite, uncertain, verified }

    static func canCorrect(_ snapshot: Snapshot, current: Target, state: FieldState?) -> Bool {
        Monotonic.now - snapshot.created <= correctionLifetime && matches(snapshot.target, current)
            && isSame(snapshot.target.element, current.element) && state == snapshot.state
    }

    static func correct(_ snapshot: Snapshot, restoreRaw: Bool, cancellation: OperationCancellation,
                        completion: @escaping (MutationOutcome) -> Void) {
        queue.async {
            let outcome = mutate(snapshot, restoreRaw: restoreRaw, cancellation: cancellation)
            DispatchQueue.main.async { completion(outcome) }
        }
    }

    private static func mutate(_ snapshot: Snapshot, restoreRaw: Bool, cancellation: OperationCancellation) -> MutationOutcome {
        guard let element = snapshot.target.element else { return .noWrite }
        return performCorrection(snapshot, restoreRaw: restoreRaw, cancellation: cancellation,
                                 currentTarget: captureTarget, readState: { readState(in: element) },
                                 writable: {
            for attribute in [kAXSelectedTextRangeAttribute, kAXSelectedTextAttribute] {
                var settable: DarwinBoolean = false
                guard AXUIElementIsAttributeSettable(element, attribute as CFString, &settable) == .success, settable.boolValue else { return false }
            }
            return true
        }, select: { setSelection($0, in: element) }, write: {
            AXUIElementSetAttributeValue(element, kAXSelectedTextAttribute as CFString, $0 as CFString) == .success
        })
    }

    static func performCorrection(_ snapshot: Snapshot, restoreRaw: Bool, cancellation: OperationCancellation,
                                  currentTarget: () -> Target, readState: () -> FieldState?, writable: () -> Bool,
                                  select: (NSRange) -> Bool, write: (String) -> Bool) -> MutationOutcome {
        guard !cancellation.isCancelled, canCorrect(snapshot, current: currentTarget(), state: readState()),
              writable() else { return .noWrite }
        func ownsField() -> Bool {
            let current = currentTarget()
            return matches(snapshot.target, current) && isSame(snapshot.target.element, current.element)
        }
        let replacement = restoreRaw ? snapshot.rawReplacement : ""
        let selected = FieldState(value: snapshot.state.value, selection: snapshot.range)
        guard let expected = replacing(selected, with: replacement), select(snapshot.range) else { return .noWrite }
        guard !cancellation.isCancelled, ownsField(), readState() == selected else {
            // Restore only our selection, when the field still has exactly the state we selected.
            if ownsField(), readState() == selected { _ = select(snapshot.state.selection) }
            return .noWrite
        }
        // Any value-write attempt consumes the snapshot, including an unacknowledged write.
        guard write(replacement) else { return .uncertain }
        let deadline = Monotonic.now + verificationSeconds
        repeat {
            guard !cancellation.isCancelled, ownsField() else { return .uncertain }
            if readState() == expected { return .verified }
            if Monotonic.now >= deadline { return .uncertain }
            usleep(20_000)
        } while true
    }

    private static func setSelection(_ range: NSRange, in element: AXUIElement) -> Bool {
        var value = CFRange(location: range.location, length: range.length)
        guard let argument = AXValueCreate(.cfRange, &value) else { return false }
        return AXUIElementSetAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, argument) == .success
    }
}
