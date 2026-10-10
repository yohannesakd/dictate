using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Dictate.Desktop;

internal sealed class AtSpiField : SafeHandleZeroOrMinusOneIsInvalid
{
    private static readonly object Sync = new();
    private const string Library = "libatspi.so.0";
    private AtSpiField(nint accessible) : base(true) { SetHandle(accessible); }
    protected override bool ReleaseHandle() { g_object_unref(handle); return true; }

    public static AtSpiField? Focused()
    {
        lock (Sync)
        {
            try
            {
                if (atspi_init() is not (0 or 1)) return null;
                atspi_set_timeout(50, 50);
                var desktop = atspi_get_desktop(0);
                if (desktop == 0) return null;
                try
                {
                    var started = Stopwatch.GetTimestamp();
                    var inspected = 0;
                    nint Search(nint item, int depth)
                    {
                        if (++inspected > 5000 || depth > 64 || Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(300)) return 0;
                        if (depth > 1) atspi_accessible_clear_cache(item);
                        var states = atspi_accessible_get_state_set(item);
                        if (states != 0)
                        {
                            var focused = atspi_state_set_contains(states, 12) != 0;
                            g_object_unref(states);
                            if (focused) { g_object_ref(item); return item; }
                        }
                        var count = atspi_accessible_get_child_count(item, 0);
                        for (var i = 0; i < count; i++)
                        {
                            var child = atspi_accessible_get_child_at_index(item, i, 0);
                            if (child == 0) continue;
                            var found = Search(child, depth + 1);
                            g_object_unref(child);
                            if (found != 0) return found;
                            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(300)) break;
                        }
                        return 0;
                    }
                    var result = Search(desktop, 0);
                    return result == 0 ? null : new AtSpiField(result);
                }
                finally { g_object_unref(desktop); }
            }
            catch (DllNotFoundException) { return null; }
        }
    }

    public bool IsFocused
    {
        get
        {
            lock (Sync)
            {
                atspi_accessible_clear_cache(handle);
                var states = atspi_accessible_get_state_set(handle);
                if (states == 0) return false;
                try { return atspi_state_set_contains(states, 12) != 0 && atspi_state_set_contains(states, 6) == 0; }
                finally { g_object_unref(states); }
            }
        }
    }

    public (string Text, int Start, int End)? Read()
    {
        lock (Sync)
        {
            if (atspi_accessible_get_role(handle, 0) == 40) return null;
            var text = atspi_accessible_get_text_iface(handle);
            if (text == 0) return null;
            try
            {
                var count = atspi_text_get_character_count(text, 0);
                if (count < 0 || count > 1_000_000) return null;
                var value = atspi_text_get_text(text, 0, count, 0);
                if (value == 0) return null;
                string content;
                try { content = Marshal.PtrToStringUTF8(value)!; } finally { g_free(value); }
                var start = atspi_text_get_caret_offset(text, 0);
                var end = start;
                if (atspi_text_get_n_selections(text, 0) > 0)
                {
                    var selection = atspi_text_get_selection(text, 0, 0);
                    if (selection != 0) { start = Marshal.ReadInt32(selection); end = Marshal.ReadInt32(selection, 4); g_free(selection); }
                }
                if (start < 0 || end < start || end > count || content.Length > 1_000_000) return null;
                return (content, Utf16Offset(content, start), Utf16Offset(content, end));
            }
            finally { g_object_unref(text); }
        }
    }

    public bool ReplaceRange(string expected, int start, int end, string inserted)
    {
        lock (Sync)
        {
            var state = Read();
            if (!IsFocused || state is not { } current || current.Text != expected || current.Start != start || current.End != end) return false;
            var editable = atspi_accessible_get_editable_text_iface(handle);
            var text = atspi_accessible_get_text_iface(handle);
            if (editable == 0 || text == 0) { if (editable != 0) g_object_unref(editable); if (text != 0) g_object_unref(text); return false; }
            try
            {
                var first = expected[..start].EnumerateRunes().Count();
                var last = expected[..end].EnumerateRunes().Count();
                if (last > first && atspi_editable_text_delete_text(editable, first, last, 0) == 0) return false;
                if (inserted.Length > 0 && atspi_editable_text_insert_text(editable, first, inserted, Encoding.UTF8.GetByteCount(inserted), 0) == 0) return false;
                return atspi_text_set_caret_offset(text, first + inserted.EnumerateRunes().Count(), 0) != 0;
            }
            finally { g_object_unref(editable); g_object_unref(text); }
        }
    }

    public bool Select(int start, int end)
    {
        lock (Sync)
        {
            var text = atspi_accessible_get_text_iface(handle);
            if (text == 0) return false;
            try { return (atspi_text_get_n_selections(text, 0) > 0 ? atspi_text_set_selection(text, 0, start, end, 0) : atspi_text_add_selection(text, start, end, 0)) != 0; }
            finally { g_object_unref(text); }
        }
    }

    private static int Utf16Offset(string value, int characters) => value.EnumerateRunes().Take(characters).Sum(rune => rune.Utf16SequenceLength);
    [DllImport(Library)] private static extern void atspi_accessible_clear_cache(nint value);
    [DllImport(Library)] private static extern int atspi_accessible_get_role(nint value, nint error);
    [DllImport(Library)] private static extern int atspi_init();
    [DllImport(Library)] private static extern void atspi_set_timeout(int timeout, int startup);
    [DllImport(Library)] private static extern nint atspi_get_desktop(int index);
    [DllImport(Library)] private static extern nint atspi_accessible_get_state_set(nint value);
    [DllImport(Library)] private static extern int atspi_state_set_contains(nint value, int state);
    [DllImport(Library)] private static extern int atspi_accessible_get_child_count(nint value, nint error);
    [DllImport(Library)] private static extern nint atspi_accessible_get_child_at_index(nint value, int index, nint error);
    [DllImport(Library)] private static extern nint atspi_accessible_get_text_iface(nint value);
    [DllImport(Library)] private static extern nint atspi_accessible_get_editable_text_iface(nint value);
    [DllImport(Library)] private static extern int atspi_text_get_character_count(nint value, nint error);
    [DllImport(Library)] private static extern nint atspi_text_get_text(nint value, int start, int end, nint error);
    [DllImport(Library)] private static extern int atspi_text_get_caret_offset(nint value, nint error);
    [DllImport(Library)] private static extern int atspi_text_get_n_selections(nint value, nint error);
    [DllImport(Library)] private static extern nint atspi_text_get_selection(nint value, int index, nint error);
    [DllImport(Library)] private static extern int atspi_text_set_caret_offset(nint value, int caret, nint error);
    [DllImport(Library)] private static extern int atspi_editable_text_delete_text(nint value, int start, int end, nint error);
    [DllImport(Library)] private static extern int atspi_editable_text_insert_text(nint value, int start, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int length, nint error);
    [DllImport(Library)] private static extern int atspi_text_add_selection(nint value, int start, int end, nint error);
    [DllImport(Library)] private static extern int atspi_text_set_selection(nint value, int selection, int start, int end, nint error);
    [DllImport("libgobject-2.0.so.0")] private static extern nint g_object_ref(nint value);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(nint value);
    [DllImport("libglib-2.0.so.0")] private static extern void g_free(nint value);
}
