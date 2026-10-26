using System.Runtime.InteropServices;
using System.Text;
using Dictate.Core;

namespace Dictate.Desktop;

public sealed class WindowsDelivery : ITextDelivery
{
    public static string? SelectedText()
    {
        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(thread, ref info)) return null;
        var state = Read(info.Focus);
        return state is { } value && value.End > value.Start ? value.Text[value.Start..value.End] : null;
    }
    public ITextTarget CaptureTarget()
    {
        var window = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(window, out var pid);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        GetGUIThreadInfo(thread, ref info);
        return new Target(window, info.Focus, pid);
    }

    private sealed class Target(nint window, nint field, uint pid) : ITextTarget
    {
        public string? Application => "Windows process " + pid;
        private bool IsCurrent()
        {
            if (GetForegroundWindow() != window || GetWindowThreadProcessId(window, out var currentPid) == 0 || currentPid != pid) return false;
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            return GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref info) && info.Focus == field;
        }

        public async Task<DeliveryReceipt> InsertAsync(string text, string raw, CancellationToken cancellation)
        {
            if (!IsCurrent()) return new(DeliveryOutcome.FocusChanged, Error: "The focused field changed. Copy the text from History.");
            var before = Read(field);
            var inserted = before is null ? text : Format(text, before.Value.Text, before.Value.Start, before.Value.End);
            var posted = 0;
            foreach (var rune in inserted.EnumerateRunes())
            {
                if (cancellation.IsCancellationRequested) return new(DeliveryOutcome.Cancelled, posted);
                if (!IsCurrent()) return new(DeliveryOutcome.FocusChanged, posted, Error: "The focused field changed during insertion.");
                var events = rune.ToString().SelectMany(character => new[] { Input.Unicode(character, false), Input.Unicode(character, true) }).ToArray();
                if (SendInput((uint)events.Length, events, Marshal.SizeOf<Input>()) != events.Length)
                    return new(DeliveryOutcome.Failed, posted, Error: "Windows could not insert all text. Check the field before copying from History.");
                posted += rune.Utf16SequenceLength;
            }
            if (before is null) return new(DeliveryOutcome.Sent, posted);
            var expected = before.Value.Text[..before.Value.Start] + inserted + before.Value.Text[before.Value.End..];
            var caret = before.Value.Start + inserted.Length;
            for (var i = 0; i < 10; i++)
            {
                if (!IsCurrent()) return new(DeliveryOutcome.Sent, posted);
                var after = Read(field);
                if (after is { } value && value.Text == expected && value.Start == caret && value.End == caret)
                {
                    var correction = before.Value.Start == before.Value.End ? new Correction(this, field, expected, before.Value.Start, inserted.Length, caret) : null;
                    return new(DeliveryOutcome.Verified, posted, correction);
                }
                await Task.Delay(15, CancellationToken.None).ConfigureAwait(false);
            }
            return new(DeliveryOutcome.Sent, posted);
        }

        private sealed class Correction(Target target, nint field, string expected, int start, int length, int caret) : ITextCorrection
        {
            public Task<bool> ReplaceAsync(string text, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                var current = Read(field);
                if (!target.IsCurrent() || current is not { } value || value.Text != expected || value.Start != caret || value.End != caret) return Task.FromResult(false);
                var replacement = text.Length == 0 ? "" : Format(text, expected, start, start + length);
                if (SendMessageTimeout(field, 0x00B1, start, start + length, 2, 50, out _) == 0) return Task.FromResult(false);
                if (!target.IsCurrent() || cancellation.IsCancellationRequested) return Task.FromResult(false);
                var pointer = Marshal.StringToHGlobalUni(replacement);
                try { return Task.FromResult(SendMessageTimeout(field, 0x00C2, 1, pointer, 2, 50, out _) != 0); }
                finally { Marshal.FreeHGlobal(pointer); }
            }
        }
    }

    private static string Format(string text, string value, int start, int end)
    {
        if (start > 0 && !char.IsWhiteSpace(value[start - 1]) && text.Length > 0 && char.IsLetterOrDigit(text[0])) text = " " + text;
        if (end < value.Length && char.IsLetterOrDigit(value[end]) && text.Length > 0 && !char.IsWhiteSpace(text[^1])) text += " ";
        return text;
    }

    private static unsafe (string Text, int Start, int End)? Read(nint field)
    {
        if (field == 0) return null;
        var name = new StringBuilder(128);
        GetClassName(field, name, name.Capacity);
        if (!name.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase) || (GetWindowLong(field, -16) & 0x20) != 0) return null;
        if (SendMessageTimeout(field, 0x000E, 0, 0, 2, 50, out var length) == 0 || length > 1_000_000) return null;
        var buffer = new char[(int)length + 1];
        uint start = 0, end = 0;
        fixed (char* pointer = buffer)
        {
            if (SendMessageTimeout(field, 0x000D, buffer.Length, (nint)pointer, 2, 50, out var copied) == 0) return null;
            if (SendMessageTimeout(field, 0x00B0, (nint)(&start), (nint)(&end), 2, 50, out _) == 0 || end > copied || start > end) return null;
            return (new string(buffer, 0, (int)copied), (int)start, (int)end);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo { public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Input
    {
        public uint Type;
        public InputUnion Data;
        public static Input Unicode(char value, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Scan = value, Flags = (uint)(4 | (up ? 2 : 0)) } } };
    }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(nint window, StringBuilder text, int count);
    [DllImport("user32", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint window, uint message, nint wparam, nint lparam, uint flags, uint timeout, out nuint result);
    [DllImport("user32", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
