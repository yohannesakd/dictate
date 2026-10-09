// Dictate: system-wide dictation with Phonon-2 on the Neural Engine, in the style of Wispr Flow.

import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let model = AppModel()
    private let navigation = HubNavigation()
    private lazy var controller = DictationController(model: model)
    private var flowBar: FlowBar?
    private var hub: NSWindow?
    private var statusItem: NSStatusItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.applicationIconImage = appIcon()
        setUpMainMenu()
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

    /// The app menu and the Edit menu. Text fields need the Edit menu for copy and paste shortcuts.
    private func setUpMainMenu() {
        let appMenu = NSMenu()
        let settings = NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self
        appMenu.addItem(settings)
        appMenu.addItem(.separator())
        appMenu.addItem(NSMenuItem(title: "Close Window", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w"))
        appMenu.addItem(NSMenuItem(title: "Quit Dictate", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))

        let editMenu = NSMenu(title: "Edit")
        editMenu.addItem(NSMenuItem(title: "Undo", action: Selector(("undo:")), keyEquivalent: "z"))
        let redo = NSMenuItem(title: "Redo", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        editMenu.addItem(redo)
        editMenu.addItem(.separator())
        editMenu.addItem(NSMenuItem(title: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x"))
        editMenu.addItem(NSMenuItem(title: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c"))
        editMenu.addItem(NSMenuItem(title: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v"))
        editMenu.addItem(NSMenuItem(title: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a"))

        let mainMenu = NSMenu()
        for menu in [appMenu, editMenu] {
            let item = NSMenuItem()
            item.submenu = menu
            mainMenu.addItem(item)
        }
        NSApp.mainMenu = mainMenu
    }

    private func setUpStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(systemSymbolName: "waveform", accessibilityDescription: "Dictate")
        let menu = NSMenu()
        menu.delegate = self
        menu.autoenablesItems = false
        item.menu = menu
        statusItem = item
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let paste = NSMenuItem(title: "Insert Last Dictation", action: #selector(pasteLast), keyEquivalent: "v")
        paste.keyEquivalentModifierMask = [.control, .command]
        paste.target = self
        paste.isEnabled = !model.history.isEmpty && model.phase != .processing
        menu.addItem(paste)
        for (title, action) in [("Undo Last Dictation", #selector(undoLast)), ("Restore Raw Transcript", #selector(restoreRaw))] {
            let correction = NSMenuItem(title: title, action: action, keyEquivalent: "")
            correction.target = self
            correction.isEnabled = action == #selector(restoreRaw) ? controller.canRestoreRaw : controller.canCorrectLast
            menu.addItem(correction)
        }
        let open = NSMenuItem(title: "Open Dictate", action: #selector(openHub), keyEquivalent: "")
        open.target = self
        menu.addItem(open)
        let settings = NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: "")
        settings.target = self
        menu.addItem(settings)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit Dictate", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
    }

    @objc private func undoLast() { controller.undoLastDictation() }
    @objc private func restoreRaw() { controller.restoreRawTranscript() }
    @objc private func pasteLast() { controller.pasteLast() }
    @objc private func openHub() { showHub() }
    @objc private func openSettings() {
        navigation.screen = .settings
        showHub()
    }

    private func showHub() {
        if hub == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 480, height: 600),
                                  styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                                  backing: .buffered, defer: false)
            window.titlebarAppearsTransparent = true
            window.titleVisibility = .hidden
            window.isReleasedWhenClosed = false
            window.contentView = NSHostingView(rootView: HubView(model: model, navigation: navigation, controller: controller))
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
