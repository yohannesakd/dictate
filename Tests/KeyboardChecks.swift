import AppKit
import Carbon.HIToolbox

@main struct KeyboardChecks {
    @MainActor static func main() async {
        let monitor = KeyboardMonitor()
        var downs = 0, otherKeys = 0, escapes = 0
        var observedDown: Double?
        monitor.onFnDown = { observed in precondition(observed <= Monotonic.now); observedDown = observed; downs += 1 }
        monitor.onOtherKeyWithFn = { otherKeys += 1 }
        monitor.onEscape = { escapes += 1 }
        monitor.isDictationActive = { true }
        let fn = CGEvent(keyboardEventSource: nil, virtualKey: CGKeyCode(kVK_Function), keyDown: true)!
        fn.flags = .maskSecondaryFn
        let earliest = Monotonic.now
        _ = monitor.handle(.flagsChanged, fn)
        let latest = Monotonic.now
        let synthetic = CGEvent(keyboardEventSource: nil, virtualKey: CGKeyCode(kVK_Escape), keyDown: true)!
        synthetic.setIntegerValueField(.eventSourceUserData, value: dictateEventMarker)
        precondition(monitor.handle(.keyDown, synthetic) != nil)
        try! await Task.sleep(nanoseconds: 20_000_000)
        precondition(downs == 1 && otherKeys == 0 && escapes == 0 && observedDown! >= earliest && observedDown! <= latest)
        let ordinary = CGEvent(keyboardEventSource: nil, virtualKey: CGKeyCode(kVK_Escape), keyDown: true)!
        ordinary.setIntegerValueField(.keyboardEventKeycode, value: Int64(kVK_Escape))
        ordinary.setIntegerValueField(.eventSourceUserData, value: 0)
        precondition(ordinary.getIntegerValueField(.keyboardEventKeycode) == Int64(kVK_Escape))
        precondition(ordinary.getIntegerValueField(.eventSourceUserData) == 0)
        precondition(monitor.handle(.keyDown, ordinary) == nil)
        try! await Task.sleep(nanoseconds: 20_000_000)
        precondition(escapes == 1)
        // CGEvents are passed directly to the handler. No event tap is started and no event is posted.
        print("Passed marked-event filtering and monotonic keyboard timestamp checks")
    }
}
