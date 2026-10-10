// The flow bar: a small pill at the bottom center of the screen.
// Idle: hidden. Messages appear in the app window.
// Listening: white level bars; hands-free adds cancel and stop buttons, the only clickable parts.
// Processing: pulsing dots. The panel never takes keyboard focus from the target app.

import AppKit
import Combine
import SwiftUI

enum FlowBarLayout {
    static let panelSize = CGSize(width: 420, height: 90)
    static let bottomPadding: CGFloat = 10

    static func pillSize(_ model: AppModel) -> CGSize {
        switch model.phase {
        case .idle: return .zero
        case .listening: return model.handsFree ? CGSize(width: 148, height: 36) : CGSize(width: 92, height: 32)
        case .processing: return CGSize(width: 92, height: 32)
        }
    }

    /// Only the hands-free pill has buttons. Every other state lets the mouse pass through.
    static func isInteractive(_ model: AppModel) -> Bool {
        model.phase == .listening && model.handsFree
    }

    /// The hands-free pill, in panel coordinates (origin bottom-left).
    static func buttonsRect(_ model: AppModel) -> CGRect? {
        guard isInteractive(model) else { return nil }
        let size = pillSize(model)
        return CGRect(x: (panelSize.width - size.width) / 2, y: bottomPadding, width: size.width, height: size.height)
    }
}

private final class FlowBarPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class FirstClickHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

final class FlowBar {
    private let panel: NSPanel
    private let model: AppModel
    private var mouseMonitors: [Any] = []
    private var stateChange: AnyCancellable?

    init(model: AppModel, controller: DictationController) {
        self.model = model
        panel = FlowBarPanel(contentRect: CGRect(origin: .zero, size: FlowBarLayout.panelSize),
                             styleMask: [.nonactivatingPanel, .borderless], backing: .buffered, defer: false)
        panel.level = .statusBar
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        let view = FlowBarView(model: model,
                               onStop: { [weak controller] in controller?.finish() },
                               onCancel: { [weak controller] in controller?.cancel() })
        panel.contentView = FirstClickHostingView(rootView: view)
        reposition()
        updatePresentation()

        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in self?.reposition() }
        panel.acceptsMouseMovedEvents = true
        stateChange = model.objectWillChange
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.updatePresentation() }
    }

    private func reposition() {
        guard let frame = (NSScreen.screens.first ?? NSScreen.main)?.visibleFrame else { return }
        panel.setFrameOrigin(CGPoint(x: frame.midX - FlowBarLayout.panelSize.width / 2, y: frame.minY + 2))
    }

    /// Watches the mouse only while the hands-free buttons are on screen.
    private func updatePresentation() {
        if model.phase == .idle {
            panel.orderOut(nil)
        } else if !panel.isVisible {
            panel.orderFrontRegardless()
        }
        if FlowBarLayout.isInteractive(model), mouseMonitors.isEmpty {
            let global = NSEvent.addGlobalMonitorForEvents(matching: .mouseMoved) { [weak self] _ in self?.trackMouse() }
            let local = NSEvent.addLocalMonitorForEvents(matching: .mouseMoved) { [weak self] event in
                self?.trackMouse()
                return event
            }
            mouseMonitors = [global, local].compactMap { $0 }
        } else if !FlowBarLayout.isInteractive(model), !mouseMonitors.isEmpty {
            mouseMonitors.forEach(NSEvent.removeMonitor)
            mouseMonitors = []
        }
        trackMouse()
    }

    /// The panel takes clicks only while the mouse is over the hands-free buttons.
    private func trackMouse() {
        let inside = FlowBarLayout.buttonsRect(model)
            .map { $0.offsetBy(dx: panel.frame.minX, dy: panel.frame.minY).contains(NSEvent.mouseLocation) } ?? false
        if panel.ignoresMouseEvents == inside { panel.ignoresMouseEvents = !inside }
    }
}

struct FlowBarView: View {
    @ObservedObject var model: AppModel
    let onStop: () -> Void
    let onCancel: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            Spacer(minLength: 0)
            pill.padding(.bottom, FlowBarLayout.bottomPadding)
        }
        .frame(width: FlowBarLayout.panelSize.width, height: FlowBarLayout.panelSize.height)
        .animation(.spring(response: 0.3, dampingFraction: 0.8), value: model.phase)
        .animation(.spring(response: 0.3, dampingFraction: 0.8), value: model.handsFree)
    }

    @ViewBuilder private var pill: some View {
        if model.phase != .idle {
            let size = FlowBarLayout.pillSize(model)
            content
                .frame(width: size.width, height: size.height)
                .background(capsule)
        }
    }

    private var capsule: some View {
        Capsule()
            .fill(Color.black.opacity(0.88))
            .overlay(Capsule().strokeBorder(Color.white.opacity(0.22), lineWidth: 1))
            .shadow(color: .black.opacity(0.35), radius: 6, y: 2)
    }

    @ViewBuilder private var content: some View {
        switch model.phase {
        case .idle:
            EmptyView()
        case .listening:
            HStack(spacing: 10) {
                if model.handsFree { RoundButton(symbol: "xmark", fill: .white.opacity(0.16), action: onCancel) }
                LevelBars(levels: model.levels)
                if model.handsFree { RoundButton(symbol: "stop.fill", fill: .red, action: onStop) }
            }
        case .processing:
            ProcessingDots()
        }
    }
}

private struct LevelBars: View {
    let levels: [CGFloat]

    var body: some View {
        HStack(spacing: 2.5) {
            ForEach(levels.indices, id: \.self) { i in
                Capsule()
                    .fill(Color.white)
                    .frame(width: 3, height: 3 + levels[i] * 17)
            }
        }
        .frame(height: 20)
        .animation(.linear(duration: 0.06), value: levels)
    }
}

private struct ProcessingDots: View {
    var body: some View {
        TimelineView(.animation) { context in
            let t = context.date.timeIntervalSinceReferenceDate
            HStack(spacing: 5) {
                ForEach(0..<3, id: \.self) { i in
                    Circle()
                        .fill(Color.white)
                        .frame(width: 5, height: 5)
                        .opacity(0.3 + 0.7 * (0.5 + 0.5 * sin(t * 7 - Double(i) * 0.9)))
                }
            }
        }
    }
}

private struct RoundButton: View {
    let symbol: String
    let fill: Color
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 9, weight: .bold))
                .foregroundColor(.white)
                .frame(width: 22, height: 22)
                .background(Circle().fill(fill))
        }
        .buttonStyle(.plain)
    }
}
