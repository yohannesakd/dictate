// The main window. Home shows the status, any missing setup, this week's dictation, and recent dictations.
// History lists every saved dictation. Settings holds cleanup, dictionary, general options, shortcuts, and the model.

import AppKit
import SwiftUI

/// Which screen the window shows. The Settings menu item also sets it.
final class HubNavigation: ObservableObject {
    enum Screen { case home, history, settings }
    @Published var screen = Screen.home
}

struct HubView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var navigation: HubNavigation
    let controller: DictationController

    var body: some View {
        ZStack {
            switch navigation.screen {
            case .home:
                HomeScreen(model: model, controller: controller, navigation: navigation)
                    .transition(.move(edge: .leading))
            case .history:
                HistoryScreen(history: model.history) { navigation.screen = .home }
                    .transition(.move(edge: .trailing))
            case .settings:
                SettingsScreen(model: model, controller: controller) { navigation.screen = .home }
                    .transition(.move(edge: .trailing))
            }
        }
        .animation(.easeInOut(duration: 0.2), value: navigation.screen)
        .frame(minWidth: 460, minHeight: 520)
    }
}

/// The title row of History and Settings. Clicking it, or pressing Esc, goes back to Home.
private struct BackHeader: View {
    let title: String
    let back: () -> Void

    var body: some View {
        Button(action: back) {
            HStack(spacing: 6) {
                Image(systemName: "chevron.left").font(.system(size: 13, weight: .semibold))
                Text(title).font(.title3.weight(.semibold))
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .keyboardShortcut(.cancelAction)
        .help("Back")
        .padding(.horizontal, 24)
        .padding(.top, 6)
        .padding(.bottom, 4)
    }
}

// MARK: Home

private struct HomeScreen: View {
    @ObservedObject var model: AppModel
    let controller: DictationController
    @ObservedObject var navigation: HubNavigation

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
                .padding(.horizontal, 24)
                .padding(.top, 6)
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    if model.needsSetup {
                        SetupList(model: model, controller: controller).padding(.bottom, 20)
                    }
                    if let issue = model.smartIssue, model.smartCleanup {
                        Button { navigation.screen = .settings } label: {
                            Label("Smart cleanup: \(issue)", systemImage: "exclamationmark.triangle.fill")
                                .font(.callout).foregroundStyle(.orange).multilineTextAlignment(.leading)
                        }
                        .buttonStyle(.plain)
                        .padding(.bottom, 20)
                    }
                    if model.history.isEmpty {
                        GettingStarted()
                    } else {
                        WeekView(usage: model.usage)
                        recent.padding(.top, 28)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 24)
                .padding(.bottom, 24)
            }
        }
    }

    private var header: some View {
        HStack(spacing: 12) {
            Image(nsImage: appIcon()).resizable().frame(width: 40, height: 40)
            VStack(alignment: .leading, spacing: 2) {
                Text("Dictate").font(.title3.weight(.semibold))
                status
            }
            Spacer()
            Button { navigation.screen = .settings } label: { Image(systemName: "gearshape").font(.system(size: 15)) }
                .buttonStyle(.borderless)
                .help("Settings")
        }
        .padding(.bottom, 24)
    }

    @ViewBuilder private var status: some View {
        switch model.modelStatus {
        case .loading:
            Text("Loading the speech model…").foregroundStyle(.secondary)
        case .failed(let message):
            Text("The speech model did not load").foregroundStyle(.red).help(message)
        case .ready:
            Text(model.needsSetup ? "Finish the setup below to start" : "Hold fn and speak in any app")
                .foregroundStyle(.secondary)
        }
    }

    private var recent: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Recent").font(.headline)
                Spacer()
                if model.history.count > 3 {
                    Button("Show All") { navigation.screen = .history }.buttonStyle(.link)
                }
            }
            .padding(.bottom, 4)
            ForEach(Array(model.history.prefix(3).enumerated()), id: \.element.id) { index, item in
                if index > 0 { Divider() }
                HistoryRow(item: item)
            }
        }
    }
}

