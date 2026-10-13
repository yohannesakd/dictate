using System.Text;
using System.Text.Json;
using Dictate.Core;

namespace Dictate.Desktop;

/// <summary>Private JSON-lines transport. Electron owns both redirected pipes.</summary>
internal sealed class DesktopHost : IDisposable
{
    private const int MaxRequest = 1024 * 1024;
    private readonly DesktopSession session;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<Task> pending = [];
    private readonly object output = new();
    private long sequence;
    private long lastLevel;
    private bool stopped;
    private bool preparing;
    private bool signingIn;
    private readonly Dictionary<string, string> credentialsErrors = [];
    private readonly Dictionary<string, bool> credentials = [];

    internal DesktopHost(DesktopSession session)
    {
        this.session = session;
        session.SettingsChanged += Publish;
        session.Notice += _ => Publish();
        session.Controller.PhaseChanged += _ => session.Post(Publish);
        session.Controller.Completed += _ => session.Post(Publish);
        session.Audio.Level += value =>
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref lastLevel) < 60) return;
            Interlocked.Exchange(ref lastLevel, now);
            Send(new { @event = "level", data = Math.Clamp(value, 0, 1) });
        };
        session.Hotkeys.CaptureWord += () =>
        {
            session.Shortcuts.Cancel();
            var selected = OperatingSystem.IsMacOS() ? MacDelivery.SelectedText() : OperatingSystem.IsWindows() ? WindowsDelivery.SelectedText() : LinuxDelivery.SelectedText();
            if (string.IsNullOrWhiteSpace(selected)) session.Message("Select the word to add, then use the dictionary shortcut.");
            else Send(new { @event = "capture-word", data = selected.Trim() });
        };
        session.Hotkeys.RepeatLast += async () =>
        {
            try
            {
                if (session.Store.History().FirstOrDefault() is { } last)
                {
                    var receipt = await session.Controller.RepeatAsync(last.Text, last.Raw);
                    if (receipt.Error is not null) session.Message(receipt.Error);
                }
            }
            catch (Exception error) { session.Message(error.Message); }
        };
    }

    internal async Task RunAsync()
    {
        session.Hotkeys.Start();
        Publish();
        using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true));
        var buffer = new char[4096];
        var line = new StringBuilder();
        try
        {
            while (await reader.ReadAsync(buffer) is var count && count > 0)
            {
                for (var index = 0; index < count; index++)
                {
                    var value = buffer[index];
                    if (value == '\n')
                    {
                        if (line.Length > 0)
                        {
                            using var document = JsonDocument.Parse(line.ToString());
                            var request = document.RootElement.Clone();
                            if (pending.Count >= 8) Send(new { id = request.GetProperty("id").GetInt64(), ok = false, error = "Wait for the current action to finish." });
                            else
                            {
                                var task = HandleAsync(request);
                                pending.Add(task);
                                _ = ForgetAsync(task);
                            }
                        }
                        line.Clear();
                    }
                    else if (value != '\r')
                    {
                        if (line.Length >= MaxRequest) throw new InvalidDataException("The app request is too large.");
                        line.Append(value);
                    }
                }
            }
        }
        finally
        {
            stopped = true;
            lifetime.Cancel();
            session.Shortcuts.Cancel();
            await Task.WhenAll(pending.ToArray());
        }
    }

    private async Task ForgetAsync(Task task) { await task; pending.Remove(task); }

    private async Task HandleAsync(JsonElement request)
    {
        var id = request.GetProperty("id").GetInt64();
        try
        {
            var command = request.GetProperty("command").GetString();
            var args = request.TryGetProperty("args", out var supplied) ? supplied : default;
            object? result = null;
            switch (command)
            {
                case "state": result = Snapshot(); break;
                case "prepare":
                    if (preparing || session.Controller.Phase != DictationPhase.Idle) throw new InvalidOperationException("Finish the current action before loading a model.");
                    preparing = true; Publish();
                    try { await session.Speech.PrepareAsync(session.Settings.SpeechModel, lifetime.Token, session.Settings.SpeechCompute); }
                    finally { preparing = false; Publish(); }
                    break;
                case "settings": await session.UpdateSettingsAsync(settings => AppCommands.Patch(settings, args)); break;
                case "add-word":
                    var heard = AppCommands.Word(args, "heard"); var written = AppCommands.Word(args, "written");
                    await session.UpdateSettingsAsync(settings => settings with { Dictionary = [..settings.Dictionary, new(heard, written)] }); break;
                case "remove-word":
                    var removed = new DictionaryEntry(AppCommands.Word(args, "heard"), AppCommands.Word(args, "written"));
                    await session.UpdateSettingsAsync(settings => settings with { Dictionary = settings.Dictionary.Where(entry => entry != removed).ToArray() }); break;
                case "permissions": if (OperatingSystem.IsMacOS()) MacNative.Permissions(true); Publish(); break;
                case "enable-shortcuts": session.Hotkeys.Start(); Publish(); break;
                case "start": if (preparing) throw new InvalidOperationException("Wait for the model to finish loading."); session.Shortcuts.ToggleHandsFree(); break;
                case "finish": await session.Controller.FinishAsync(); break;
                case "cancel": session.Shortcuts.Cancel(); break;
                case "undo": result = await session.Controller.CorrectLastAsync(false); break;
                case "restore": result = await session.Controller.CorrectLastAsync(true); break;
                case "dismiss": session.ClearNotice(); break;
                case "credentials": await ReadCredentialsAsync(); Publish(); break;
                case "save-key":
                    var account = AppCommands.KeyAccount(args.GetProperty("provider").GetString());
                    var key = args.GetProperty("key").GetString()?.Trim();
                    if (string.IsNullOrEmpty(key) || key.Length > 8192) throw new ArgumentException("Enter a valid API key.");
                    await session.Secrets.WriteAsync(account, key, lifetime.Token); await ReadCredentialsAsync(); Publish(); break;
                case "remove-key":
                    await session.Secrets.WriteAsync(AppCommands.KeyAccount(args.GetProperty("provider").GetString()), null, lifetime.Token);
                    await ReadCredentialsAsync(); Publish(); break;
                case "sign-in":
                    if (signingIn) throw new InvalidOperationException("Finish the current sign-in first.");
                    signingIn = true; Publish();
                    try { await DesktopSignIn.SignInAsync(new(session.Http, session.Secrets), lifetime.Token); await ReadCredentialsAsync(); }
                    finally { signingIn = false; Publish(); }
                    break;
                case "sign-out": await session.Secrets.WriteAsync("chatgpt-token", null, lifetime.Token); await ReadCredentialsAsync(); Publish(); break;
                default: throw new ArgumentException("The app command is not supported.");
            }
            Send(new { id, ok = true, result });
        }
        catch (Exception error) { Send(new { id, ok = false, error = error is OperationCanceledException ? "The action was cancelled." : error.Message }); }
    }

    private async Task ReadCredentialsAsync()
    {
        var provider = session.Settings.Provider;
        var name = AppCommands.ProviderName(provider);
        credentialsErrors.Remove(name);
        var account = provider switch { CleanupProvider.Groq => "groq-api-key", CleanupProvider.DeepSeek => "deepseek-api-key", _ => "chatgpt-token" };
        try { credentials[name] = await session.Secrets.ReadAsync(account, lifetime.Token) is not null; }
        catch (Exception error) when (error is not OperationCanceledException) { credentials.Remove(name); credentialsErrors[name] = error.Message; }
    }

    private object Snapshot()
    {
        var directory = Path.Combine(session.Store.DirectoryPath, "models");
        var whisperModel = Path.Combine(directory, "ggml-" + SpeechProfiles.WhisperModel + ".bin");
        return new
        {
            sequence = ++sequence, platform = OperatingSystem.IsMacOS() ? "mac" : OperatingSystem.IsWindows() ? "windows" : "linux",
            speech = new { hardware = session.Speech.ActiveHardware, compute = session.Speech.LoadedCompute, fallback = session.Speech.FallbackReason },
            settings = session.Settings, phase = session.Controller.Phase, notice = session.Status, preparing, signingIn,
            permissions = OperatingSystem.IsMacOS() ? MacNative.Permissions(false) : (object?)null,
            wayland = OperatingSystem.IsLinux() && LinuxDelivery.IsWayland, credentials,
            credentialsError = credentialsErrors.GetValueOrDefault(AppCommands.ProviderName(session.Settings.Provider)),
            models = new { ready = session.Speech.ModelReady, whisper = File.Exists(whisperModel) },
            directory = session.Store.DirectoryPath, usage = session.Store.Usage(),
            history = session.Store.History().Select(record => new { record.Id, record.Date, record.Text, record.Raw, record.Application, record.Note, record.SpeechEngine, record.Delivery, record.Metrics }).ToArray()
        };
    }

    private void Publish() { if (!stopped) Send(new { @event = "state", data = Snapshot() }); }
    private void Send(object value)
    {
        if (stopped) return;
        var line = JsonSerializer.Serialize(value, FileStore.Json);
        lock (output) Console.WriteLine(line);
    }

    public void Dispose() { stopped = true; lifetime.Cancel(); lifetime.Dispose(); }
}
