// The main window: setup, model choice, shortcuts, and dictation history.

import AppKit
import SwiftUI

struct HubView: View {
    @ObservedObject var model: AppModel
    let controller: DictationController

    var body: some View {
        ScrollView { content }
            .frame(minWidth: 580, minHeight: 640)
    }

    var content: some View {
        VStack(alignment: .leading, spacing: 26) {
            header
            if model.needsSetup { setup }
            section("Model") { ModelCard(status: model.modelStatus) }
            section("Shortcuts") {
                VStack(spacing: 0) {
                    ShortcutRow(title: "Push to talk", note: "Hold, speak, release", keys: [["fn"]])
                    Divider()
                    ShortcutRow(title: "Hands-free", note: "Press again to finish", keys: [["fn", "fn"], ["fn", "Space"]])
                    Divider()
                    ShortcutRow(title: "Cancel", note: nil, keys: [["esc"]])
                    Divider()
                    ShortcutRow(title: "Insert last dictation", note: nil, keys: [["⌃", "⌘", "V"]])
                }
                .card()
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Remove filler words")
                        Text("Drops um and uh, repeated words, and text before “scratch that”.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Toggle("", isOn: $model.removeFillers).toggleStyle(.switch).controlSize(.small).labelsHidden()
                }
                .padding(.vertical, 6)
                .card()
                HStack {
                    Text("Show the flow bar when not dictating")
                    Spacer()
                    Toggle("", isOn: $model.showIdleBar).toggleStyle(.switch).controlSize(.small).labelsHidden()
                }
                .padding(.vertical, 6)
                .card()
            }
            section("History") { history }
        }
        .padding(.horizontal, 28)
        .padding(.top, 40)
        .padding(.bottom, 28)
    }

    private var header: some View {
        HStack(spacing: 14) {
            Image(nsImage: appIcon()).resizable().frame(width: 48, height: 48)
            VStack(alignment: .leading, spacing: 3) {
                Text("Dictate").font(.system(size: 24, weight: .semibold))
                Text("Hold fn and speak in any app. Release to insert the text. Everything runs on this Mac.")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var setup: some View {
        section("Finish setup") {
            VStack(spacing: 0) {
                if !model.micAuthorized {
                    SetupRow(symbol: "mic.fill", title: "Microphone", detail: "Dictate listens only while you dictate.",
                             button: "Allow", action: controller.requestMicrophone)
                }
                if !model.accessibilityTrusted {
                    SetupRow(symbol: "accessibility", title: "Accessibility",
                             detail: "Needed for the fn shortcut and to paste into other apps.",
                             button: "Open Settings", action: controller.requestAccessibility)
                }
                if !model.fnKeyFree {
                    SetupRow(symbol: "globe", title: "fn key",
                             detail: "In Keyboard settings, set “Press 🌐 key to” to “Do Nothing”.",
                             button: "Open Keyboard", action: controller.openKeyboardSettings)
                }
            }
            .card()
        }
    }

    @ViewBuilder private var history: some View {
        if model.history.isEmpty {
            Text("Your dictations appear here.")
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, minHeight: 60)
                .card()
        } else {
            VStack(spacing: 0) {
                ForEach(Array(model.history.prefix(30).enumerated()), id: \.element.id) { index, item in
                    if index > 0 { Divider() }
                    HistoryRow(item: item)
                }
            }
            .card()
        }
    }

    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title.uppercased())
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(.secondary)
            content()
        }
    }
}

private struct ModelCard: View {
    let status: ModelStatus

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("Phonon-2").font(.system(size: 14, weight: .semibold))
                Spacer()
                statusLabel
            }
            Text("English · runs on the Neural Engine on this Mac")
                .font(.callout).foregroundStyle(.secondary)
            Text("By Fermion Research, based on NVIDIA Parakeet TDT 0.6B v3. Model licence: CC-BY-4.0.")
                .font(.caption).foregroundStyle(.tertiary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.vertical, 10)
        .card()
    }

    @ViewBuilder private var statusLabel: some View {
        switch status {
        case .ready:
            Label("Ready", systemImage: "circle.fill").labelStyle(StatusLabelStyle(color: .green))
        case .loading:
            HStack(spacing: 5) { ProgressView().controlSize(.mini); Text("Loading").font(.caption) }
        case .failed(let message):
            Label("Error", systemImage: "circle.fill").labelStyle(StatusLabelStyle(color: .red)).help(message)
        }
    }
}

