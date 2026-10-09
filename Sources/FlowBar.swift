// The flow bar: a small pill at the bottom center of the screen.
// Idle: a thin capsule (hover shows a hint, click starts hands-free).
// Listening: white level bars; hands-free adds cancel and stop buttons.
// Processing: pulsing dots. The panel never takes keyboard focus from the target app.

import AppKit
import Combine
import SwiftUI

enum FlowBarLayout {
    static let panelSize = CGSize(width: 420, height: 90)
    static let bottomPadding: CGFloat = 10

    /// Pill size for interactive phases; nil means the pill sizes to its text.
    static func pillSize(_ model: AppModel) -> CGSize? {
        switch model.phase {
        case .idle:
            if model.hovering { return CGSize(width: 214, height: 30) }
            return model.showIdleBar ? CGSize(width: 42, height: 9) : .zero
        case .listening: return model.handsFree ? CGSize(width: 148, height: 36) : CGSize(width: 92, height: 32)
        case .processing: return CGSize(width: 92, height: 32)
        case .notice: return nil
        }
    }

    /// Area that receives the mouse, in panel coordinates (origin bottom-left). Slightly larger than the pill.
    static func hitRect(_ model: AppModel) -> CGRect? {
        let interactive: Bool
        switch model.phase {
        case .idle: interactive = model.showIdleBar
        case .listening: interactive = model.handsFree
        case .processing, .notice: interactive = false
        }
        guard interactive, let size = pillSize(model) else { return nil }
        let hit = CGSize(width: max(size.width, 80) + 12, height: max(size.height, 22) + 12)
        return CGRect(x: (panelSize.width - hit.width) / 2, y: bottomPadding - 6, width: hit.width, height: hit.height)
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
    private var observers: [Any] = []
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
                               onStart: { [weak controller] in controller?.startHandsFree() },
                               onStop: { [weak controller] in controller?.finish() },
                               onCancel: { [weak controller] in controller?.cancel() })
        panel.contentView = FirstClickHostingView(rootView: view)
        reposition()
        panel.orderFrontRegardless()

        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in self?.reposition() }
        // Pass clicks through everywhere except the pill, and drive the hover state.
        // Event monitors instead of a polling timer: no work while the mouse is still.
        observers.append(NSEvent.addGlobalMonitorForEvents(matching: .mouseMoved) { [weak self] _ in self?.trackMouse() } as Any)
        observers.append(NSEvent.addLocalMonitorForEvents(matching: .mouseMoved) { [weak self] event in
            self?.trackMouse()
            return event
        } as Any)
        panel.acceptsMouseMovedEvents = true
        stateChange = model.objectWillChange
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.trackMouse() }
    }

    private func reposition() {
        guard let frame = (NSScreen.screens.first ?? NSScreen.main)?.visibleFrame else { return }
        panel.setFrameOrigin(CGPoint(x: frame.midX - FlowBarLayout.panelSize.width / 2, y: frame.minY + 2))
    }

    private func trackMouse() {
        let mouse = NSEvent.mouseLocation
        let inside: Bool
        if let rect = FlowBarLayout.hitRect(model) {
            inside = rect.offsetBy(dx: panel.frame.minX, dy: panel.frame.minY).contains(mouse)
        } else {
            inside = false
        }
        if panel.ignoresMouseEvents == inside { panel.ignoresMouseEvents = !inside }
        let hovering = inside && model.phase == .idle
        if model.hovering != hovering { model.hovering = hovering }
    }
}

struct FlowBarView: View {
    @ObservedObject var model: AppModel
    let onStart: () -> Void
    let onStop: () -> Void
    let onCancel: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            Spacer(minLength: 0)
            pill.padding(.bottom, FlowBarLayout.bottomPadding)
        }
        .frame(width: FlowBarLayout.panelSize.width, height: FlowBarLayout.panelSize.height)
        .animation(.spring(response: 0.3, dampingFraction: 0.8), value: model.phase)
        .animation(.spring(response: 0.3, dampingFraction: 0.8), value: model.hovering)
        .animation(.spring(response: 0.3, dampingFraction: 0.8), value: model.handsFree)
    }

    @ViewBuilder private var pill: some View {
        if let size = FlowBarLayout.pillSize(model) {
            content
                .frame(width: size.width, height: size.height)
                .background(capsule)
                .opacity(size == .zero ? 0 : 1)
                .contentShape(Capsule())
                .onTapGesture { if model.phase == .idle { onStart() } }
        } else if case .notice(let text) = model.phase {
            Text(text)
                .font(.system(size: 12, weight: .medium))
                .foregroundColor(.white)
                .lineLimit(1)
                .padding(.horizontal, 14)
                .frame(height: 30)
                .frame(maxWidth: FlowBarLayout.panelSize.width - 20)
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
            if model.hovering {
                Text("Click or hold fn to start dictating")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(.white.opacity(0.9))
                    .fixedSize()
                    .transition(.opacity)
            }
        case .listening:
            HStack(spacing: 10) {
                if model.handsFree { RoundButton(symbol: "xmark", fill: .white.opacity(0.16), action: onCancel) }
                LevelBars(levels: model.levels)
                if model.handsFree { RoundButton(symbol: "stop.fill", fill: .red, action: onStop) }
            }
        case .processing:
            ProcessingDots()
        case .notice:
            EmptyView()
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
