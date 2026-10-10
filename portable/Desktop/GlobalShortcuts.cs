using System.Runtime.InteropServices;
using Avalonia.Threading;
using Dictate.Core;

namespace Dictate.Desktop;

public sealed class GlobalShortcuts(ShortcutController shortcuts, DictationController controller) : IDisposable
{
    private readonly CancellationTokenSource stopped = new();
    private Thread? thread;
    private bool down;
    private bool chordHandled;
    private bool replayDown;
    private nint hook;
    private HookCallback? callback;
    public event Action<string>? Notice;
    public event Action? CaptureWord;
    public event Action? RepeatLast;

    public void Start()
    {
        thread = new Thread(() =>
        {
            try { if (OperatingSystem.IsWindows()) RunWindows(); else RunX11(); }
            catch (Exception error) { Notice?.Invoke(error.Message); }
        }) { IsBackground = true, Name = "Dictate shortcuts" };
        thread.Start();
    }

    private void Post(Action action) => Dispatcher.UIThread.Post(() =>
    {
        try { action(); }
        catch (Exception error) { Notice?.Invoke(error.Message); }
    });

    private void RunWindows()
    {
        callback = (code, message, data) =>
        {
            if (code < 0) return CallNextHookEx(hook, code, message, data);
            var key = Marshal.ReadInt32(data);
            var pressed = message == 0x100 || message == 0x104;
            var released = message == 0x101 || message == 0x105;
            if (key == 0x77)
            {
                if (pressed && !down) { down = true; chordHandled = false; Post(shortcuts.Down); }
                if (released && down) { down = false; Post(shortcuts.Up); }
                return 1;
            }
            if (pressed && key == 0x44 && down && !chordHandled) { chordHandled = true; Post(() => CaptureWord?.Invoke()); return 1; }
            if (pressed && !replayDown && key == 0x56 && (GetAsyncKeyState(0x11) & 0x8000) != 0 && (GetAsyncKeyState(0x10) & 0x8000) != 0) { replayDown = true; Post(() => RepeatLast?.Invoke()); return 1; }
            if (released && key == 0x56 && replayDown) { replayDown = false; return 1; }
            if (pressed && key == 0x20 && down && !chordHandled) { chordHandled = true; Post(shortcuts.ToggleHandsFree); return 1; }
            if (key == 0x1B && controller.Phase != DictationPhase.Idle) { if (pressed) Post(shortcuts.Cancel); return 1; }
            return CallNextHookEx(hook, code, message, data);
        };
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == 0) throw new InvalidOperationException("Windows could not enable the dictation shortcut.");
        try
        {
            while (!stopped.IsCancellationRequested)
            {
                while (PeekMessage(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
                Thread.Sleep(10);
            }
        }
        finally { UnhookWindowsHookEx(hook); }
    }

    private unsafe void RunX11()
    {
        if (LinuxDelivery.IsWayland) { Notice?.Invoke("Global shortcuts need an X11 session. Use the recording controls on Wayland."); return; }
        var display = XOpenDisplay(null);
        if (display == 0) throw new InvalidOperationException("An X11 desktop is required for global dictation shortcuts.");
        var root = XDefaultRootWindow(display);
        var f8 = XKeysymToKeycode(display, 0xFFC5);
        var conflict = false;
        nint previous = 0;
        XErrorCallback errors = (source, error) =>
        {
            if (source == display && Marshal.ReadByte(error, IntPtr.Size * 4) == 10) { conflict = true; return 0; }
            return previous == 0 ? 0 : Marshal.GetDelegateForFunctionPointer<XErrorCallback>(previous)(source, error);
        };
        previous = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(errors));
        try
        {
            XGrabKey(display, f8, 1 << 15, root, 0, 1, 1);
            var replay = XKeysymToKeycode(display, 0x76);
            foreach (var modifiers in new uint[] { 5, 7, 21, 23 }) XGrabKey(display, replay, modifiers, root, 0, 1, 1);
            XSync(display, 0);
        }
        finally { XSetErrorHandler(previous); GC.KeepAlive(errors); }
        if (conflict) { XUngrabKey(display, 0, 1 << 15, root); XCloseDisplay(display); throw new InvalidOperationException("A Dictate shortcut is already assigned to another app. Release F8 or Ctrl+Shift+V before using Dictate."); }
        var v = XKeysymToKeycode(display, 0x76);
        XFlush(display);
        var keyboard = new byte[32];
        var storage = stackalloc long[24];
        try
        {
            while (!stopped.IsCancellationRequested)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, (nint)storage);
                    var kind = *(int*)storage;
                    var key = Marshal.PtrToStructure<XKeyEvent>((nint)storage);
                    if (kind == 2 && !replayDown && key.Keycode == v && (key.State & 5) == 5) { replayDown = true; Post(() => RepeatLast?.Invoke()); continue; }
                    if (kind == 2 && down && !chordHandled && key.Keycode == XKeysymToKeycode(display, 0x20)) { chordHandled = true; Post(shortcuts.ToggleHandsFree); }
                    if (kind == 2 && down && !chordHandled && key.Keycode == XKeysymToKeycode(display, 0x64)) { chordHandled = true; Post(() => CaptureWord?.Invoke()); }
                    if (key.Keycode != f8) continue;
                    if (kind == 2 && !down) { down = true; chordHandled = false; Post(shortcuts.Down); }
                    if (kind == 3 && down)
                    {
                        // X11 autorepeat emits a release and press while the physical key remains down.
                        XQueryKeymap(display, keyboard);
                        if ((keyboard[f8 / 8] & (1 << (f8 % 8))) == 0) { down = false; Post(shortcuts.Up); }
                    }
                }
                XQueryKeymap(display, keyboard);
                if ((keyboard[v / 8] & (1 << (v % 8))) == 0) replayDown = false;
                var escape = XKeysymToKeycode(display, 0xFF1B);
                if (controller.Phase != DictationPhase.Idle && (keyboard[escape / 8] & (1 << (escape % 8))) != 0) Post(shortcuts.Cancel);
                Thread.Sleep(15);
            }
        }
        finally { XUngrabKey(display, f8, 1 << 15, root); foreach (var modifiers in new uint[] { 5, 7, 21, 23 }) XUngrabKey(display, v, modifiers, root); XCloseDisplay(display); }
    }

    public void Dispose() { stopped.Cancel(); thread?.Join(TimeSpan.FromSeconds(1)); stopped.Dispose(); }
    private delegate int XErrorCallback(nint display, nint error);
    [DllImport("libX11.so.6")] private static extern nint XSetErrorHandler(nint handler);
    [DllImport("libX11.so.6")] private static extern int XSync(nint display, int discard);
    private delegate nint HookCallback(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] private struct XKeyEvent { public int Type; public nuint Serial; public int SendEvent; public nint Display; public nuint Window, Root, Subwindow, Time; public int X, Y, RootX, RootY; public uint State, Keycode; public int SameScreen; }
    [DllImport("user32")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] private static extern nint SetWindowsHookEx(int id, HookCallback callback, nint module, uint thread);
    [DllImport("user32")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(nint display, nuint key);
    [DllImport("libX11.so.6")] private static extern int XGrabKey(nint display, int key, uint modifiers, nuint window, int ownerEvents, int pointerMode, int keyboardMode);
    [DllImport("libX11.so.6")] private static extern int XUngrabKey(nint display, int key, uint modifiers, nuint window);
    [DllImport("libX11.so.6")] private static extern int XPending(nint display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(nint display, nint value);
    [DllImport("libX11.so.6")] private static extern int XQueryKeymap(nint display, byte[] keys);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
}
