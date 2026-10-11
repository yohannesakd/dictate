using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dictate.Core;
using Dictate.Checks;
using Dictate.Desktop;
using Dictate.Speech;

internal sealed class MacChecks : Application
{
    internal static string Output = "";
    internal static string Fixtures = "";
    internal static string Models = "";
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Light; Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = new DesktopSession(Path.Combine(Output, "data"));
            var window = new MainWindow(session);
            desktop.MainWindow = window;
            window.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
            {
                try { await CheckAsync(session, window); File.WriteAllText(Path.Combine(Output, "exit-code.txt"), "0"); }
                catch (Exception error) { File.WriteAllText(Path.Combine(Output, "error.txt"), error.ToString()); File.WriteAllText(Path.Combine(Output, "exit-code.txt"), "1"); }
                finally { session.Dispose(); desktop.Shutdown(); }
            });
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static async Task CheckAsync(DesktopSession session, MainWindow window)
    {
        Directory.CreateDirectory(Output);
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var notes = new List<string>();
        Require(JsonSerializer.Deserialize<DictationSettings>(MacNative.TakeString(MacNative.LegacySettings())!, FileStore.Json) is not null,
            "The stable Mac preference domain could not be read.");
        var imported = MacMigration.ConvertHistory([
            "{\"date\":\"2026-10-10T10:00:00Z\",\"raw\":\"old\",\"text\":\"old\",\"delivery_outcome\":\"sent\"}",
            "{\"id\":\"082229E2-537B-4DA3-A784-DE94B7DBBC90\",\"date\":\"2026-10-10T10:29:28Z\",\"delivery_outcome\":\"unexpected\",\"verification_mismatch\":\"selection\",\"observed_utf16_units\":263}"
        ]).ToArray();
        Require(imported.Length == 2 && imported[0].Id != Guid.Empty && imported[1].Id == Guid.Parse("082229E2-537B-4DA3-A784-DE94B7DBBC90")
            && imported[1].DeliveryEvidence["verification_mismatch"] == "selection" && imported[1].DeliveryEvidence["observed_utf16_units"] == "263",
            "Legacy history lost identity or delivery evidence.");
        var permissions = MacNative.Permissions(false);
        File.WriteAllText(Path.Combine(Output, "permissions.json"), permissions.GetRawText());
        Require(permissions.GetProperty("accessibility").GetBoolean(), "Accessibility permission is required for the signed verification bundle.");
        Require(permissions.GetProperty("microphone").GetBoolean(), "Microphone permission is required for the signed verification bundle.");
        session.Hotkeys.Start();
        Require(MacNative.Permissions(false).GetProperty("keyboard").GetBoolean(), "The native fn shortcut did not start.");
        foreach (var page in new[] { "Home", "History", "Settings" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == page);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(150);
            using var bitmap = new RenderTargetBitmap(new PixelSize(500, 640));
            bitmap.Render(window); bitmap.Save(Path.Combine(Output, page.ToLowerInvariant() + ".png"), new PngBitmapEncoderOptions());
        }
        var choice = window.GetVisualDescendants().OfType<ComboBox>().Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Speech engine");
        foreach (var engine in SpeechProfiles.Engines)
        {
            choice.SelectedItem = engine;
            await Task.Delay(150);
            Require(session.Settings.SpeechEngine == engine, "The shared menu did not select " + engine);
            Require(session.Store.LoadSettings().SpeechEngine == engine, "The menu choice did not persist.");
        }
        notes.Add("Shared Home, History, Settings, and both menu choices rendered. Choices persisted.");
        var delivery = new MacDelivery();
        var command = Path.Combine(Output, "fixture-command");
        using var fixture = new ExternalFixture(command);
        await fixture.StartAsync();
        async Task Signal(int action) => await fixture.SignalAsync(action);
        async Task<string> Text(int index = 0)
        {
            await Signal(6);
            return File.ReadAllText(command + (index == 0 ? ".first" : ".second"));
        }
        await Signal(0);
        var receipt = await delivery.CaptureTarget().InsertAsync("Hello ☀ 😀.", "hello", CancellationToken.None);
        Require(receipt.Outcome == DeliveryOutcome.Verified && await Text() == "Hello ☀ 😀.", "Unicode delivery failed: " + receipt);
        Require(receipt.Correction is not null && await receipt.Correction.ReplaceAsync("hello", CancellationToken.None), "Raw restoration failed: " + receipt);
        Require(await Text() == "hello", "Raw restoration changed the text.");
        await Signal(0);
        receipt = await delivery.CaptureTarget().InsertAsync("Undo this.", "undo", CancellationToken.None);
        Require(receipt.Correction is not null && await receipt.Correction.ReplaceAsync("", CancellationToken.None) && await Text() == "", "Undo failed.");
        await Signal(5);
        Require(MacDelivery.SelectedText() == "old", "Selected-word capture failed.");
        receipt = await delivery.CaptureTarget().InsertAsync("new", "new", CancellationToken.None);
        Require(receipt.Outcome == DeliveryOutcome.Verified && await Text() == "Before new after", "Selection replacement failed.");
        await Signal(0);
        var stale = delivery.CaptureTarget();
        await Signal(2);
        Require((await stale.InsertAsync("wrong", "wrong", CancellationToken.None)).Outcome == DeliveryOutcome.FocusChanged && await Text(1) == "", "A stale target received text.");
        await Signal(0);
        receipt = await delivery.CaptureTarget().InsertAsync("Keep this.", "keep", CancellationToken.None);
        await Signal(3);
        Require(receipt.Correction is not null && !await receipt.Correction.ReplaceAsync("", CancellationToken.None), "Correction overwrote an edit.");
        await Signal(0);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            receipt = await delivery.CaptureTarget().InsertAsync("wrong", "wrong", cancelled.Token);
            Require(receipt.Outcome == DeliveryOutcome.Cancelled && await Text() == "", "Cancelled delivery posted text.");
        }
        // Dictate can also receive text in its own Settings fields.
        window.Activate(); await Task.Delay(200);
        var ownField = window.GetVisualDescendants().OfType<TextBox>().Single(field => field.PlaceholderText == "Heard as");
        ownField.Text = ""; ownField.BringIntoView(); ownField.Focus(); await Task.Delay(100);
        receipt = await delivery.CaptureTarget().InsertAsync("Own field.", "own", CancellationToken.None);
        Require(receipt.Outcome is DeliveryOutcome.Verified or DeliveryOutcome.Sent, "Own-field delivery failed: " + receipt);
        var ownText = "";
        for (var attempt = 0; attempt < 50; attempt++)
        {
            ownText = ownField.Text ?? "";
            if (ownText == "Own field.") break;
            await Task.Delay(20);
        }
        Require(ownText == "Own field.", "The app's own field did not receive text: " + ownText + "; " + receipt);
        if (receipt.Correction is not null) Require(await receipt.Correction.ReplaceAsync("", CancellationToken.None), "Verified own-field correction failed.");
        await Signal(0);
        var focused = delivery.CaptureTarget();
        var bar = new FlowBar(session);
        try
        {
            bar.Show();
            session.Controller.Start(session.Settings with { SmartCleanup = false });
            await Task.Delay(200); bar.Update();
            Require(bar.Opacity == 1, "The listening indicator did not appear.");
            Require((await focused.InsertAsync("Focus preserved.", "focus", CancellationToken.None)).Outcome == DeliveryOutcome.Verified, "The indicator moved focus from the external field.");
            session.Shortcuts.Cancel(); bar.Update();
            Require(bar.Opacity == 0, "The idle indicator did not disappear.");
            session.Message("Verification warning"); await Task.Delay(50);
            Require(!bar.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Verification warning"), "A warning entered the indicator.");
        }
        finally { session.Shortcuts.Cancel(); bar.Close(); }
        notes.Add("The indicator preserved focus, hid when idle, and excluded warnings. Own-field delivery used the same correction guards.");
        notes.Add("Native Unicode, raw restoration, undo, selection, focus change, edit guard, and cancellation passed.");
        var account = "shared-ui-check-" + Guid.NewGuid();
        try { await session.Secrets.WriteAsync(account, "test-only"); Require(await session.Secrets.ReadAsync(account) == "test-only", "Keychain roundtrip failed."); }
        finally { await session.Secrets.WriteAsync(account, null); }
        Require(await session.Secrets.ReadAsync(account) is null, "Keychain deletion failed.");
        for (var i = 0; i < 2; i++) { session.Audio.Start(); await Task.Delay(700); Require(session.Audio.Stop().Length > 0, "Microphone returned no audio."); }
        notes.Add("Keychain and microphone restart passed.");
        using var speech = new SpeechRecognizer(Models);
        foreach (var engine in SpeechProfiles.Engines)
        {
            var transcript = await speech.TranscribeAsync(WaveAudio.Read(Path.Combine(Fixtures, "jfk.wav")), engine, SpeechProfiles.WhisperModel, "en", CancellationToken.None);
            Require(transcript.Contains("country", StringComparison.OrdinalIgnoreCase), engine + " recognition failed: " + transcript);
            await Signal(0);
            receipt = await delivery.CaptureTarget().InsertAsync(transcript, transcript, CancellationToken.None);
            Require(receipt.Outcome == DeliveryOutcome.Verified && await Text() == transcript, engine + " text did not reach the native field.");
            notes.Add(engine + " English CPU recognition and native field delivery passed.");
        }
        await SpeechEvaluation.RunAsync(speech, Path.Combine(Fixtures, "samples.json"), Path.Combine(Output, "audio.json"));
        await SpeechEvaluation.CheckCancellationAsync(speech, WaveAudio.Read(Path.Combine(Fixtures, "jfk.wav")));
        notes.Add("Both English profiles completed six audio fixtures, five passes, cancellation, and recovery.");
        File.WriteAllLines(Path.Combine(Output, "checks.txt"), notes);
    }
    [DllImport("dictate-macos", EntryPoint = "dictate_fixture_run")] internal static extern void RunFixture([MarshalAs(UnmanagedType.LPUTF8Str)] string command);
    private sealed class ExternalFixture(string command) : IDisposable
    {
        private Process? process;
        public async Task StartAsync()
        {
            var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            foreach (var argument in new[] { "-n", "-W", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../..")), "--args", "--fixture", command }) start.ArgumentList.Add(argument);
            process = Process.Start(start)!;
            await WaitAsync(() => File.Exists(command + ".ready"));
        }
        public async Task SignalAsync(int action)
        {
            await File.WriteAllTextAsync(command + ".pending", action.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(command + ".pending", command);
            await WaitAsync(() => !File.Exists(command));
            if (File.Exists(command + ".error")) throw new InvalidOperationException(File.ReadAllText(command + ".error"));
        }
        private async Task WaitAsync(Func<bool> ready)
        {
            for (var i = 0; i < 250; i++) { if (process!.HasExited) throw new InvalidOperationException("The AppKit fixture exited."); if (ready()) return; await Task.Delay(20); }
            throw new TimeoutException("The AppKit fixture did not respond.");
        }
        public void Dispose()
        {
            if (process is null) return;
            if (!process.HasExited)
            {
                File.WriteAllText(command, "7");
                if (!process.WaitForExit(3000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            }
            process.Dispose();
        }
    }
}
