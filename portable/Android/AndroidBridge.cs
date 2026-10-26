using Android.Content;
using Android.Webkit;
using Java.Interop;
using System.Text.Json;
using Dictate.Core;

namespace Dictate.Android;

internal sealed class AndroidBridge : Java.Lang.Object
{
    private readonly AppView view;
    private readonly Session session;
    private readonly bool keyboard;
    private readonly Func<JsonElement, Task<object?>> windowAction;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken cancellation;
    private readonly Dictionary<string, bool> credentials = [];
    private readonly Dictionary<string, string> credentialErrors = [];
    private int pending;
    private long sequence;
    private bool stopped, signingIn;
    internal string? InitialSection { get; set; }
    internal string? InitialWord { get; set; }

    internal AndroidBridge(AppView view, Session session, bool keyboard, Func<JsonElement, Task<object?>> windowAction)
    {
        this.view = view; this.session = session; this.keyboard = keyboard; this.windowAction = windowAction;
        cancellation = lifetime.Token;
        session.Changed += Publish;
    }

    [JavascriptInterface, Export("post")]
    public void Post(string message)
    {
        if (message.Length > 1024 * 1024) return;
        MainThread.Post(async () =>
        {
            if (stopped) return;
            long id = 0;
            try
            {
                using var document = JsonDocument.Parse(message);
                var request = document.RootElement;
                id = request.GetProperty("id").GetInt64();
                if (pending >= 8) { Send(new { id, ok = false, error = "Wait for the current action to finish." }); return; }
                pending++;
                try
                {
                    object? result = request.GetProperty("type").GetString() switch
                    {
                        "state" => new { state = Snapshot(), section = InitialSection, word = InitialWord },
                        "command" => await CommandAsync(request.GetProperty("payload")),
                        "window" => await windowAction(request.GetProperty("payload")),
                        _ => throw new ArgumentException("The app request is not supported.")
                    };
                    Send(new { id, ok = true, result });
                }
                finally { pending--; }
            }
            catch (Exception error)
            {
                var text = error is OperationCanceledException ? "The action was cancelled." : error.Message;
                session.Message(text);
                Send(new { id, ok = false, error = text });
            }
        });
    }

    private async Task<object?> CommandAsync(JsonElement request)
    {
        var args = request.GetProperty("args");
        switch (request.GetProperty("command").GetString())
        {
            case "prepare": await session.PrepareAsync(cancellation); break;
            case "settings":
                if (args.TryGetProperty("open_at_login", out _)) throw new ArgumentException("Android keeps the voice keyboard available without opening the app at login.");
                await session.UpdateSettingsAsync(settings => AppCommands.Patch(settings, args)); break;
            case "add-word":
                var heard = AppCommands.Word(args, "heard"); var written = AppCommands.Word(args, "written");
                await session.UpdateSettingsAsync(settings => settings with { Dictionary = [..settings.Dictionary, new(heard, written)] }); break;
            case "remove-word":
                var removed = new DictionaryEntry(AppCommands.Word(args, "heard"), AppCommands.Word(args, "written"));
                await session.UpdateSettingsAsync(settings => settings with { Dictionary = settings.Dictionary.Where(entry => entry != removed).ToArray() }); break;
            case "start":
                if (session.Preparing) throw new InvalidOperationException("Wait for the model to finish loading.");
                if (!keyboard || !session.Delivery.HasTarget) throw new InvalidOperationException("Choose Dictate in a text field to start dictation.");
                if (!MicrophoneAllowed()) { await windowAction(JsonSerializer.SerializeToElement("permissions")); break; }
                session.Controller.Start(session.Settings); break;
            case "finish": await session.Controller.FinishAsync(); break;
            case "cancel": session.Controller.Cancel(); break;
            case "undo":
            case "restore":
                var restored = await session.Controller.CorrectLastAsync(request.GetProperty("command").GetString() == "restore");
                if (!restored) session.Message("The field cannot be corrected safely. Check the text or copy it from History.");
                return restored;
            case "repeat":
                if (session.Store.History().FirstOrDefault() is { } last)
                {
                    var receipt = await session.Controller.RepeatAsync(last.Text, last.Raw);
                    if (receipt.Error is not null) session.Message(receipt.Error);
                }
                break;
            case "dismiss": session.ClearNotice(); break;
            case "credentials": await ReadCredentialsAsync(); Publish(); break;
            case "save-key":
                var key = args.GetProperty("key").GetString()?.Trim();
                if (string.IsNullOrEmpty(key) || key.Length > 8192) throw new ArgumentException("Enter a valid API key.");
                await session.Secrets.WriteAsync(AppCommands.KeyAccount(args.GetProperty("provider").GetString()), key, cancellation);
                await ReadCredentialsAsync(); Publish(); break;
            case "remove-key":
                await session.Secrets.WriteAsync(AppCommands.KeyAccount(args.GetProperty("provider").GetString()), null, cancellation);
                await ReadCredentialsAsync(); Publish(); break;
            case "sign-in":
                if (keyboard || signingIn) throw new InvalidOperationException("Open Dictate to sign in.");
                signingIn = true; Publish();
                try { await SignInAsync(); await ReadCredentialsAsync(); }
                finally { signingIn = false; Publish(); }
                break;
            case "sign-out": await session.Secrets.WriteAsync("chatgpt-token", null, cancellation); await ReadCredentialsAsync(); Publish(); break;
            default: throw new ArgumentException("The app command is not supported.");
        }
        return null;
    }

