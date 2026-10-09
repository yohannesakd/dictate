// Global keyboard handling through a Quartz event tap (needs Accessibility access).
// It watches the fn key and consumes the few keys that belong to dictation.

import AppKit
import Carbon.HIToolbox

final class KeyboardMonitor {
    var onFnDown: (Double) -> Void = { _ in }
    var onFnUp: (Double) -> Void = { _ in }
    var onFnSpace: (Double) -> Void = { _ in }
    var onOtherKeyWithFn: () -> Void = {}
    var onEscape: () -> Void = {}
    var onPasteLast: () -> Void = {}
    /// macOS disabled the tap for a moment, so key releases may have been missed.
    var onTapReset: () -> Void = {}
    /// Esc is consumed only while dictation is active.
    var isDictationActive: () -> Bool = { false }

    private var tap: CFMachPort?
    private var fnDown = false

    var isRunning: Bool { tap.map { CGEvent.tapIsEnabled(tap: $0) } ?? false }

    /// Creates or re-enables the tap. Returns false until macOS grants Accessibility access.
    @discardableResult
    func start() -> Bool {
        if let tap {
            CGEvent.tapEnable(tap: tap, enable: true)
            return CGEvent.tapIsEnabled(tap: tap)
        }
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

    func handle(_ type: CGEventType, _ event: CGEvent) -> Unmanaged<CGEvent>? {
        let pass = Unmanaged.passUnretained(event)
        if event.getIntegerValueField(.eventSourceUserData) == dictateEventMarker { return pass }
        let observed = Monotonic.now
        switch type {
        case .tapDisabledByTimeout, .tapDisabledByUserInput:
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            // The fn release may have happened while the tap was off. Start again from a known state.
            fnDown = false
            DispatchQueue.main.async(execute: onTapReset)
            return pass

        case .flagsChanged:
            let keyCode = Int(event.getIntegerValueField(.keyboardEventKeycode))
            let isFn = event.flags.contains(.maskSecondaryFn)
            // 63 is fn; 179 is the globe key on newer keyboards.
            if (keyCode == kVK_Function || keyCode == 179), isFn != fnDown {
                fnDown = isFn
                let action = isFn ? onFnDown : onFnUp
                DispatchQueue.main.async { action(observed) }
            }
            return pass

        case .keyDown:
            let keyCode = Int(event.getIntegerValueField(.keyboardEventKeycode))
            let isRepeat = event.getIntegerValueField(.keyboardEventAutorepeat) != 0
            if fnDown, keyCode == kVK_Space {
                if !isRepeat { DispatchQueue.main.async { self.onFnSpace(observed) } }
                return nil
            }
            if keyCode == kVK_Escape, isDictationActive() {
                DispatchQueue.main.async(execute: onEscape)
                return nil
            }
            let chord = event.flags.intersection([.maskCommand, .maskControl, .maskAlternate, .maskShift])
            if keyCode == kVK_ANSI_V, chord == [.maskCommand, .maskControl] {
                if !isRepeat { DispatchQueue.main.async(execute: onPasteLast) }
                return nil
            }
            if fnDown { DispatchQueue.main.async(execute: onOtherKeyWithFn) }
            return pass

        default:
            return pass
        }
    }
}
