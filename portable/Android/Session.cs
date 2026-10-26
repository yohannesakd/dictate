using Android.Content;
using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Android;

public sealed class Session
{
    private static Session? instance;
    private readonly SemaphoreSlim settingsGate = new(1);
    public static Session Get(Context context) => instance ??= new(context.ApplicationContext!);
    public FileStore Store { get; }
    public DictationSettings Settings { get; private set; }
    public AndroidSecrets Secrets { get; }
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };
    public AndroidDelivery Delivery { get; } = new();
    public AndroidAudio Audio { get; } = new();
    public WhisperEngine Speech { get; }
    public DictationController Controller { get; }
    public event Action? Changed;
    public string? Status { get; private set; }
    public bool Preparing { get; private set; }

    private Session(Context context)
    {
        Store = new(context.FilesDir!.AbsolutePath);
        Settings = Store.LoadSettings();
        Secrets = new(context);
        Speech = new(Path.Combine(Store.DirectoryPath, "models"));
        Speech.Status += _ => MainThread.Post(() => Changed?.Invoke());
        Controller = new(Audio, Speech, Delivery, new(new HttpCleanupClient(Http, Secrets)), Store);
        Controller.Notice += Message;
        Controller.PhaseChanged += _ => MainThread.Post(() => Changed?.Invoke());
        Controller.Completed += _ => MainThread.Post(() => Changed?.Invoke());
        Audio.Interrupted += message => MainThread.Post(() => { Message(message); _ = Controller.FinishAsync(); });
    }
    public void Message(string message) => MainThread.Post(() => { Status = message; Changed?.Invoke(); });
    public void ClearNotice() { Status = null; Changed?.Invoke(); }
    public void Refresh() => Changed?.Invoke();

    public async Task UpdateSettingsAsync(Func<DictationSettings, DictationSettings> change)
    {
        await settingsGate.WaitAsync();
        try
        {
            var next = SpeechProfiles.Normalize(change(Settings));
            await Store.SaveSettingsAsync(next);
            Settings = next;
            MainThread.Post(() => Changed?.Invoke());
        }
        finally { settingsGate.Release(); }
    }

    public async Task PrepareAsync(CancellationToken cancellation)
    {
        if (Preparing || Controller.Phase != DictationPhase.Idle)
            throw new InvalidOperationException("Finish the current action before loading a model.");
        Preparing = true; Changed?.Invoke();
        try { await Speech.PrepareAsync(Settings.SpeechModel, cancellation, Settings.SpeechCompute); }
        finally { Preparing = false; MainThread.Post(() => Changed?.Invoke()); }
    }
}
