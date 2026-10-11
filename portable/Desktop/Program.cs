using Avalonia;

namespace Dictate.Desktop;

public static class Program
{
    internal static string? DataDirectory { get; private set; }
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--data-directory", var directory]) DataDirectory = Path.GetFullPath(directory);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
        .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] });
}
