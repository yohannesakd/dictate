using Android.App;
using Android.Content;
using Android.InputMethodServices;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using System.Text.Json;

namespace Dictate.Android;

[Service(Name = "dev.local.dictate.portable.DictateInputMethod", Label = "Dictate", Permission = "android.permission.BIND_INPUT_METHOD", Exported = true)]
[IntentFilter(["android.view.InputMethod"])]
[MetaData("android.view.im", Resource = "@xml/input_method")]
public sealed class DictateInputMethod : InputMethodService
{
    private Session session = null!;
    private AppView? view;

    public override void OnCreate() { base.OnCreate(); session = Session.Get(this); }
    public override View? OnCreateInputView()
    {
        view?.Close();
        view = new(this, session, true, WindowActionAsync);
        var root = new InsetLayout(this, includeStatusBar: false, padding: 0);
        root.SetBackgroundColor(global::Android.Graphics.Color.Rgb(248, 249, 251));
        root.AddView(view, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)(244 * Resources!.DisplayMetrics!.Density)));
        return root;
    }

    private Task<object?> WindowActionAsync(JsonElement action)
    {
        var type = action.GetString();
        if (type == "choose-keyboard") ((InputMethodManager)GetSystemService(Context.InputMethodService)!).ShowInputMethodPicker();
        else
        {
            session.Controller.Cancel();
            var intent = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
            if (type == "capture-word")
            {
                var selected = session.Delivery.SelectedText();
                if (string.IsNullOrWhiteSpace(selected)) throw new InvalidOperationException("Select the word to add first.");
                intent!.PutExtra("dictionary_word", selected.Trim());
            }
            else if (type == "permissions") intent!.PutExtra("request_microphone", true);
            else if (type == "open-app") intent!.PutExtra("settings_section", "speech");
            else throw new ArgumentException("The keyboard action is not supported.");
            StartActivity(intent);
        }
        return Task.FromResult<object?>(null);
    }

    public override void OnStartInput(EditorInfo? attribute, bool restarting)
    {
        base.OnStartInput(attribute, restarting); session.Controller.Cancel();
        var variation = attribute?.InputType & InputTypes.MaskVariation;
        var inputClass = attribute?.InputType & InputTypes.MaskClass;
        var password = inputClass == InputTypes.ClassText && variation is InputTypes.TextVariationPassword or InputTypes.TextVariationVisiblePassword or InputTypes.TextVariationWebPassword
            || inputClass == InputTypes.ClassNumber && variation == InputTypes.NumberVariationPassword;
        session.Delivery.Bind(password ? null : CurrentInputConnection, attribute?.PackageName);
        session.Refresh();
    }
    public override void OnFinishInput() { session.Controller.Cancel(); session.Delivery.Bind(null, null); session.Refresh(); base.OnFinishInput(); }
    public override void OnFinishInputView(bool finishingInput) { session.Controller.Cancel(); base.OnFinishInputView(finishingInput); }
    public override void OnDestroy()
    {
        view?.Close(); session.Controller.Cancel(); session.Delivery.Bind(null, null); base.OnDestroy();
    }
}
