#if DEBUG
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Android.Webkit;
using System.Text.Json;
using Dictate.Core;
using Dictate.Speech;

namespace Dictate.Android;

[Instrumentation(Name = "dev.local.dictate.portable.DeviceChecks", TargetPackage = "dev.local.dictate.portable", FunctionalTest = true)]
public sealed class DeviceChecks : Instrumentation
{
    public DeviceChecks() { }
    public DeviceChecks(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    public override void OnCreate(Bundle? arguments) { base.OnCreate(arguments); Start(); }
    public override void OnStart()
    {
        using var result = new Bundle();
        try
        {
            CheckAsync().GetAwaiter().GetResult();
            result.PutString("stream", "Android shared UI, English profile, model preparation, clipboard, draft preservation, native insertion, correction, binding changes, Keystore, microphone, and CPU speech passed.\n");
            Finish(Result.Ok, result);
        }
        catch (Exception error) { result.PutString("stream", error + "\n"); Finish(Result.Canceled, result); }
    }

    private async Task CheckAsync()
    {
        var context = TargetContext!;
        await CheckAppAsync(context);
        using var intent = new Intent(context, typeof(FieldCheckActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        var activity = StartActivitySync(intent)!;
        WaitForIdleSync();
        EditText? field = null;
        IInputConnection? connection = null;
        RunOnMainSync(() =>
        {
            IEnumerable<View> Views(View view)
            {
                yield return view;
                if (view is ViewGroup group) for (var i = 0; i < group.ChildCount; i++) foreach (var child in Views(group.GetChildAt(i)!)) yield return child;
            }
            field = Views(activity.Window!.DecorView).OfType<EditText>().First();
            field.Text = "Before "; field.RequestFocus(); field.SetSelection(field.Text!.Length);
            connection = field.OnCreateInputConnection(new EditorInfo());
        });
        var delivery = new AndroidDelivery();
        delivery.Bind(connection, context.PackageName);
        var first = await delivery.CaptureTarget().InsertAsync("Hello ☀.", "hello", CancellationToken.None);
        Require(first.Outcome == DeliveryOutcome.Verified && first.Correction is not null, "Text was not verified: " + first);
        Require(await first.Correction!.ReplaceAsync("", CancellationToken.None), "Undo failed.");
        var stale = delivery.CaptureTarget();
        delivery.Bind(connection, context.PackageName);
        Require((await stale.InsertAsync("wrong", "wrong", CancellationToken.None)).Outcome == DeliveryOutcome.FocusChanged, "The stale binding accepted text.");
        var second = await delivery.CaptureTarget().InsertAsync("New text.", "new", CancellationToken.None);
        Require(second.Correction is not null, "No correction receipt.");
        RunOnMainSync(() => { field!.Append(" changed"); field.SetSelection(field.Text!.Length); });
        Require(!await second.Correction!.ReplaceAsync("", CancellationToken.None), "Correction changed edited text.");
        var secrets = new AndroidSecrets(context);
        var account = "device-check-" + Guid.NewGuid();
        try
        {
            await secrets.WriteAsync(account, "test-only");
            Require(await secrets.ReadAsync(account) == "test-only", "Keystore roundtrip failed.");
        }
        finally { await secrets.WriteAsync(account, null); }
        Require(await secrets.ReadAsync(account) is null, "Credential removal failed.");
        using (var audio = new AndroidAudio()) { audio.Start(); await Task.Delay(500); Require(audio.Stop().Length > 0, "Microphone returned no samples."); }
        var path = Path.Combine(context.FilesDir!.AbsolutePath, "device-check.wav");
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 44;
        var pcm = new List<float>();
        while (reader.BaseStream.Position + 2 <= reader.BaseStream.Length) pcm.Add(reader.ReadInt16() / 32768f);
        using var speech = new WhisperEngine(Path.Combine(context.FilesDir.AbsolutePath, "models"));
        var hardware = new List<object>();
        string transcript = "";
        foreach (var mode in new[] { SpeechComputeMode.CpuOnly, SpeechComputeMode.Automatic, SpeechComputeMode.CpuOnly })
        {
            transcript = await speech.TranscribeAsync(pcm.ToArray(), SpeechProfiles.WhisperModel, "en", CancellationToken.None, mode);
            Require(transcript.Contains("country", StringComparison.OrdinalIgnoreCase) && transcript.Contains("fellow", StringComparison.OrdinalIgnoreCase), "Unexpected transcript: " + transcript);
            Require(mode != SpeechComputeMode.CpuOnly || speech.ActiveHardware == "CPU", "CPU-only mode reported an accelerator.");
            hardware.Add(new { mode, speech.ActiveHardware, speech.FallbackReason, transcript });
        }
        File.WriteAllText(Path.Combine(context.FilesDir.AbsolutePath, "speech-hardware.json"), JsonSerializer.Serialize(hardware));
        RunOnMainSync(() => { field!.Text = ""; field.SetSelection(0); });
        delivery.Bind(connection, context.PackageName);
        Require((await delivery.CaptureTarget().InsertAsync(transcript, transcript, CancellationToken.None)).Outcome == DeliveryOutcome.Verified,
            "Whisper transcript did not reach the real field.");
        await Dictate.Checks.SpeechEvaluation.CheckCancellationAsync(speech, pcm.ToArray(), SpeechProfiles.WhisperModel);
        var manifest = Path.Combine(context.FilesDir.AbsolutePath, "evaluation", "samples.json");
        if (File.Exists(manifest)) await Dictate.Checks.SpeechEvaluation.RunAsync(speech, manifest,
            Path.Combine(context.FilesDir.AbsolutePath, "speech-evaluation.json"));
    }

    private async Task CheckAppAsync(Context context)
    {
        using var intent = new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask);
        var activity = (MainActivity)StartActivitySync(intent)!;
        var session = Session.Get(context);
        async Task<JsonElement> Evaluate(string script)
        {
            var completion = new TaskCompletionSource<string>();
            RunOnMainSync(() => activity.View.EvaluateJavascript(script, new JavascriptResult(completion)));
            return JsonSerializer.Deserialize<JsonElement>(await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        async Task Wait(string script)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if ((await Evaluate(script)).ValueKind == JsonValueKind.True) return;
                await Task.Delay(100);
            }
            throw new InvalidOperationException("Android UI did not reach: " + script);
        }
        async Task Click(string selector)
        {
            await Evaluate($"document.querySelector({JsonSerializer.Serialize(selector)}).click()");
            await Task.Delay(200);
        }
        await Wait("!!document.querySelector('[data-action=dictate-field]')");
        await Wait("document.fonts.check('13px Inter')");
        Require((await Evaluate("document.documentElement.scrollWidth <= innerWidth")).GetBoolean(), "Android Home overflowed.");
        await Evaluate("window.testField=document.querySelector('#dictation-test');testField.value='Before ';testField.focus();testField.setSelectionRange(7,7)");
        RunOnMainSync(session.Refresh);
        await Task.Delay(200);
        Require((await Evaluate("testField===document.querySelector('#dictation-test') && document.activeElement===testField && testField.value==='Before ' && testField.selectionStart===7")).GetBoolean(), "A state update replaced the live dictation field.");
        await Click("[data-page=settings]");
        Require((await Evaluate("!document.querySelector('#engine-button') && !document.querySelector('input[name=speech-engine]')")).GetBoolean(), "The removed engine selector remains visible.");
        Require(session.Store.LoadSettings().SpeechEngine == SpeechEngineKind.Whisper, "The Android profile is not Whisper.");
        await session.PrepareAsync(CancellationToken.None);
        await Wait("document.querySelector('#model-status').textContent==='Downloaded'");
        foreach (var mode in new[] { SpeechComputeMode.CpuOnly, SpeechComputeMode.Automatic })
        {
            var value = mode == SpeechComputeMode.CpuOnly ? "cpu_only" : "automatic";
            await Evaluate($"window.hardwareSaved=false;window.dictate.command('settings',{{speech_compute:'{value}'}}).then(()=>window.hardwareSaved=true)");
            await Wait("window.hardwareSaved");
            await session.PrepareAsync(CancellationToken.None);
            Require(session.Speech.LoadedCompute == mode, "The Android hardware mode did not reach the recognizer.");
            if (mode == SpeechComputeMode.CpuOnly)
                await Wait("document.querySelector('#hardware-status').textContent.includes('Using CPU.')");
        }
        await Click("[data-section=cleanup]");
        var fillers = session.Settings.Rules.Fillers;
        await Click("#toggle-fillers");
        Require(session.Settings.Rules.Fillers != fillers, "The cleanup switch did not save.");
        await Click("#toggle-fillers");
        await Evaluate("document.querySelector('#api-key').value='test-only-unsaved';document.querySelector('#provider').value='deep_seek';document.querySelector('#provider').dispatchEvent(new Event('change',{bubbles:true}))");
        await Wait("document.querySelector('#api-key')?.dataset.provider==='deep_seek'");
        Require((await Evaluate("document.querySelector('#api-key').value==='' ")).GetBoolean(), "An unsaved credential crossed provider identities.");
        await Click("[data-section=dictionary]");
        await Evaluate("window.draft=document.querySelector('#heard');draft.value='unfinished phrase';draft.focus();draft.setSelectionRange(4,4)");
        RunOnMainSync(session.Refresh); await Task.Delay(200);
        Require((await Evaluate("draft===document.querySelector('#heard') && document.activeElement===draft && draft.selectionStart===4")).GetBoolean(), "An Android state update replaced the active editor.");
        await Evaluate("document.querySelector('#heard').value='verification phrase';document.querySelector('#written').value='Verification phrase';document.querySelector('#word-form').requestSubmit()");
        await Wait("document.querySelector('#heard').value==='' && !document.querySelector('#word-form button').disabled");
        Require(session.Settings.Dictionary.Any(entry => entry.Heard == "verification phrase"), "Android dictionary add failed.");
        await Click("[data-section=general]");
        Require((await Evaluate("document.documentElement.scrollWidth <= innerWidth")).GetBoolean(), "Android Settings overflowed.");
        await session.Store.AppendAsync(new TranscriptRecord { Raw = "sample raw", Rules = "Sample transcript.", Text = "Sample transcript.", Smart = "off", Delivery = DeliveryOutcome.Verified });
        RunOnMainSync(session.Refresh);
        await Click("[data-page=history]");
        await Click("[data-copy][data-original=false]");
        RunOnMainSync(() => Require(((global::Android.Content.ClipboardManager)context.GetSystemService(Context.ClipboardService)!).PrimaryClip?.GetItemAt(0)?.Text == "Sample transcript.", "Android copy did not reach the native clipboard."));
        await Evaluate("document.querySelector('#history-search').value='missing-fixture';document.querySelector('#history-search').focus();document.querySelector('#history-search').dispatchEvent(new Event('input',{bubbles:true}))");
        Require((await Evaluate("document.querySelectorAll('.transcript').length===0")).GetBoolean(), "Android history search failed.");
        await Evaluate("window.rejected=false;window.dictate.command('unsupported',{}).catch(()=>window.rejected=true)");
        await Wait("window.rejected");
        RunOnMainSync(() => { session.ClearNotice(); activity.Finish(); });
    }

    private sealed class JavascriptResult(TaskCompletionSource<string> completion) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) => completion.TrySetResult(value?.ToString() ?? "null");
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
}

[Activity(Exported = false)]
public sealed class FieldCheckActivity : Activity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        SetContentView(new EditText(this));
    }
}
#endif
