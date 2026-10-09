// Dictate: system-wide dictation with Phonon-2 on the Neural Engine, in the style of Wispr Flow.

import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let model = AppModel()
    private lazy var controller = DictationController(model: model)
    private var flowBar: FlowBar?
    private var hub: NSWindow?
    private var statusItem: NSStatusItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.applicationIconImage = appIcon()
        controller.start()
        flowBar = FlowBar(model: model, controller: controller)
        setUpStatusItem()
        if model.needsSetup || !UserDefaults.standard.bool(forKey: "launchedBefore") {
            UserDefaults.standard.set(true, forKey: "launchedBefore")
            showHub()
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showHub()
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    // MARK: Menus

    private func setUpStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(systemSymbolName: "waveform", accessibilityDescription: "Dictate")
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let paste = NSMenuItem(title: "Insert Last Dictation", action: #selector(pasteLast), keyEquivalent: "v")
        paste.keyEquivalentModifierMask = [.control, .command]
        paste.target = self
        paste.isEnabled = !model.history.isEmpty
        menu.addItem(paste)
        let open = NSMenuItem(title: "Open Dictate", action: #selector(openHub), keyEquivalent: "")
        open.target = self
        menu.addItem(open)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit Dictate", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
    }

    @objc private func pasteLast() { controller.pasteLast() }
    @objc private func openHub() { showHub() }

    private func showHub() {
        if hub == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 600, height: 700),
                                  styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                                  backing: .buffered, defer: false)
            window.titlebarAppearsTransparent = true
            window.titleVisibility = .hidden
            window.isReleasedWhenClosed = false
            window.contentView = NSHostingView(rootView: HubView(model: model, controller: controller))
            window.center()
            hub = window
        }
        hub?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