    private async Task ReadCredentialsAsync()
    {
        var provider = session.Settings.Provider;
        var name = AppCommands.ProviderName(provider);
        credentialErrors.Remove(name);
        var account = provider switch { CleanupProvider.Groq => "groq-api-key", CleanupProvider.DeepSeek => "deepseek-api-key", _ => "chatgpt-token" };
        try { credentials[name] = await session.Secrets.ReadAsync(account, cancellation) is not null; }
        catch (Exception error) when (error is not OperationCanceledException) { credentials.Remove(name); credentialErrors[name] = error.Message; }
    }

    private bool MicrophoneAllowed() => view.Context!.CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) == global::Android.Content.PM.Permission.Granted;
    private object Snapshot()
    {
        var directory = Path.Combine(session.Store.DirectoryPath, "models");
        var keyboards = (global::Android.Views.InputMethods.InputMethodManager)view.Context!.GetSystemService(Context.InputMethodService)!;
        var enabled = keyboards.EnabledInputMethodList.Any(method => method.PackageName == view.Context.PackageName);
        return new
        {
            sequence = ++sequence, platform = "android", keyboard, canRecord = session.Delivery.HasTarget,
            speech = new { hardware = session.Speech.ActiveHardware, compute = session.Speech.LoadedCompute, fallback = session.Speech.FallbackReason },
            settings = session.Settings, phase = session.Controller.Phase, notice = keyboard ? null : session.Status,
            preparing = session.Preparing, signingIn,
            permissions = new { microphone = MicrophoneAllowed(), keyboard = enabled },
            wayland = false, credentials, credentialsError = credentialErrors.GetValueOrDefault(AppCommands.ProviderName(session.Settings.Provider)),
            models = new { whisper = File.Exists(Path.Combine(directory, "ggml-" + SpeechProfiles.WhisperModel + ".bin")) },
            directory = session.Store.DirectoryPath, usage = session.Store.Usage(),
            history = session.Store.History().Select(record => new { record.Id, record.Date, record.Text, record.Raw, record.Application, record.Note, record.SpeechEngine, record.Delivery, record.Metrics }).ToArray()
        };
    }

    internal void Publish() { if (!stopped) Send(new { type = "state", state = Snapshot() }); }
    internal void CaptureWord(string word) => Send(new { type = "capture-word", word });
    internal void Navigate(string section) => Send(new { type = "navigate", page = "settings", section });
    private void Send(object message)
    {
        var json = JsonSerializer.Serialize(message, FileStore.Json);
        MainThread.Post(() => { if (!stopped) view.EvaluateJavascript("window.dictateReceive?.(" + json + ")", null); });
    }

    private async Task SignInAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 1455);
        listener.Start();
        try
        {
            var signIn = ChatGptAuth.Begin();
            view.Context!.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(signIn.Url)).AddFlags(ActivityFlags.NewTask));
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(deadline.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                var line = await reader.ReadLineAsync(deadline.Token);
                var path = line?.Split(' ').ElementAtOrDefault(1);
                if (path is null || !path.StartsWith("/auth/callback?", StringComparison.Ordinal)) continue;
                await new ChatGptAuth(session.Http, session.Secrets).CompleteAsync(signIn, "http://localhost:1455" + path, deadline.Token);
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 24\r\nConnection: close\r\n\r\nDictate is now signed in."), deadline.Token);
                return;
            }
        }
        finally { listener.Stop(); }
    }

    internal void Close()
    {
        stopped = true; session.Changed -= Publish;
        lifetime.Cancel(); lifetime.Dispose();
    }
}
