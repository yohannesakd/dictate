// Global keyboard handling through a Quartz event tap (needs Accessibility access).
// It watches the fn key and consumes the few keys that belong to dictation.

import AppKit
import Carbon.HIToolbox

final class KeyboardMonitor {
    var onFnDown: () -> Void = {}
    var onFnUp: () -> Void = {}
    var onFnSpace: () -> Void = {}
    var onOtherKeyWithFn: () -> Void = {}
    var onEscape: () -> Void = {}
    var onPasteLast: () -> Void = {}
    /// Esc is consumed only while dictation is active.
    var isDictationActive: () -> Bool = { false }

    private var tap: CFMachPort?
    private var fnDown = false

    var isRunning: Bool { tap != nil }

    /// Returns false until macOS grants Accessibility access.
    @discardableResult
    func start() -> Bool {
        guard tap == nil else { return true }
        let mask = (1 << CGEventType.flagsChanged.rawValue) | (1 << CGEventType.keyDown.rawValue)
        let callback: CGEventTapCallBack = { _, type, event, refcon in
            let monitor = Unmanaged<KeyboardMonitor>.fromOpaque(refcon!).takeUnretainedValue()
            return monitor.handle(type, event)
        }
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                          eventsOfInterest: CGEventMask(mask), callback: callback,
                                          userInfo: Unmanaged.passUnretained(self).toOpaque()) else { return false }
        CFRunLoopAddSource(CFRunLoopGetMain(), CFMachPortCreateRunLoopSource(nil, tap, 0), .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        self.tap = tap
        return true
    }

    private func handle(_ type: CGEventType, _ event: CGEvent) -> Unmanaged<CGEvent>? {
        let pass = Unmanaged.passUnretained(event)
        switch type {
        case .tapDisabledByTimeout, .tapDisabledByUserInput:
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return pass

        case .flagsChanged:
            let keyCode = Int(event.getIntegerValueField(.keyboardEventKeycode))
            let isFn = event.flags.contains(.maskSecondaryFn)
            // 63 is fn; 179 is the globe key on newer keyboards.
            if (keyCode == kVK_Function || keyCode == 179), isFn != fnDown {
                fnDown = isFn
                let action = isFn ? onFnDown : onFnUp
                DispatchQueue.main.async(execute: action)
            }
            return pass

        case .keyDown:
            let keyCode = Int(event.getIntegerValueField(.keyboardEventKeycode))
            let isRepeat = event.getIntegerValueField(.keyboardEventAutorepeat) != 0
            if fnDown, keyCode == kVK_Space {
                if !isRepeat { DispatchQueue.main.async(execute: onFnSpace) }
                return nil
            }
            if keyCode == kVK_Escape, isDictationActive() {
                DispatchQueue.main.async(execute: onEscape)
                return nil
            }
            let chord = event.flags.intersection([.maskCommand, .maskControl, .maskAlternate, .maskShift])
            if keyCode == kVK_ANSI_V, chord == [.maskCommand, .maskControl] {
                DispatchQueue.main.async(execute: onPasteLast)
                return nil
            }
            if fnDown { DispatchQueue.main.async(execute: onOtherKeyWithFn) }
            return pass

        default:
            return pass
        }
    }
}

/// Types text into the focused field with synthetic key events. The clipboard is not used.
func typeText(_ text: String) {
    let units = Array(text.utf16)
    DispatchQueue.global(qos: .userInitiated).async {
        let source = CGEventSource(stateID: .hidSystemState)
        var start = 0
        while start < units.count {
            var end = min(start + 16, units.count)
            if end < units.count, UTF16.isLeadSurrogate(units[end - 1]) { end -= 1 }  // Keep surrogate pairs together.
            let chunk = Array(units[start..<end])
            for keyDown in [true, false] {
                guard let event = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: keyDown) else { continue }
                event.flags = []  // Ignore modifiers the user may still hold, such as fn.
                chunk.withUnsafeBufferPointer { event.keyboardSetUnicodeString(stringLength: chunk.count, unicodeString: $0.baseAddress) }
                event.post(tap: .cghidEventTap)
            }
            usleep(2000)  // Some apps drop characters when events arrive too fast.
            start = end
        }
    }
}