/// Shown before the first dictation: how to dictate.
private struct GettingStarted: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Put the cursor where the text goes. Then:").foregroundStyle(.secondary)
            step(keys: ["fn"], text: "Hold, speak, and release")
            step(keys: ["fn", "fn"], text: "Press twice to talk hands-free, then press fn to finish")
            step(keys: ["esc"], text: "Cancel")
        }
        .padding(.top, 8)
    }

    private func step(keys: [String], text: String) -> some View {
        HStack(spacing: 12) {
            HStack(spacing: 3) { ForEach(keys, id: \.self) { KeyCap(label: $0) } }
                .frame(width: 64, alignment: .leading)
            Text(text)
        }
    }
}

/// Words per day for the last 7 days, drawn like the flow bar's level bars, with three plain facts below.
private struct WeekView: View {
    let usage: Usage

    var body: some View {
        let week = usage.week()
        let most = max(week.map(\.day.words).max() ?? 0, 1)
        VStack(alignment: .leading, spacing: 18) {
            HStack(alignment: .bottom, spacing: 0) {
                ForEach(week.indices, id: \.self) { i in
                    let isToday = i == week.count - 1
                    VStack(spacing: 8) {
                        Capsule()
                            .fill(isToday ? Color.primary : Color.primary.opacity(0.18))
                            .frame(width: 14, height: 6 + 74 * CGFloat(week[i].day.words) / CGFloat(most))
                        Text(week[i].date.formatted(.dateTime.weekday(.narrow)))
                            .font(.caption)
                            .foregroundStyle(isToday ? .primary : .secondary)
                    }
                    .frame(maxWidth: .infinity)
                    .help("\(week[i].day.words.formatted()) words")
                }
            }
            .frame(height: 104, alignment: .bottom)

            VStack(alignment: .leading, spacing: 6) {
                Text(today(week.last?.day ?? Usage.Day())).font(.title3)
                if let latency = usage.medianLatency {
                    Text("Text appears \(Self.duration(latency)) after you release fn.")
                        .foregroundStyle(.secondary)
                }
                if let wpm = usage.wordsPerMinute {
                    Text("You speak \(wpm) words per minute.").foregroundStyle(.secondary)
                }
            }
        }
    }

    private func today(_ day: Usage.Day) -> String {
        switch day.dictations {
        case 0: return "Nothing dictated today."
        case 1: return "\(day.words.formatted()) words today, in 1 dictation."
        default: return "\(day.words.formatted()) words today, in \(day.dictations) dictations."
        }
    }

    private static func duration(_ ms: Int) -> String {
        ms < 1000 ? "\(ms) ms" : String(format: "%.1f s", Double(ms) / 1000)
    }
}

private struct HistoryScreen: View {
    let history: [HistoryItem]
    let back: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            BackHeader(title: "History", back: back)
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(history.enumerated()), id: \.element.id) { index, item in
                        if index > 0 { Divider() }
                        HistoryRow(item: item)
                    }
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 16)
            }
        }
    }
}

private struct SetupList: View {
    @ObservedObject var model: AppModel
    let controller: DictationController

    var body: some View {
        VStack(spacing: 10) {
            if !model.micAuthorized {
                SetupRow(symbol: "mic.fill", title: "Allow the microphone", button: "Allow",
                         action: controller.requestMicrophone)
            }
            if !model.accessibilityTrusted {
                SetupRow(symbol: "accessibility", title: "Allow Accessibility to use fn and type text",
                         button: "Open Settings", action: controller.requestAccessibility)
            } else if !model.keyboardReady {
                SetupRow(symbol: "keyboard", title: "The fn key listener did not start",
                         button: "Try Again", action: controller.refreshPermissions)
            }
            if !model.fnKeyFree {
                SetupRow(symbol: "globe", title: "Set “Press 🌐 key to” to “Do Nothing”",
                         button: "Open Keyboard", action: controller.openKeyboardSettings)
            }
        }
    }
}

