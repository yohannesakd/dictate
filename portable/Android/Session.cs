using Android.Content;
using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Android;

public sealed class Session
{
    private static Session? instance;
    public static Session Get(Context context) => instance ??= new(context.ApplicationContext!);
    public FileStore Store { get; }
    public DictationSettings Settings { get; set; }
    public AndroidSecrets Secrets { get; }
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };
    public AndroidDelivery Delivery { get; } = new();
    public AndroidAudio Audio { get; } = new();
    public SpeechRecognizer Speech { get; }
    public DictationController Controller { get; }
    public event Action<string>? Notice;
    public string Status { get; private set; } = "Enable the Dictate voice keyboard to dictate in other apps.";

    private Session(Context context)
    {
        Store = new(context.FilesDir!.AbsolutePath);
        Settings = Store.LoadSettings();
        Secrets = new(context);
        Speech = new(Path.Combine(Store.DirectoryPath, "models"), new AndroidWhistleProcess(),
            Path.Combine(context.ApplicationInfo!.NativeLibraryDir!, "libdictate_needle.so"));
        Speech.Status += Message;
        Controller = new(Audio, Speech, Delivery, new(new HttpCleanupClient(Http, Secrets)), Store);
        Controller.Notice += Message;
        Audio.Interrupted += message => MainThread.Post(() => { Message(message); _ = Controller.FinishAsync(); });
    }
    public void Message(string message) { Status = message; Notice?.Invoke(message); }
}
