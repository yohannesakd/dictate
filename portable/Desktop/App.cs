using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Desktop;

public sealed class App : Application
{
    private DesktopSession? session;
    private FlowBar? bar;
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            session = new DesktopSession();
            desktop.MainWindow = new MainWindow(session);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            bar = new FlowBar(session);
            bar.Show();
            session.Controller.PhaseChanged += _ => Dispatcher.UIThread.Post(bar.Update);
            session.SettingsChanged += bar.Update;
            desktop.MainWindow.Closing += (_, args) => { if (args.CloseReason != WindowCloseReason.ApplicationShutdown) { args.Cancel = true; desktop.MainWindow.Hide(); } };
            var menu = new NativeMenu();
            var open = new NativeMenuItem("Open Dictate");
            open.Click += (_, _) => { desktop.MainWindow.Show(); desktop.MainWindow.Activate(); };
            var undo = new NativeMenuItem("Undo last dictation");
            undo.Click += async (_, _) => await session.Controller.CorrectLastAsync(false);
            var restore = new NativeMenuItem("Restore raw transcript");
            restore.Click += async (_, _) => await session.Controller.CorrectLastAsync(true);
            var quit = new NativeMenuItem("Quit Dictate");
            quit.Click += (_, _) => desktop.Shutdown();
            menu.Items.Add(open); menu.Items.Add(undo); menu.Items.Add(restore); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(quit);
            using var iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Dictate.Desktop/Assets/tray.png"));
            var tray = new TrayIcon { Icon = new WindowIcon(iconStream), ToolTipText = "Dictate", Menu = menu, IsVisible = true };
            tray.Clicked += (_, _) => { desktop.MainWindow.Show(); desktop.MainWindow.Activate(); };
            TrayIcon.SetIcons(this, [tray]);
            desktop.Exit += (_, _) => { tray.Dispose(); bar.Close(); session.Dispose(); };
            session.Hotkeys.CaptureWord += () => ((MainWindow)desktop.MainWindow).CaptureWord();
            session.Hotkeys.RepeatLast += async () =>
            {
                try
                {
                    var last = session.Store.History().FirstOrDefault();
                    if (last is not null) { var receipt = await session.Controller.RepeatAsync(last.Text, last.Raw); if (receipt.Error is not null) session.Message(receipt.Error); }
                }
                catch (Exception error) { session.Message(error.Message); }
            };
            session.Hotkeys.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class DesktopSession : IDisposable
{
    public FileStore Store { get; }
    public DictationSettings Settings { get; set; }
    public DesktopSecrets Secrets { get; } = new();
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };
    public WhisperEngine Speech { get; }
    public DictationController Controller { get; }
    public ShortcutController Shortcuts { get; }
    public GlobalShortcuts Hotkeys { get; }
    public DesktopAudio Audio { get; } = new();
    public event Action<string>? Notice;
    public event Action? SettingsChanged;
    public void RefreshSettings() => SettingsChanged?.Invoke();
    public string Status { get; private set; } = "Download a speech model in Settings to start.";

    public DesktopSession(string? directory = null)
    {
        directory ??= OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dictate")
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "dictate");
        Store = new FileStore(directory);
        Settings = Store.LoadSettings();
        Speech = new(Path.Combine(directory, "models"));
        Speech.Status += Message;
        var cleanup = new SmartCleanup(new HttpCleanupClient(Http, Secrets));
        Controller = new(Audio, Speech, OperatingSystem.IsWindows() ? new WindowsDelivery() : new LinuxDelivery(), cleanup, Store);
        Controller.Notice += Message;
        Audio.Interrupted += message => Dispatcher.UIThread.Post(() => { Message(message); _ = Controller.FinishAsync(); });
        Shortcuts = new(Controller, () => Settings);
        Hotkeys = new(Shortcuts, Controller);
        Hotkeys.Notice += Message;
    }

    public void Message(string message) { Status = message; Notice?.Invoke(message); }
    public void Dispose() { Hotkeys.Dispose(); Shortcuts.Dispose(); Controller.Dispose(); Speech.Dispose(); Http.Dispose(); }
}
