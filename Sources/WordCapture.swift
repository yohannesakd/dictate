// Adds a dictionary word from a dictation mistake: select the wrong text in any app, press Control-Command-E,
// and type what you meant. The selection becomes "heard as"; the typed text becomes "write as".

import AppKit
import ApplicationServices
import Carbon.HIToolbox
import SwiftUI

final class WordCapture {
    private let model: AppModel
    private var panel: CapturePanel?
    private var resignObserver: Any?

    init(model: AppModel) { self.model = model }

    func show() {
        Self.readSelection { [weak self] selection in
            guard let self else { return }
            let heard = selection?.text.trimmingCharacters(in: .whitespacesAndNewlines.union(.punctuationCharacters)) ?? ""
            guard !heard.isEmpty, !heard.contains(where: \.isNewline) else {
                NSSound.beep()
                return
            }
            self.present(heard: heard, near: selection?.bounds)
        }
    }

    // MARK: Popup

    private func present(heard: String, near selection: CGRect?) {
        close()
        let panel = CapturePanel(contentRect: .zero, styleMask: [.nonactivatingPanel, .borderless],
                                 backing: .buffered, defer: false)
        panel.level = .popUpMenu
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        let view = CaptureView(heard: heard,
                               save: { [weak self] written in self?.save(heard: heard, written: written) ?? false },
                               close: { [weak self] in self?.close() })
        let host = NSHostingView(rootView: view)
        panel.contentView = host
        panel.setContentSize(host.fittingSize)
        panel.setFrameOrigin(Self.origin(for: panel.frame.size, near: selection))
        // A non-activating panel takes the keyboard without bringing Dictate forward, so the app keeps its selection.
        panel.makeKeyAndOrderFront(nil)
        resignObserver = NotificationCenter.default.addObserver(forName: NSWindow.didResignKeyNotification, object: panel,
                                                                queue: .main) { [weak self] _ in self?.close() }
        self.panel = panel
    }

    private func close() {
        if let resignObserver { NotificationCenter.default.removeObserver(resignObserver) }
        resignObserver = nil
        panel?.orderOut(nil)
        panel = nil
    }

    /// Adds the word, or changes the entry that already has this "heard as". Returns false when there is nothing to save.
    private func save(heard: String, written: String) -> Bool {
        let written = written.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !written.isEmpty, written != heard else { return false }
        let key = PersonalDictionary.normalize(heard)
        if let index = model.dictionary.firstIndex(where: { PersonalDictionary.normalize($0.heard) == key }) {
            model.dictionary[index].written = written
        } else {
            model.dictionary.append(DictionaryEntry(heard: heard, written: written))
        }
        return true
    }

    /// Below the selection, or above it near the bottom of the screen. At the pointer when the app hides the selection.
    private static func origin(for size: CGSize, near selection: CGRect?) -> CGPoint {
        let anchor = selection ?? CGRect(origin: NSEvent.mouseLocation, size: .zero)
        let screen = NSScreen.screens.first { $0.frame.contains(CGPoint(x: anchor.midX, y: anchor.midY)) } ?? NSScreen.main
        let visible = screen?.visibleFrame ?? .zero
        var origin = CGPoint(x: anchor.minX, y: anchor.minY - size.height - 6)
        if origin.y < visible.minY { origin.y = anchor.maxY + 6 }
        origin.x = min(max(origin.x, visible.minX + 6), visible.maxX - size.width - 6)
        origin.y = min(max(origin.y, visible.minY + 6), visible.maxY - size.height - 6)
        return origin
    }

    // MARK: Selection

    struct Selection {
        let text: String
        /// In AppKit screen coordinates. Nil when the app does not report where the selection is.
        let bounds: CGRect?
    }

    /// Reads the selection through Accessibility. Apps that do not expose it (many Electron apps) get Command-C,
    /// and the clipboard is restored afterward.
    static func readSelection(completion: @escaping (Selection?) -> Void) {
        let element = Insertion.focusedElement()
        var value: CFTypeRef?
        if let element,
           AXUIElementCopyAttributeValue(element, kAXSelectedTextAttribute as CFString, &value) == .success,
           let text = value as? String, !text.isEmpty {
            completion(Selection(text: text, bounds: selectionBounds(in: element)))
            return
        }
        copySelection { text in completion(text.map { Selection(text: $0, bounds: nil) }) }
    }

    private static func selectionBounds(in element: AXUIElement) -> CGRect? {
        var range: CFTypeRef?
        var bounds: CFTypeRef?
        var rect = CGRect.zero
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &range) == .success,
              let range,
              AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute as CFString,
                                                         range, &bounds) == .success,
              let bounds, CFGetTypeID(bounds) == AXValueGetTypeID(),
              AXValueGetValue(bounds as! AXValue, .cgRect, &rect), rect.width > 0 || rect.height > 0 else { return nil }
        // Accessibility measures from the top left of the main screen; AppKit from its bottom left.
        let mainHeight = NSScreen.screens.first?.frame.height ?? 0
        return CGRect(x: rect.minX, y: mainHeight - rect.maxY, width: rect.width, height: rect.height)
    }

    private static func copySelection(completion: @escaping (String?) -> Void) {
        let pasteboard = NSPasteboard.general
        let saved = (pasteboard.pasteboardItems ?? []).map { item -> NSPasteboardItem in
            let copy = NSPasteboardItem()
            for type in item.types { if let data = item.data(forType: type) { copy.setData(data, forType: type) } }
            return copy
        }
        let before = pasteboard.changeCount
        let source = CGEventSource(stateID: .hidSystemState)
        for keyDown in [true, false] {
            let event = CGEvent(keyboardEventSource: source, virtualKey: CGKeyCode(kVK_ANSI_C), keyDown: keyDown)
            event?.flags = .maskCommand  // Not the Control key the user still holds from the shortcut.
            event?.post(tap: .cghidEventTap)
        }
        // Apps usually copy within a few milliseconds. Without a selection, the clipboard does not change.
        func check(_ attempt: Int) {
            guard pasteboard.changeCount == before else {
                let text = pasteboard.string(forType: .string)
                pasteboard.clearContents()
                pasteboard.writeObjects(saved)
                completion(text)
                return
            }
            guard attempt < 25 else { return completion(nil) }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.01) { check(attempt + 1) }
        }
        check(0)
    }
}

private final class CapturePanel: NSPanel {
    override var canBecomeKey: Bool { true }
}

private struct CaptureView: View {
    let heard: String
    let save: (String) -> Bool
    let close: () -> Void
    @State private var written: String
    @State private var saved = false
    @FocusState private var focused: Bool

    init(heard: String, save: @escaping (String) -> Bool, close: @escaping () -> Void) {
        self.heard = heard
        self.save = save
        self.close = close
        _written = State(initialValue: heard)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if saved {
                Label("Added to the dictionary", systemImage: "checkmark").font(.callout)
            } else {
                Text("What did you mean by “\(heard)”?").font(.callout).lineLimit(2)
                TextField("", text: $written)
                    .textFieldStyle(.roundedBorder)
                    .focused($focused)
                    .onSubmit(submit)
                    .onExitCommand(perform: close)
            }
        }
        .padding(12)
        .frame(width: 280, alignment: .leading)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10))
        .onAppear { focused = true }
    }

    private func submit() {
        guard save(written) else { return close() }
        saved = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8, execute: close)
    }
}
