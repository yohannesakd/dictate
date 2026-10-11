namespace Dictate.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string? directory = args switch
        {
            [] => null,
            ["--data-directory", var path] => Path.GetFullPath(path),
            _ => throw new ArgumentException("Use --data-directory followed by an absolute data path.")
        };
        DesktopRuntime.Run(async () =>
        {
            using var session = new DesktopSession(directory);
            using var host = new DesktopHost(session);
            await host.RunAsync();
        });
    }

}
