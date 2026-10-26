using Android.Content;
using Android.Views;
using Android.Widget;

namespace Dictate.Android;

internal sealed class InsetLayout : LinearLayout
{
    private readonly bool includeStatusBar;
    private readonly int spacing;

    public InsetLayout(Context context, bool includeStatusBar, int padding = 12) : base(context)
    {
        this.includeStatusBar = includeStatusBar;
        spacing = (int)(padding * context.Resources!.DisplayMetrics!.Density);
        Orientation = Orientation.Vertical;
        SetPadding(spacing, spacing, spacing, spacing);
        SetLayerType(LayerType.Software, null);
    }

    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets is not null && OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var types = WindowInsets.Type.SystemBars();
            if (includeStatusBar) types |= WindowInsets.Type.Ime();
            var bars = insets.GetInsets(types);
            SetPadding(spacing + bars.Left, spacing + (includeStatusBar ? bars.Top : 0), spacing + bars.Right, spacing + bars.Bottom);
        }
        return insets;
    }
}
