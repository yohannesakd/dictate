using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Dictate.Core;
using Dictate.Desktop;
using NAudio.Wave;

[SupportedOSPlatform("windows")]
internal static class WindowsChecks
{
    private sealed record Fields(long Window, long First, long Second);

    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-native-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var command = Path.Combine(directory, "command");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--fixture");
        start.ArgumentList.Add(command);
        using var fixture = Process.Start(start)!;
        try
        {
            await WaitAsync(() => File.Exists(command + ".ready"), "The native fixture did not start.");
            var fields = JsonSerializer.Deserialize<Fields>(File.ReadAllText(command + ".ready"))!;
            async Task Signal(string value)
            {
                File.WriteAllText(command + ".pending", value);
                File.Move(command + ".pending", command);
                await WaitAsync(() => !File.Exists(command), "The native fixture did not handle " + value);
                if (File.Exists(command + ".error")) throw new InvalidOperationException(File.ReadAllText(command + ".error"));
            }
            var delivery = new WindowsDelivery();
            await Signal("reset");
            var receipt = await delivery.CaptureTarget().InsertAsync("Hello ☀ 😀.", "hello", CancellationToken.None);
            Require(receipt.Outcome == DeliveryOutcome.Verified && receipt.Correction is not null, "Unicode insertion was not verified: " + receipt);
            Require(Read((nint)fields.First) == "Hello ☀ 😀.", "Unicode insertion changed the text.");
            Require(await receipt.Correction!.ReplaceAsync("hello", CancellationToken.None), "Raw restoration failed.");
            Require(Read((nint)fields.First) == "hello", "Raw restoration changed the text.");
            await Signal("reset");
            receipt = await delivery.CaptureTarget().InsertAsync("Undo this.", "undo", CancellationToken.None);
            Require(receipt.Correction is not null && await receipt.Correction.ReplaceAsync("", CancellationToken.None), "Undo failed.");
            Require(Read((nint)fields.First) == "", "Undo left inserted text.");
            await Signal("selection");
            Require(WindowsDelivery.SelectedText() == "old", "Selected-word capture failed.");
            receipt = await delivery.CaptureTarget().InsertAsync("new", "new", CancellationToken.None);
            Require(receipt.Outcome == DeliveryOutcome.Verified && receipt.Correction is null, "Selection replacement receipt was incorrect.");
            Require(Read((nint)fields.First) == "Before new after", "Selection replacement changed surrounding text.");
            await Signal("reset");
            var stale = delivery.CaptureTarget();
            await Signal("second");
            Require((await stale.InsertAsync("wrong", "wrong", CancellationToken.None)).Outcome == DeliveryOutcome.FocusChanged, "A stale target accepted text.");
            Require(Read((nint)fields.Second) == "", "Text entered the second field.");
            await Signal("reset");
            receipt = await delivery.CaptureTarget().InsertAsync("Keep this.", "keep", CancellationToken.None);
            Require(receipt.Correction is not null, "No correction receipt after insertion.");
            await Signal("edit");
            Require(!await receipt.Correction!.ReplaceAsync("", CancellationToken.None), "Correction removed edited text.");
            Require(Read((nint)fields.First) == "Keep this. changed", "Correction changed edited text.");
            await Signal("reset");
            receipt = await delivery.CaptureTarget().InsertAsync("Keep this.", "keep", CancellationToken.None);
            await Signal("caret");
            Require(receipt.Correction is not null && !await receipt.Correction.ReplaceAsync("", CancellationToken.None), "Correction ignored a caret change.");
            await Signal("reset");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Require((await delivery.CaptureTarget().InsertAsync("cancelled", "cancelled", cancellation.Token)).Outcome == DeliveryOutcome.Cancelled, "Cancelled insertion succeeded.");
            Require(Read((nint)fields.First) == "", "Cancelled insertion changed the field.");
            Console.WriteLine("Windows native Unicode insertion, selection replacement, selected-word capture, raw restoration, undo, focus changes, edit guards, caret guards, and cancellation passed.");
            Console.WriteLine("Windows microphone devices: " + WaveInEvent.DeviceCount);
            Require(WaveInEvent.DeviceCount > 0, "Windows has no microphone capture device.");
            using var audio = new DesktopAudio();
            string? interruption = null;
            audio.Interrupted += value => interruption = value;
            for (var i = 0; i < 2; i++)
            {
                audio.Start();
                await Task.Delay(700);
                var samples = audio.Stop();
                Require(interruption is null && samples.Length > 0, "Microphone capture failed: " + interruption);
                Require(samples.All(float.IsFinite), "The microphone returned invalid samples.");
                Console.WriteLine("Windows microphone capture " + (i + 1) + ": " + samples.Length + " samples at 16 kHz.");
            }
        }
        finally
        {
            if (!fixture.HasExited) { fixture.Kill(entireProcessTree: true); await fixture.WaitForExitAsync(); }
            Directory.Delete(directory, recursive: true);
        }
    }

    public static void RunFixture(string command)
    {
        var windowClass = new WindowClass
        {
            Procedure = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
            Instance = GetModuleHandle(null),
            Background = 6,
            Name = "DictateVerificationWindow"
        };
        Require(RegisterClass(ref windowClass) != 0, "Windows could not register the fixture window.");
        var window = CreateWindowEx(0, windowClass.Name, "Dictate native Windows checks", 0x10CF0000, 100, 100, 600, 240, 0, 0, windowClass.Instance, 0);
        var first = CreateWindowEx(0, "EDIT", "", 0x50810000, 20, 30, 500, 30, window, 0, 0, 0);
        var second = CreateWindowEx(0, "EDIT", "", 0x50810000, 20, 90, 500, 30, window, 0, 0, 0);
        Require(window != 0 && first != 0 && second != 0, "Windows could not create the native fields.");
        File.WriteAllText(command + ".ready", JsonSerializer.Serialize(new Fields(window, first, second)));
        try
        {
            while (true)
            {
                while (PeekMessage(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
                if (File.Exists(command))
                {
                    try
                    {
                        var value = File.ReadAllText(command);
                        Focus(window, value == "second" ? second : first);
                        if (value == "reset") { SetWindowText(first, ""); SetWindowText(second, ""); }
                        else if (value == "selection") { SetWindowText(first, "Before old after"); SendMessage(first, 0x00B1, 7, 10); }
                        else if (value == "edit") { SetWindowText(first, Read(first) + " changed"); SendMessage(first, 0x00B1, -1, -1); }
                        else if (value == "caret") SendMessage(first, 0x00B1, 0, 0);
                    }
                    catch (Exception error) { File.WriteAllText(command + ".error", error.ToString()); }
                    File.Delete(command);
                }
                Thread.Sleep(10);
            }
        }
        finally { DestroyWindow(window); }
    }

    private static void Focus(nint window, nint field)
    {
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try { SetForegroundWindow(window); SetFocus(field); }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
        Require(GetForegroundWindow() == window, "The fixture could not enter the foreground. Run it in the interactive desktop session. Expected=" + window + "; actual=" + GetForegroundWindow());
    }

    private static string Read(nint field)
    {
        var text = new StringBuilder(1024);
        SendMessageText(field, 0x000D, text.Capacity, text);
        return text.ToString();
    }
    private static async Task WaitAsync(Func<bool> predicate, string error)
    {
        for (var i = 0; i < 1500; i++) { if (predicate()) return; await Task.Delay(10); }
        throw new TimeoutException(error);
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass { public uint Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string Name; }
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassW")] private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")] private static extern nint CreateWindowEx(uint extended, string name, string text, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "SetWindowTextW")] private static extern bool SetWindowText(nint window, string text);
    [DllImport("user32", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wparam, nint lparam);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern nint SendMessageText(nint window, uint message, nint wparam, StringBuilder text);
    [DllImport("user32")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32")] private static extern nint SetFocus(nint window);
    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("user32")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
}
