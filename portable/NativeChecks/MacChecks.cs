using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using Dictate.Core;
using Dictate.Checks;
using Dictate.Desktop;
using Dictate.Speech;

internal static class MacChecks
{
    internal static string Output = "";
    internal static string Fixtures = "";
    internal static string Models = "";
    internal static void Run(string[] args)
    {
        Output = args[0]; Fixtures = args[1]; Models = args[2];
        Directory.CreateDirectory(Output);
        DesktopRuntime.Run(async () =>
        {
            using var session = new DesktopSession(Path.Combine(Output, "data"));
            try { await CheckAsync(session); File.WriteAllText(Path.Combine(Output, "exit-code.txt"), "0"); }
            catch (Exception error) { File.WriteAllText(Path.Combine(Output, "error.txt"), error.ToString()); File.WriteAllText(Path.Combine(Output, "exit-code.txt"), "1"); }
        });
    }
    private static async Task CheckAsync(DesktopSession session)
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
        Require(session.Settings.SpeechEngine == SpeechEngineKind.Whisper && session.Store.LoadSettings().SpeechEngine == SpeechEngineKind.Whisper,
            "The English Whisper profile was not selected.");
        notes.Add("The native service uses the English Whisper profile.");
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
        await fixture.WaitAsync(() => delivery.CaptureTarget().Application == "dev.local.dictate.fixture");
        var target = delivery.CaptureTarget();
        Require(target.Application == "dev.local.dictate.fixture", "The external fixture has no focus. Keep the Mac unlocked and the check window active.");
        var receipt = await target.InsertAsync("Hello ☀ 😀.", "hello", CancellationToken.None);
        var observed = await Text();
        Require(receipt.Outcome == DeliveryOutcome.Verified && observed == "Hello ☀ 😀.", "Unicode delivery failed: " + receipt + "; target=" + File.ReadAllText(command + ".focus.json") + "; observed=" + observed);
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
        notes.Add("Native Unicode, raw restoration, undo, selection, focus change, edit guard, and cancellation passed.");
        var account = "shared-ui-check-" + Guid.NewGuid();
        try { await session.Secrets.WriteAsync(account, "test-only"); Require(await session.Secrets.ReadAsync(account) == "test-only", "Keychain roundtrip failed."); }
        finally { await session.Secrets.WriteAsync(account, null); }
        Require(await session.Secrets.ReadAsync(account) is null, "Keychain deletion failed.");
        for (var i = 0; i < 2; i++) { session.Audio.Start(); await Task.Delay(700); Require(session.Audio.Stop().Length > 0, "Microphone returned no audio."); }
        notes.Add("Keychain and microphone restart passed.");
        using var speech = new WhisperEngine(Models);
        var transcript = await speech.TranscribeAsync(WaveAudio.Read(Path.Combine(Fixtures, "jfk.wav")), SpeechProfiles.WhisperModel, "en", CancellationToken.None);
        Require(transcript.Contains("country", StringComparison.OrdinalIgnoreCase), "Whisper recognition failed: " + transcript);
        await Signal(0);
        receipt = await delivery.CaptureTarget().InsertAsync(transcript, transcript, CancellationToken.None);
        Require(receipt.Outcome == DeliveryOutcome.Verified && await Text() == transcript, "Whisper text did not reach the native field.");
        notes.Add($"Whisper English recognition and native field delivery passed. Using {speech.ActiveHardware}.");
        await SpeechEvaluation.RunAsync(speech, Path.Combine(Fixtures, "samples.json"), Path.Combine(Output, "audio.json"));
        await SpeechEvaluation.CheckCancellationAsync(speech, WaveAudio.Read(Path.Combine(Fixtures, "jfk.wav")));
        notes.Add("English Whisper completed six audio fixtures, five passes, cancellation, and recovery.");
        File.WriteAllLines(Path.Combine(Output, "checks.txt"), notes);
    }
    [DllImport("dictate-macos", EntryPoint = "dictate_fixture_run")] internal static extern void RunFixture([MarshalAs(UnmanagedType.LPUTF8Str)] string command);
    private sealed class ExternalFixture(string command) : IDisposable
    {
        private Process? process;
        public async Task StartAsync()
        {
            var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            var fixtureBundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Dictate.Fixture.app"));
            foreach (var argument in new[] { "-n", "-W", fixtureBundle, "--args", "--fixture", command }) start.ArgumentList.Add(argument);
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
        public async Task WaitAsync(Func<bool> ready)
        {
            for (var i = 0; i < 250; i++) { if (process!.HasExited) throw new InvalidOperationException("The AppKit fixture exited."); if (ready()) return; await Task.Delay(20); }
            throw new TimeoutException("The AppKit fixture did not become ready.");
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