private struct StatusLabelStyle: LabelStyle {
    let color: Color
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 4) {
            configuration.icon.font(.system(size: 6)).foregroundColor(color)
            configuration.title.font(.caption).foregroundStyle(.secondary)
        }
    }
}

private struct ShortcutRow: View {
    let title: String
    let note: String?
    let keys: [[String]]

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                if let note { Text(note).font(.caption).foregroundStyle(.secondary) }
            }
            Spacer()
            ForEach(keys.indices, id: \.self) { i in
                if i > 0 { Text("or").font(.caption).foregroundStyle(.secondary) }
                HStack(spacing: 3) { ForEach(keys[i], id: \.self) { KeyCap(label: $0) } }
            }
        }
        .padding(.vertical, 9)
    }
}

private struct KeyCap: View {
    let label: String
    var body: some View {
        Text(label)
            .font(.system(size: 11, weight: .medium, design: .rounded))
            .padding(.horizontal, 7)
            .padding(.vertical, 3)
            .background(RoundedRectangle(cornerRadius: 5).fill(Color.primary.opacity(0.08)))
            .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(Color.primary.opacity(0.12)))
    }
}

private struct SetupRow: View {
    let symbol: String
    let title: String
    let detail: String
    let button: String
    let action: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: symbol).font(.system(size: 15)).foregroundColor(.orange).frame(width: 22)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button(button, action: action)
        }
        .padding(.vertical, 9)
    }
}

private struct HistoryRow: View {
    let item: HistoryItem
    @State private var copied = false

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                Text(item.text).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                Text(item.date.formatted(date: .abbreviated, time: .shortened))
                    .font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(item.text, forType: .string)
                copied = true
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { copied = false }
            } label: {
                Image(systemName: copied ? "checkmark" : "doc.on.doc")
            }
            .buttonStyle(.borderless)
            .help("Copy")
        }
        .padding(.vertical, 9)
    }
}

private extension View {
    func card() -> some View {
        padding(.horizontal, 14)
            .padding(.vertical, 4)
            .background(RoundedRectangle(cornerRadius: 12).fill(Color(nsColor: .controlBackgroundColor)))
            .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color.primary.opacity(0.1)))
    }
}

func appIcon() -> NSImage {
    NSImage(size: NSSize(width: 512, height: 512), flipped: false) { rect in
        let shape = NSBezierPath(roundedRect: rect.insetBy(dx: 44, dy: 44), xRadius: 104, yRadius: 104)
        NSGradient(starting: NSColor(calibratedRed: 0.18, green: 0.16, blue: 0.22, alpha: 1),
                   ending: NSColor(calibratedRed: 0.06, green: 0.06, blue: 0.08, alpha: 1))?.draw(in: shape, angle: -90)
        // Five white level bars, like the flow bar.
        let heights: [CGFloat] = [0.28, 0.5, 0.72, 0.5, 0.28]
        let barWidth: CGFloat = 34, gap: CGFloat = 22
        let total = CGFloat(heights.count) * barWidth + CGFloat(heights.count - 1) * gap
        NSColor.white.setFill()
        for (i, h) in heights.enumerated() {
            let height = rect.height * h
            let bar = NSRect(x: rect.midX - total / 2 + CGFloat(i) * (barWidth + gap), y: rect.midY - height / 2,
                             width: barWidth, height: height)
            NSBezierPath(roundedRect: bar, xRadius: barWidth / 2, yRadius: barWidth / 2).fill()
        }
        return true
    }
}
