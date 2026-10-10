using Android.App;
using Android.Content;
using Android.InputMethodServices;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Dictate.Core;

namespace Dictate.Android;

[Service(Name = "dev.local.dictate.portable.DictateInputMethod", Label = "Dictate", Permission = "android.permission.BIND_INPUT_METHOD", Exported = true)]
[IntentFilter(["android.view.InputMethod"])]
[MetaData("android.view.im", Resource = "@xml/input_method")]
public sealed class DictateInputMethod : InputMethodService
{
    private Session session = null!;
    private TextView? status;
    private Button? record;
    private bool password;

    public override void OnCreate()
    {
        base.OnCreate();
        session = Session.Get(this);
        session.Notice += Message;
        session.Controller.PhaseChanged += Phase;
    }

    public override View? OnCreateInputView()
    {
        var layout = new InsetLayout(this, includeStatusBar: false);
        layout.SetBackgroundColor(global::Android.Graphics.Color.Rgb(245, 245, 245));
        status = new TextView(this) { Text = session.Status };
        layout.AddView(status);
        record = Button("Start dictation", async () =>
        {
            if (password) { session.Message("Dictation is disabled in password fields."); return; }
            if (CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) != global::Android.Content.PM.Permission.Granted)
            {
                var intent = new Intent(this, typeof(MainActivity)); intent.AddFlags(ActivityFlags.NewTask); StartActivity(intent); return;
            }
            if (session.Controller.Phase == DictationPhase.Listening) await session.Controller.FinishAsync();
            else session.Controller.Start(session.Settings);
        });
        layout.AddView(record);
        Phase(session.Controller.Phase);
        layout.AddView(Button("Cancel", () => { session.Controller.Cancel(); return Task.CompletedTask; }));
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.AddView(Button("Undo", async () => session.Message(await session.Controller.CorrectLastAsync(false) ? "Dictation removed." : "The field cannot be corrected safely.")));
        row.AddView(Button("Original", async () => session.Message(await session.Controller.CorrectLastAsync(true) ? "Original transcript restored." : "The field cannot be corrected safely.")));
        row.AddView(Button("Keyboard", () => { ((InputMethodManager)GetSystemService(Context.InputMethodService)!).ShowInputMethodPicker(); return Task.CompletedTask; }));
        layout.AddView(row);
        layout.AddView(Button("Repeat last", async () =>
        {
            var last = session.Store.History().FirstOrDefault();
            if (last is not null) { var receipt = await session.Controller.RepeatAsync(last.Text, last.Raw); if (receipt.Error is not null) session.Message(receipt.Error); }
        }));
        layout.AddView(Button("Add selected word", () =>
        {
            var selected = session.Delivery.SelectedText();
            if (string.IsNullOrWhiteSpace(selected)) { session.Message("Select the word to add first."); return Task.CompletedTask; }
            session.Controller.Cancel();
            var intent = new Intent(this, typeof(MainActivity));
            intent.PutExtra("dictionary_word", selected); intent.AddFlags(ActivityFlags.NewTask); StartActivity(intent);
            return Task.CompletedTask;
        }));
        return layout;
    }

    public override void OnStartInput(EditorInfo? attribute, bool restarting)
    {
        base.OnStartInput(attribute, restarting);
        session.Controller.Cancel();
        var variation = attribute?.InputType & InputTypes.MaskVariation;
        var inputClass = attribute?.InputType & InputTypes.MaskClass;
        password = inputClass == InputTypes.ClassText && variation is InputTypes.TextVariationPassword or InputTypes.TextVariationVisiblePassword or InputTypes.TextVariationWebPassword
            || inputClass == InputTypes.ClassNumber && variation == InputTypes.NumberVariationPassword;
        session.Delivery.Bind(password ? null : CurrentInputConnection, attribute?.PackageName);
        if (record is not null) record.Enabled = !password;
        if (password) Message("Dictation is disabled in password fields.");
    }

    public override void OnFinishInput()
    {
        session.Controller.Cancel(); session.Delivery.Bind(null, null); base.OnFinishInput();
    }

    public override void OnFinishInputView(bool finishingInput)
    {
        session.Controller.Cancel();
        base.OnFinishInputView(finishingInput);
    }

    public override void OnDestroy()
    {
        session.Notice -= Message; session.Controller.PhaseChanged -= Phase;
        session.Controller.Cancel(); session.Delivery.Bind(null, null); base.OnDestroy();
    }

    private void Message(string text) => MainThread.Post(() => { if (status is not null) status.Text = text; });
    private void Phase(DictationPhase phase) => MainThread.Post(() =>
    {
        if (record is null) return;
        var current = session.Controller.Phase;
        record.Text = current == DictationPhase.Listening ? "Stop and insert" : "Start dictation";
        record.Enabled = !password && current is DictationPhase.Idle or DictationPhase.Listening;
    });
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button(this) { Text = label };
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { session.Message(error.Message); } };
        return button;
    }
}
