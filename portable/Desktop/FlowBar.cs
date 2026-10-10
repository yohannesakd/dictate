using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dictate.Core;

namespace Dictate.Desktop;

public sealed class FlowBar : Window
{
    private readonly DesktopSession session;
    private readonly TextBlock label = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly StackPanel wave = new() { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border[] levels = Enumerable.Range(0, 11).Select(_ => new Border { Width = 3, Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center }).ToArray();
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    public FlowBar(DesktopSession session)
    {
        this.session = session;
        Width = 180; Height = 46; WindowDecorations = WindowDecorations.None; CanResize = false;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; Focusable = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; Background = Brushes.Transparent;
        foreach (var level in levels) wave.Children.Add(level);
        var stop = new Button { Content = "■", Focusable = false, Padding = new Thickness(8, 4), Background = Brushes.Transparent, Foreground = Brushes.White };
        stop.Click += async (_, _) => await session.Controller.FinishAsync();
        var cancel = new Button { Content = "×", Focusable = false, Padding = new Thickness(8, 4), Background = Brushes.Transparent, Foreground = Brushes.White };
        cancel.Click += (_, _) => session.Shortcuts.Cancel();
        actions.Children.Add(stop); actions.Children.Add(cancel);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 10 };
        row.Children.Add(wave); row.Children.Add(label); row.Children.Add(actions);
        Content = new Border { Background = new SolidColorBrush(Color.Parse("#E0000000")), CornerRadius = new CornerRadius(21), Child = row, Margin = new Thickness(4) };
        Opened += (_, _) => { PreserveFocus(); Update(); };
        session.Audio.Level += level => Dispatcher.UIThread.Post(() =>
        {
            if (session.Controller.Phase != DictationPhase.Listening) return;
            for (var i = 0; i < levels.Length; i++) levels[i].Height = 4 + Math.Clamp(level * 150, 0, 22) * (1 - Math.Abs(i - 5) / 7d);
        });
    }

    public void Update()
    {
        var phase = session.Controller.Phase;
        Opacity = phase == DictationPhase.Idle && !session.Settings.ShowIdleBar ? 0 : 1;
        IsHitTestVisible = phase == DictationPhase.Listening;
        ConfigureInput(phase == DictationPhase.Listening);
        wave.IsVisible = phase is DictationPhase.Idle or DictationPhase.Listening;
        label.IsVisible = phase is DictationPhase.Processing or DictationPhase.Delivering;
        label.Text = phase == DictationPhase.Processing ? "Processing…" : "Inserting…";
        actions.IsVisible = phase == DictationPhase.Listening;
        Width = phase == DictationPhase.Idle ? 66 : 180;
        if (phase == DictationPhase.Idle) foreach (var level in levels) level.Height = 4;
        var screen = Screens.Primary;
        if (screen is not null) Position = new PixelPoint(screen.WorkingArea.X + (screen.WorkingArea.Width - (int)(Width * screen.Scaling)) / 2, screen.WorkingArea.Bottom - (int)(60 * screen.Scaling));
    }

    private void ConfigureInput(bool interactive)
    {
        var native = TryGetPlatformHandle()?.Handle ?? 0;
        if (native == 0) return;
        if (OperatingSystem.IsWindows())
        {
            var style = GetWindowLongPtr(native, -20) | 0x08000000;
            SetWindowLongPtr(native, -20, interactive ? style & ~0x20 : style | 0x20);
        }
        else if (OperatingSystem.IsLinux())
        {
            var display = XOpenDisplay(null);
            if (display == 0) return;
            try
            {
                if (interactive) XShapeCombineMask(display, (nuint)native, 2, 0, 0, 0, 0);
                else XShapeCombineRectangles(display, (nuint)native, 2, 0, 0, 0, 0, 0, 0);
                XFlush(display);
            }
            finally { XCloseDisplay(display); }
        }
    }

    private void PreserveFocus()
    {
        var native = TryGetPlatformHandle()?.Handle ?? 0;
        if (native == 0) return;
        if (OperatingSystem.IsWindows()) SetWindowLongPtr(native, -20, GetWindowLongPtr(native, -20) | 0x08000000);
        else if (OperatingSystem.IsLinux())
        {
            var display = XOpenDisplay(null);
            if (display == 0) return;
            try { var hints = new WmHints { Flags = 1, Input = 0 }; XSetWMHints(display, (nuint)native, ref hints); XFlush(display); }
            finally { XCloseDisplay(display); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct WmHints { public nint Flags; public int Input, State; public nuint Pixmap, Window; public int X, Y; public nuint Mask, Group; }
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineMask(nint display, nuint window, int kind, int x, int y, nuint mask, int operation);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(nint display, nuint window, int kind, int x, int y, nint rectangles, int count, int operation, int ordering);
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern int XSetWMHints(nint display, nuint window, ref WmHints hints);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
}