private struct SetupRow: View {
    let symbol: String
    let title: String
    let button: String
    let action: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: symbol).foregroundStyle(.orange).frame(width: 18)
            Text(title)
            Spacer()
            Button(button, action: action)
        }
    }
}

private struct HistoryRow: View {
    let item: HistoryItem
    @State private var hovering = false
    @State private var expanded = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(item.text).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 10) {
                Text(time).foregroundStyle(.tertiary)
                if hasDetails {
                    Button(expanded ? "Hide original" : "Original") { expanded.toggle() }
                        .buttonStyle(.link)
                }
                Spacer()
                CopyButton(text: item.text).opacity(hovering ? 1 : 0)
            }
            .font(.caption)
            if expanded {
                if let raw = item.raw {
                    Text(raw).font(.callout).foregroundStyle(.secondary)
                        .textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                }
                if let note = item.note { Text(note).font(.caption).foregroundStyle(.orange) }
            }
        }
        .padding(.vertical, 12)
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .contextMenu {
            Button("Copy") { copy(item.text) }
            if let raw = item.raw { Button("Copy Original") { copy(raw) } }
        }
    }

    private var hasDetails: Bool { item.raw != nil || item.note != nil }

    private var time: String {
        Calendar.current.isDateInToday(item.date)
            ? item.date.formatted(date: .omitted, time: .shortened)
            : item.date.formatted(date: .abbreviated, time: .shortened)
    }
}

private struct CopyButton: View {
    let text: String
    @State private var copied = false

    var body: some View {
        Button {
            copy(text)
            copied = true
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { copied = false }
        } label: {
            Image(systemName: copied ? "checkmark" : "doc.on.doc")
        }
        .buttonStyle(.borderless)
        .help("Copy")
    }
}

private func copy(_ text: String) {
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(text, forType: .string)
}

// MARK: Settings

private struct SettingsScreen: View {
    @ObservedObject var model: AppModel
    let controller: DictationController
    let close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            BackHeader(title: "Settings", back: close)

