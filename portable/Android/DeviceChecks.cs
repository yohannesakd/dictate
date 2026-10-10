#if DEBUG
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
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
            result.PutString("stream", "Android native insertion, correction, binding changes, Keystore, microphone, and CPU speech passed.\n");
            Finish(Result.Ok, result);
        }
        catch (Exception error) { result.PutString("stream", error + "\n"); Finish(Result.Canceled, result); }
    }

    private async Task CheckAsync()
    {
        var context = TargetContext!;
        using var intent = new Intent(context, typeof(MainActivity));
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
        var transcript = await speech.TranscribeAsync(pcm.ToArray(), "tiny.en", "en", CancellationToken.None);
        Require(transcript.Contains("country", StringComparison.OrdinalIgnoreCase) && transcript.Contains("fellow", StringComparison.OrdinalIgnoreCase), "Unexpected CPU transcript: " + transcript);
        Require(await speech.TranscribeAsync(new float[32000], "tiny.en", "en", CancellationToken.None) == "", "Silent audio produced dictation text.");
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
}
#endif
