// Native AppKit fields used only by the macOS verification bundle.
import AppKit
private var fixture: NSWindow?
private let first = NSTextView(frame: NSRect(x: 20, y: 145, width: 440, height: 85))
private let second = NSTextView(frame: NSRect(x: 20, y: 25, width: 440, height: 85))
@_cdecl("dictate_fixture") public func fixtureAction(_ action: Int32) {
    if fixture == nil {
        let window = NSWindow(contentRect: NSRect(x: 100, y: 150, width: 480, height: 260), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Dictate verification field"
        first.font = .systemFont(ofSize: 16); second.font = .systemFont(ofSize: 16)
        window.contentView!.addSubview(first); window.contentView!.addSubview(second)
        window.isReleasedWhenClosed = false
        fixture = window
    }
    let window = fixture!
    window.makeKeyAndOrderFront(nil)
    NSApp.activate(ignoringOtherApps: true)
    switch action {
    case 0: first.string = ""; second.string = ""; first.setSelectedRange(NSRange(location: 0, length: 0)); window.makeFirstResponder(first)
    case 1: window.makeFirstResponder(first)
    case 2: window.makeFirstResponder(second)
    case 3: first.string += " changed"; first.setSelectedRange(NSRange(location: (first.string as NSString).length, length: 0)); window.makeFirstResponder(first)
    case 4: first.setSelectedRange(NSRange(location: 0, length: 0)); window.makeFirstResponder(first)
    case 5: first.string = "Before old after"; first.setSelectedRange(NSRange(location: 7, length: 3)); window.makeFirstResponder(first)
    default: break
    }
}
@_cdecl("dictate_fixture_run") public func runFixture(_ pointer: UnsafePointer<CChar>) {
    let command = String(cString: pointer)
    let app = NSApplication.shared
    app.setActivationPolicy(.regular)
    fixtureAction(0)
    try! "ready".write(toFile: command + ".ready", atomically: true, encoding: .utf8)
    let timer = Timer.scheduledTimer(withTimeInterval: 0.02, repeats: true) { _ in
        guard FileManager.default.fileExists(atPath: command) else { return }
        do {
            let action = Int32(try String(contentsOfFile: command, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines))!
            if action == 7 { app.terminate(nil); return }
            if action != 6 { fixtureAction(action) }
            try first.string.write(toFile: command + ".first", atomically: true, encoding: .utf8)
            try second.string.write(toFile: command + ".second", atomically: true, encoding: .utf8)
            let details: [String: Any] = ["frontmost": NSWorkspace.shared.frontmostApplication?.bundleIdentifier ?? "unknown",
                                        "fixture_active": app.isActive, "window_key": fixture?.isKeyWindow ?? false]
            try JSONSerialization.data(withJSONObject: details).write(to: URL(fileURLWithPath: command + ".focus.json"))
            try FileManager.default.removeItem(atPath: command)
        } catch { try? error.localizedDescription.write(toFile: command + ".error", atomically: true, encoding: .utf8) }
    }
    app.run()
    timer.invalidate()
}