            Form {
                SmartCleanupSection(model: model, controller: controller)
                Section {
                    Toggle("Remove filler words", isOn: $model.removeFillers)
                    Toggle("Remove repeated words", isOn: $model.removeRepeats)
                    Toggle("“Scratch that” deletes the sentence before it", isOn: $model.scratchThat)
                } header: {
                    Text("Rules")
                } footer: {
                    if model.smartCleanup {
                        Text("Used when a dictation does not need the model, or when the model fails.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                DictionarySection(entries: $model.dictionary)
                Section("General") {
                    Toggle("Show the bar when not dictating", isOn: $model.showIdleBar)
                    Toggle("Open at login", isOn: $model.openAtLogin)
                }
                Section("Shortcuts") {
                    ShortcutRow(title: "Push to talk", keys: [["fn"]])
                    ShortcutRow(title: "Hands-free", keys: [["fn", "fn"], ["fn", "Space"]])
                    ShortcutRow(title: "Cancel", keys: [["esc"]])
                    ShortcutRow(title: "Insert last dictation", keys: [["⌃", "⌘", "V"]])
                    ShortcutRow(title: "Add selected text to the dictionary", keys: [["⌃", "⌘", "E"]])
                }
                Section {
                    LabeledContent("Speech model") { ModelStatusLabel(status: model.modelStatus) }
                    LabeledContent("Transcript log") {
                        Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([TranscriptLog.url]) }
                    }
                } header: {
                    Text("About")
                } footer: {
                    Text("Phonon-2 by Fermion Research, based on NVIDIA Parakeet TDT 0.6B v3. Model licence: CC-BY-4.0. Speech recognition runs on this Mac.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)
        }
    }
}

private struct SmartCleanupSection: View {
    @ObservedObject var model: AppModel
    let controller: DictationController
    @State private var key = ""

    var body: some View {
        Section {
            Toggle("Smart cleanup", isOn: $model.smartCleanup)
            if model.smartCleanup {
                Picker("Model", selection: $model.cleanupProvider) {
                    ForEach(CleanupProvider.allCases) { Text($0.title).tag($0) }
                }
                account
                if let issue = model.smartIssue {
                    Label(issue, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.orange)
                }
            }
        } header: {
            Text("Cleanup")
        } footer: {
            Text(footer).font(.caption).foregroundStyle(.secondary)
        }
    }

    private var footer: String {
        guard model.smartCleanup else {
            return "A language model can remove fillers by context and apply spoken corrections such as “at two, actually three”."
        }
        switch model.cleanupProvider {
        case .deepseek:
            return "Runs only for dictations with fillers, corrections, or dictionary words. That text goes to DeepSeek and uses your API key."
        case .chatgpt:
            return "Runs only for dictations with fillers, corrections, or dictionary words. That text goes to OpenAI and uses your ChatGPT plan."
        }
    }

    @ViewBuilder private var account: some View {
        switch model.cleanupProvider {
        case .deepseek:
            if model.deepseekKeySaved {
                LabeledContent("API key") {
                    HStack {
                        Text("Saved in the Keychain").foregroundStyle(.secondary)
                        Button("Remove", action: controller.removeDeepSeekKey)
                    }
                }
            } else {
                HStack {
                    SecureField("API key", text: $key, prompt: Text("Paste your DeepSeek API key"))
                    Button("Save") {
                        controller.saveDeepSeekKey(key)
                        key = ""
                    }
                    .disabled(key.isEmpty)
                }
            }
        case .chatgpt:
            LabeledContent("Account") {
                if model.chatgptSignedIn {
                    HStack {
                        Text("Signed in").foregroundStyle(.secondary)
                        Button("Sign Out", action: controller.signOutChatGPT)
                    }
                } else if model.signingIn {
                    Text("Finish the sign-in in your browser").foregroundStyle(.secondary)
                } else {
                    Button("Sign In with ChatGPT", action: controller.signInChatGPT)
                }
            }
        }
    }
}

/// Rows of "heard as" -> "write as". Changes save as you type.
private struct DictionarySection: View {
    @Binding var entries: [DictionaryEntry]

    var body: some View {
        Section {
            ForEach($entries) { $entry in
                HStack {
                    TextField("Heard as", text: $entry.heard, prompt: Text("Heard as"))
                    Image(systemName: "arrow.right").foregroundStyle(.secondary)
                    TextField("Write as", text: $entry.written, prompt: Text("Write as"))
                    Button { entries.removeAll { $0.id == entry.id } } label: { Image(systemName: "minus.circle") }
                        .buttonStyle(.borderless)
                        .help("Delete")
                }
                .labelsHidden()
            }
            Button { entries.append(DictionaryEntry()) } label: { Label("Add Word", systemImage: "plus") }
                .buttonStyle(.borderless)
        } header: {
            Text("Dictionary")
        } footer: {
            Text("Words the speech model mishears, such as “ask soul” for “ask Sol”. To add one, select it in any app and press ⌃⌘E.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }
}

private struct ModelStatusLabel: View {
    let status: ModelStatus

    var body: some View {
        switch status {
        case .ready:
            Text("Phonon-2, ready").foregroundStyle(.secondary)
        case .loading:
            HStack(spacing: 5) { ProgressView().controlSize(.mini); Text("Loading").foregroundStyle(.secondary) }
        case .failed(let message):
            Text("Phonon-2, error").foregroundStyle(.red).help(message)
        }
    }
}

private struct ShortcutRow: View {
    let title: String
    let keys: [[String]]

    var body: some View {
        LabeledContent(title) {
            HStack(spacing: 6) {
                ForEach(keys.indices, id: \.self) { i in
                    if i > 0 { Text("or").font(.caption).foregroundStyle(.secondary) }
                    HStack(spacing: 3) { ForEach(keys[i], id: \.self) { KeyCap(label: $0) } }
                }
            }
        }
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
