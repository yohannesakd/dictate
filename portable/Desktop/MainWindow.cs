using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Desktop;

public sealed class MainWindow : Window
{
    private readonly DesktopSession session;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private readonly StackPanel body = new() { Spacing = 18 };
    private string screen = "Home";

    public MainWindow(DesktopSession session)
    {
        this.session = session;
        FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter");
        Background = Brushes.White; Foreground = new SolidColorBrush(Color.Parse("#1D1D1F"));
        Title = "Dictate"; Width = 500; Height = 640; MinWidth = 460; MinHeight = 520;
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(new TextBlock { Text = "Dictate", FontSize = 24, FontWeight = FontWeight.SemiBold });
        header.Children.Add(status);
        var engines = new ComboBox { ItemsSource = SpeechProfiles.Engines, SelectedItem = session.Settings.SpeechEngine, MinWidth = 140 };
        Avalonia.Automation.AutomationProperties.SetName(engines, "Speech engine");
        engines.SelectionChanged += async (_, _) =>
        {
            if (engines.SelectedItem is not SpeechEngineKind engine || engine == session.Settings.SpeechEngine) return;
            try { await session.SelectEngineAsync(engine); }
            catch (Exception error) { session.Message(error.Message); }
        };
        session.SettingsChanged += () => Dispatcher.UIThread.Post(() => { engines.SelectedItem = session.Settings.SpeechEngine; Render(); });
        header.Children.Add(engines);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var tab in new[] { "Home", "History", "Settings" }) tabs.Children.Add(Button(tab, () => { screen = tab; Render(); }));
        header.Children.Add(tabs);
        var layout = new DockPanel { Margin = new Thickness(24) };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        layout.Children.Add(new ScrollViewer { Content = body, Margin = new Thickness(0, 24, 0, 0) });
        Content = layout;
        session.Notice += _ => Dispatcher.UIThread.Post(() => status.Text = session.Status);
        session.Controller.Completed += _ => Dispatcher.UIThread.Post(Render);
        session.Controller.PhaseChanged += _ => Dispatcher.UIThread.Post(() => { if (screen == "Home") Render(); });
        KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Escape) session.Shortcuts.Cancel(); };
        Render();
    }

    private void Render()
    {
        status.Text = session.Status;
        body.Children.Clear();
        if (screen == "Settings") Settings();
        else if (screen == "History") History();
        else Home();
    }

    private void Home()
    {
        body.Children.Add(Text("Put the cursor where the text goes."));
        var shortcut = OperatingSystem.IsMacOS() ? "fn" : "F8";
        body.Children.Add(Text($"Hold {shortcut}, speak, and release. Press {shortcut} twice for hands-free dictation. Press Esc to cancel."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button(session.Controller.Phase == DictationPhase.Listening ? "Stop dictation" : "Start dictation", async () =>
        {
            if (session.Controller.Phase == DictationPhase.Listening) await session.Controller.FinishAsync();
            else session.Shortcuts.ToggleHandsFree();
        }));
        actions.Children.Add(Button("Cancel", session.Shortcuts.Cancel));
        body.Children.Add(actions);
        if (OperatingSystem.IsMacOS())
        {
            var permissions = MacNative.Permissions(false);
            if (!permissions.GetProperty("microphone").GetBoolean() || !permissions.GetProperty("accessibility").GetBoolean())
                body.Children.Add(Text("Allow microphone and Accessibility access in Settings to dictate in other apps."));
        }
        if (OperatingSystem.IsLinux() && LinuxDelivery.IsWayland) body.Children.Add(Text("On Wayland, use the recording controls. Global shortcuts are unavailable. If the field does not expose accessible text, copy from History."));
        var history = session.Store.History().ToArray();
        var usage = session.Store.Usage();
        var week = usage.Week.Select(day => day.Date).ToArray();
        var words = usage.Week.Select(day => day.Words).ToArray();
        var chart = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, Height = 110, VerticalAlignment = VerticalAlignment.Bottom };
        for (var i = 0; i < 7; i++)
        {
            var column = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom };
            column.Children.Add(new Border { Width = 14, Height = 6 + 74d * words[i] / Math.Max(1, words.Max()), CornerRadius = new CornerRadius(7), Background = Brushes.Gray, Opacity = i == 6 ? 1 : .35 });
            column.Children.Add(new TextBlock { Text = week[i].ToString("ddd")[..1], HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            chart.Children.Add(column);
        }
        body.Children.Add(chart);
        body.Children.Add(Text($"{words[^1]} words today. {words.Sum()} words this week."));
        if (usage.WordsPerMinute is { } speed) body.Children.Add(Text($"{speed} words per minute."));
        if (usage.MedianLatency is { } latency) body.Children.Add(Text($"{latency} ms from release to verified text."));
        body.Children.Add(Heading("Recent"));
        foreach (var record in history.Take(3)) body.Children.Add(HistoryRow(record));
        if (history.Length == 0) body.Children.Add(Text("Your dictations appear here after you speak."));
    }

    private void History()
    {
        body.Children.Add(Heading("History"));
        foreach (var item in session.Store.History()) body.Children.Add(HistoryRow(item));
        body.Children.Add(Button("Open transcript log", () => OpenFolder(session.Store.DirectoryPath)));
    }

    private Control HistoryRow(TranscriptRecord item)
    {
        var row = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8) };
        row.Children.Add(new TextBlock { Text = item.Date.ToLocalTime().ToString("g") + "  " + item.Delivery, FontSize = 12, Opacity = .6 });
        row.Children.Add(Text(item.Text));
        if (item.Note is not null) row.Children.Add(new TextBlock { Text = item.Note, TextWrapping = TextWrapping.Wrap, Opacity = .7, FontSize = 12 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Copy", async () => { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(item.Text); }));
        actions.Children.Add(Button("Copy original", async () => { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(item.Raw); }));
        row.Children.Add(actions);
        return row;
    }

    private void Settings()
    {
        body.Children.Add(Heading("Speech recognition"));
        body.Children.Add(Text(SpeechProfiles.Description(session.Settings.SpeechEngine)));
        body.Children.Add(Text("English speech recognition runs on your CPU. Choose Whistle or Whisper from the menu above."));
        body.Children.Add(Button("Download and load model", PrepareSpeech));
        if (OperatingSystem.IsMacOS())
        {
            body.Children.Add(Heading("Permissions"));
            body.Children.Add(Button("Allow microphone and Accessibility", () =>
            {
                MacNative.Permissions(true);
                session.Hotkeys.Start();
                session.Message("Enable Dictate in System Settings → Privacy & Security → Accessibility.");
            }));
            body.Children.Add(Button("Enable shortcuts", session.Hotkeys.Start));
            if (!MacNative.Permissions(false).GetProperty("fn_free").GetBoolean())
            {
                body.Children.Add(Text("Set “Press fn key to” to “Do Nothing” in macOS Keyboard Settings."));
                body.Children.Add(Button("Open Keyboard Settings", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", "x-apple.systempreferences:com.apple.Keyboard-Settings.extension") { UseShellExecute = true })));
            }
        }
        body.Children.Add(Heading("Cleanup"));
        body.Children.Add(Check("Remove filler sounds", session.Settings.Rules.Fillers, value => Save(session.Settings with { Rules = session.Settings.Rules with { Fillers = value } })));
        body.Children.Add(Check("Remove stutters", session.Settings.Rules.Repeats, value => Save(session.Settings with { Rules = session.Settings.Rules with { Repeats = value } })));
        body.Children.Add(Check("Apply scratch that", session.Settings.Rules.ScratchThat, value => Save(session.Settings with { Rules = session.Settings.Rules with { ScratchThat = value } })));
        body.Children.Add(Check("Smart cleanup", session.Settings.SmartCleanup, value => Save(session.Settings with { SmartCleanup = value })));
        body.Children.Add(Text("Smart cleanup sends the transcript to the selected provider. Speech recognition stays on this device."));
        var provider = new ComboBox { ItemsSource = Enum.GetValues<CleanupProvider>(), SelectedItem = session.Settings.Provider };
        provider.SelectionChanged += async (_, _) => { await Save(session.Settings with { Provider = (CleanupProvider)provider.SelectedItem! }); Render(); };
        body.Children.Add(provider);
        if (session.Settings.Provider == CleanupProvider.ChatGPT)
        {
            body.Children.Add(Button("Sign in with ChatGPT", async () => await DesktopSignIn.SignInAsync(new ChatGptAuth(session.Http, session.Secrets))));
            body.Children.Add(Button("Sign out", async () => await session.Secrets.WriteAsync("chatgpt-token", null)));
        }
        else
        {
            var key = new TextBox { PasswordChar = '•', PlaceholderText = "API key" };
            body.Children.Add(key);
            body.Children.Add(Button("Save API key", async () => { await session.Secrets.WriteAsync(session.Settings.Provider.ToString().ToLowerInvariant() + "-api-key", key.Text?.Trim()); key.Text = ""; session.Message("API key saved in the system credential store."); }));
            body.Children.Add(Button("Remove API key", async () => await session.Secrets.WriteAsync(session.Settings.Provider.ToString().ToLowerInvariant() + "-api-key", null)));
        }
        body.Children.Add(Heading("Personal dictionary"));
        foreach (var entry in session.Settings.Dictionary)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(Text(entry.Heard + " → " + entry.Written));
            row.Children.Add(Button("Remove", async () => { await Save(session.Settings with { Dictionary = session.Settings.Dictionary.Where(item => item != entry).ToArray() }); Render(); }));
            body.Children.Add(row);
        }
        var heard = new TextBox { PlaceholderText = "Heard as" };
        var written = new TextBox { PlaceholderText = "Write as" };
        body.Children.Add(heard); body.Children.Add(written);
        body.Children.Add(Button("Add word", async () =>
        {
            if (string.IsNullOrWhiteSpace(heard.Text) || string.IsNullOrWhiteSpace(written.Text)) return;
            await Save(session.Settings with { Dictionary = [..session.Settings.Dictionary, new(heard.Text.Trim(), written.Text.Trim())] }); Render();
        }));
        body.Children.Add(Heading("General"));
        body.Children.Add(Check("Open at login", session.Settings.OpenAtLogin, async value => { DesktopStartup.Set(value); await Save(session.Settings with { OpenAtLogin = value }); }));
        body.Children.Add(Button("Undo last dictation", async () => session.Message(await session.Controller.CorrectLastAsync(false) ? "Dictation removed." : "The field cannot be corrected safely.")));
        body.Children.Add(Button("Restore raw transcript", async () => session.Message(await session.Controller.CorrectLastAsync(true) ? "Original transcript restored." : "The field cannot be corrected safely.")));
        body.Children.Add(Button("Quit Dictate", () => (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()));
    }

    public void CaptureWord()
    {
        session.Shortcuts.Cancel();
        try
        {
            var selected = OperatingSystem.IsMacOS() ? MacDelivery.SelectedText() : OperatingSystem.IsWindows() ? WindowsDelivery.SelectedText() : LinuxDelivery.SelectedText();
            if (string.IsNullOrWhiteSpace(selected)) { session.Message("Select the word to add, then use the dictionary shortcut."); return; }
            var heard = new TextBox { PlaceholderText = "Heard as" };
            var written = new TextBox { Text = selected.Trim(), PlaceholderText = "Write as" };
            var dialog = new Window { Title = "Add dictionary word", Width = 360, Height = 250, CanResize = false };
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
            panel.Children.Add(Label("Heard as", heard)); panel.Children.Add(Label("Write as", written));
            panel.Children.Add(Button("Save word", async () =>
            {
                if (string.IsNullOrWhiteSpace(heard.Text) || string.IsNullOrWhiteSpace(written.Text)) return;
                await Save(session.Settings with { Dictionary = [..session.Settings.Dictionary, new(heard.Text.Trim(), written.Text.Trim())] });
                dialog.Close(); session.Message("Dictionary word saved.");
            }));
            dialog.Content = panel; dialog.Show();
        }
        catch (Exception error) { session.Message(error.Message); }
    }

    private async Task PrepareSpeech()
    {
        if (session.Controller.Phase != DictationPhase.Idle) { session.Message("Finish or cancel dictation before loading a model."); return; }
        await session.Speech.PrepareAsync(session.Settings.SpeechEngine, session.Settings.SpeechModel);
    }
    private async Task Save(DictationSettings value) { await session.Store.SaveSettingsAsync(value); session.Settings = value; session.RefreshSettings(); }
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeight.SemiBold };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private static Control Label(string label, Control input) => new StackPanel { Spacing = 6, Children = { Text(label), input } };
    private Button Button(string label, Action action) => Button(label, () => { action(); return Task.CompletedTask; });
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); } catch (Exception error) { session.Message(error.Message); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
    private CheckBox Check(string label, bool value, Func<bool, Task> changed)
    {
        var check = new CheckBox { Content = label, IsChecked = value };
        check.IsCheckedChanged += async (_, _) => { try { await changed(check.IsChecked == true); } catch (Exception error) { session.Message(error.Message); check.IsChecked = value; } };
        return check;
    }
    private static void OpenFolder(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsWindows() ? "explorer" : "xdg-open", path) { UseShellExecute = true });
}
