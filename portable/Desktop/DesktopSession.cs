using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Desktop;

public sealed class DesktopSession : IDisposable
{
    private readonly SynchronizationContext context;
    private readonly SemaphoreSlim settingsGate = new(1);
    public FileStore Store { get; }
    public DictationSettings Settings { get; private set; }
    public DesktopSecrets Secrets { get; } = new();
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };
    public ISpeechRecognizer Speech { get; }
    public DictationController Controller { get; }
    public ShortcutController Shortcuts { get; }
    public GlobalShortcuts Hotkeys { get; }
    public DesktopAudio Audio { get; } = new();
    public event Action<string>? Notice;
    public event Action? SettingsChanged;
    public string? Status { get; private set; }

    public DesktopSession(string? directory = null)
    {
        context = SynchronizationContext.Current ?? throw new InvalidOperationException("Start the native main loop before creating a session.");
        var importMac = directory is null && OperatingSystem.IsMacOS();
        var macData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Dictate");
        directory ??= OperatingSystem.IsMacOS() ? Path.Combine(macData, "Desktop") : OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dictate")
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "dictate");
        var speechEngine = OperatingSystem.IsMacOS() ? SpeechEngineKind.Phonon : SpeechEngineKind.Whisper;
        Store = new(directory, speechEngine);
        if (importMac) MacMigration.Import(Store, macData);
        Settings = Store.LoadSettings();
        Speech = OperatingSystem.IsMacOS() ? new PhononEngine() : new WhisperEngine(Path.Combine(directory, "models"));
        Controller = new(Audio, Speech, OperatingSystem.IsMacOS() ? new MacDelivery() : OperatingSystem.IsWindows() ? new WindowsDelivery() : new LinuxDelivery(),
            new SmartCleanup(new HttpCleanupClient(Http, Secrets)), Store);
        Controller.Notice += Message;
        Audio.Interrupted += message => Post(() => { Message(message); _ = Controller.FinishAsync(); });
        Shortcuts = new(Controller, () => Settings);
        Hotkeys = new(Shortcuts, Controller, context);
        Hotkeys.Notice += Message;
        if (OperatingSystem.IsMacOS()) Controller.PhaseChanged += phase => Post(() => MacNative.KeyboardActive(phase == DictationPhase.Idle ? 0 : 1));
    }

    public void Post(Action action) => context.Post(_ => action(), null);

    public async Task UpdateSettingsAsync(Func<DictationSettings, DictationSettings> change)
    {
        await settingsGate.WaitAsync();
        try
        {
            var next = SpeechProfiles.Normalize(change(Settings), Speech.Kind);
            await Store.SaveSettingsAsync(next);
            Settings = next;
            SettingsChanged?.Invoke();
        }
        finally { settingsGate.Release(); }
    }

    public void Message(string message) => Post(() => { Status = message; Notice?.Invoke(message); });
    public void ClearNotice() { Status = null; SettingsChanged?.Invoke(); }
    public void Dispose() { Hotkeys.Dispose(); Shortcuts.Dispose(); Controller.Dispose(); Speech.Dispose(); Http.Dispose(); settingsGate.Dispose(); }
}
