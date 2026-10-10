using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Android;

[Activity(Name = "dev.local.dictate.portable.MainActivity", Label = "Dictate", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private Session session = null!;
    private LinearLayout body = null!;
    private TextView status = null!;
    private string screen = "Home";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        session = Session.Get(this);
        session.Notice += Message;
        session.Controller.Completed += Completed;
        if (Intent?.HasExtra("dictionary_word") == true) screen = "Settings";
        Render();
    }

    private void Render()
    {
        var root = new InsetLayout(this, includeStatusBar: true);
        var title = Text("Dictate"); title.TextSize = 24; root.AddView(title);
        status = Text(session.Status); root.AddView(status);
        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var tab in new[] { "Home", "History", "Settings" }) tabs.AddView(Button(tab, () => { screen = tab; Render(); return Task.CompletedTask; }));
        root.AddView(tabs);
        var scroll = new ScrollView(this);
        body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(body); root.AddView(scroll); SetContentView(root);
        if (screen == "Settings") Settings();
        else if (screen == "History") History();
        else Home();
    }

    private void Home()
    {
        body.AddView(Text("Use the Dictate voice keyboard to speak into text fields in other apps."));
        body.AddView(Button("Allow microphone", () => { RequestPermissions([global::Android.Manifest.Permission.RecordAudio], 1); return Task.CompletedTask; }));
        body.AddView(Button("Enable voice keyboard", () => { StartActivity(new Intent(global::Android.Provider.Settings.ActionInputMethodSettings)); return Task.CompletedTask; }));
        body.AddView(Button("Choose keyboard", () => { ((InputMethodManager)GetSystemService(Context.InputMethodService)!).ShowInputMethodPicker(); return Task.CompletedTask; }));
        body.AddView(Button("Download speech model", async () => await session.Speech.PrepareAsync(session.Settings.SpeechModel)));
        body.AddView(Text("Try dictation below. Tap the field, choose Dictate, and tap Start dictation."));
        var sample = new EditText(this) { Hint = "Your dictated text appears here", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine };
        body.AddView(sample);
        var records = session.Store.History().ToArray();
        var usage = session.Store.Usage();
        body.AddView(Text($"{usage.Week[^1].Words} words today. {usage.Week.Sum(day => day.Words)} words this week."));
        if (usage.WordsPerMinute is { } speed) body.AddView(Text($"{speed} words per minute."));
        if (usage.MedianLatency is { } latency) body.AddView(Text($"{latency} ms from release to verified text."));
        foreach (var record in records.Take(3)) Row(record);
    }

    private void History()
    {
        foreach (var record in session.Store.History()) Row(record);
        if (!session.Store.History().Any()) body.AddView(Text("Your dictations appear here after you speak."));
    }

    private void Row(TranscriptRecord record)
    {
        body.AddView(Text(record.Date.ToLocalTime().ToString("g") + "  " + record.Delivery));
        body.AddView(Text(record.Text));
        if (record.Note is not null) body.AddView(Text(record.Note));
        body.AddView(Button("Copy", () => { Copy(record.Text); return Task.CompletedTask; }));
        body.AddView(Button("Copy original", () => { Copy(record.Raw); return Task.CompletedTask; }));
    }

    private void Settings()
    {
        body.AddView(Text("Speech model"));
        var models = new Spinner(this);
        models.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, WhisperEngine.Models);
        models.SetSelection(Array.IndexOf(WhisperEngine.Models, session.Settings.SpeechModel));
        models.ItemSelected += async (_, args) => await Save(session.Settings with { SpeechModel = WhisperEngine.Models[args.Position] });
        body.AddView(models);
        body.AddView(Text("Tiny uses less memory. Base balances speed and accuracy. Small needs more memory and time."));
        body.AddView(Button("Download and load model", async () => await session.Speech.PrepareAsync(session.Settings.SpeechModel)));
        var language = new EditText(this) { Text = session.Settings.Language, Hint = "Language: auto, en, de, ..." };
        body.AddView(language);
        body.AddView(Button("Save language", async () => await Save(session.Settings with { Language = language.Text?.Trim() ?? "auto" })));
        body.AddView(Text("Cleanup"));
        body.AddView(Check("Remove filler sounds", session.Settings.Rules.Fillers, value => Save(session.Settings with { Rules = session.Settings.Rules with { Fillers = value } })));
        body.AddView(Check("Remove stutters", session.Settings.Rules.Repeats, value => Save(session.Settings with { Rules = session.Settings.Rules with { Repeats = value } })));
        body.AddView(Check("Apply scratch that", session.Settings.Rules.ScratchThat, value => Save(session.Settings with { Rules = session.Settings.Rules with { ScratchThat = value } })));
        body.AddView(Check("Smart cleanup", session.Settings.SmartCleanup, value => Save(session.Settings with { SmartCleanup = value })));
        body.AddView(Text("Smart cleanup sends the transcript to the selected provider. Speech recognition stays on this device."));
        var providers = new Spinner(this);
        providers.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, Enum.GetNames<CleanupProvider>());
        providers.SetSelection((int)session.Settings.Provider);
        providers.ItemSelected += async (_, args) => await Save(session.Settings with { Provider = (CleanupProvider)args.Position });
        body.AddView(providers);
        var key = new EditText(this) { Hint = "API key for Groq or DeepSeek", InputType = InputTypes.ClassText | InputTypes.TextVariationPassword };
        body.AddView(key);
        body.AddView(Button("Save API key", async () =>
        {
            if (session.Settings.Provider == CleanupProvider.ChatGPT) { session.Message("Use Sign in with ChatGPT."); return; }
            await session.Secrets.WriteAsync(session.Settings.Provider.ToString().ToLowerInvariant() + "-api-key", key.Text?.Trim());
            key.Text = ""; session.Message("API key saved with Android Keystore encryption.");
        }));
        body.AddView(Button("Remove API key", async () => await session.Secrets.WriteAsync(session.Settings.Provider.ToString().ToLowerInvariant() + "-api-key", null)));
        body.AddView(Button("Sign in with ChatGPT", SignInAsync));
        body.AddView(Button("Sign out of ChatGPT", async () => await session.Secrets.WriteAsync("chatgpt-token", null)));
        body.AddView(Text("Personal dictionary"));
        foreach (var entry in session.Settings.Dictionary)
        {
            body.AddView(Text(entry.Heard + " → " + entry.Written));
            body.AddView(Button("Remove", async () => { await Save(session.Settings with { Dictionary = session.Settings.Dictionary.Where(item => item != entry).ToArray() }); Render(); }));
        }
        var heard = new EditText(this) { Hint = "Heard as" };
        var written = new EditText(this) { Hint = "Write as", Text = Intent?.GetStringExtra("dictionary_word") };
        body.AddView(heard); body.AddView(written);
        body.AddView(Button("Add word", async () =>
        {
            if (string.IsNullOrWhiteSpace(heard.Text) || string.IsNullOrWhiteSpace(written.Text)) return;
            await Save(session.Settings with { Dictionary = [..session.Settings.Dictionary, new(heard.Text.Trim(), written.Text.Trim())] }); Render();
        }));
    }

    private async Task SignInAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 1455);
        listener.Start();
        try
        {
            var signIn = ChatGptAuth.Begin();
            StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(signIn.Url)));
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                var line = await reader.ReadLineAsync(cancellation.Token);
                var path = line?.Split(' ').ElementAtOrDefault(1);
                if (path is null || !path.StartsWith("/auth/callback?", StringComparison.Ordinal)) continue;
                await new ChatGptAuth(session.Http, session.Secrets).CompleteAsync(signIn, "http://localhost:1455" + path, cancellation.Token);
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 24\r\nConnection: close\r\n\r\nDictate is now signed in."), cancellation.Token);
                session.Message("Signed in with ChatGPT."); return;
            }
        }
        finally { listener.Stop(); }
    }

    private async Task Save(DictationSettings settings)
    {
        try { await session.Store.SaveSettingsAsync(settings); session.Settings = settings; }
        catch (Exception error) { session.Message(error.Message); }
    }
    private TextView Text(string value) => new(this) { Text = value, TextSize = 16 };
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button(this) { Text = label };
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try { await action(); } catch (Exception error) { session.Message(error.Message); }
            finally { button.Enabled = true; }
        };
        return button;
    }
    private CheckBox Check(string label, bool value, Func<bool, Task> changed)
    {
        var check = new CheckBox(this) { Text = label, Checked = value };
        check.CheckedChange += async (_, args) => await changed(args.IsChecked);
        return check;
    }
    private void Copy(string value) => ((global::Android.Content.ClipboardManager)GetSystemService(ClipboardService)!).PrimaryClip = ClipData.NewPlainText("Dictate", value);
    private void Message(string text) => MainThread.Post(() => status.Text = text);
    private void Completed(TranscriptRecord record) => MainThread.Post(() => { if (screen == "History") Render(); });
    protected override void OnDestroy() { session.Notice -= Message; session.Controller.Completed -= Completed; base.OnDestroy(); }
}
