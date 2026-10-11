using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using System.Text.Json;
using Dictate.Core;

namespace Dictate.Android;

[Activity(Name = "dev.local.dictate.portable.MainActivity", Label = "Dictate", MainLauncher = true, Exported = true,
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleTop,
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : Activity
{
    private Session session = null!;
    internal AppView View { get; private set; } = null!;
    private bool exporting;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        session = Session.Get(this);
        View = new(this, session, false, WindowActionAsync);
        View.Bridge.InitialWord = Intent?.GetStringExtra("dictionary_word");
        View.Bridge.InitialSection = Intent?.GetStringExtra("settings_section");
        var root = new InsetLayout(this, includeStatusBar: true, padding: 0);
        root.SetBackgroundColor(global::Android.Graphics.Color.Rgb(248, 249, 251));
        root.AddView(View, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        SetContentView(root);
        if (Intent?.GetBooleanExtra("request_microphone", false) == true) RequestMicrophone();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent); Intent = intent;
        if (intent?.GetStringExtra("dictionary_word") is { } word) View.Bridge.CaptureWord(word);
        else if (intent?.GetStringExtra("settings_section") is { } section) View.Bridge.Navigate(section);
        if (intent?.GetBooleanExtra("request_microphone", false) == true) RequestMicrophone();
    }
    protected override void OnResume() { base.OnResume(); if (View is not null) session.Refresh(); }
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, global::Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults); session.Refresh();
    }
    private void RequestMicrophone() => RequestPermissions([global::Android.Manifest.Permission.RecordAudio], 1);

    private Task<object?> WindowActionAsync(JsonElement action)
    {
        var type = action.ValueKind == JsonValueKind.String ? action.GetString() : action.GetProperty("type").GetString();
        var keyboards = (InputMethodManager)GetSystemService(Context.InputMethodService)!;
        switch (type)
        {
            case "copy":
                var id = action.GetProperty("id").GetGuid();
                var record = session.Store.History().FirstOrDefault(item => item.Id == id) ?? throw new ArgumentException("The dictation is no longer in History.");
                ((global::Android.Content.ClipboardManager)GetSystemService(ClipboardService)!).PrimaryClip =
                    ClipData.NewPlainText("Dictate", action.GetProperty("original").GetBoolean() ? record.Raw : record.Text);
                break;
            case "permissions": RequestMicrophone(); break;
            case "keyboard-settings": StartActivity(new Intent(global::Android.Provider.Settings.ActionInputMethodSettings)); break;
            case "choose-keyboard": keyboards.ShowInputMethodPicker(); break;
            case "dictate-field":
                keyboards.ShowSoftInput(View, ShowFlags.Implicit);
                var selected = global::Android.Provider.Settings.Secure.GetString(ContentResolver, "default_input_method");
                if (selected?.StartsWith("dev.local.dictate.portable/", StringComparison.Ordinal) != true) keyboards.ShowInputMethodPicker();
                break;
            case "open-log":
                if (exporting) throw new InvalidOperationException("Finish the current export first.");
                if (!session.Store.History().Any()) throw new InvalidOperationException("There are no dictations to export.");
                var intent = new Intent(Intent.ActionCreateDocument).AddCategory(Intent.CategoryOpenable)!.SetType("application/x-ndjson")!;
                intent.PutExtra(Intent.ExtraTitle, "dictate-transcripts.jsonl");
                exporting = true;
                try { StartActivityForResult(intent, 42); } catch { exporting = false; throw; }
                break;
            case "quit": Finish(); break;
            default: throw new ArgumentException("The app action is not supported.");
        }
        return Task.FromResult<object?>(null);
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != 42) return;
        exporting = false;
        if (resultCode != Result.Ok || data?.Data is not { } uri) return;
        try
        {
            using var output = ContentResolver!.OpenOutputStream(uri) ?? throw new IOException("The export file could not be opened.");
            using var input = File.OpenRead(Path.Combine(session.Store.DirectoryPath, "transcripts.jsonl"));
            await input.CopyToAsync(output);
            session.Message("Transcript log exported.");
        }
        catch (Exception error) { session.Message("The log export did not finish: " + error.Message); }
    }

    protected override void OnDestroy() { View.Close(); base.OnDestroy(); }
}
