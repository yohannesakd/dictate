using Android.Content;
using Android.Webkit;
using Dictate.Core;

namespace Dictate.Android;

internal sealed class AppView : WebView
{
    internal AndroidBridge Bridge { get; }

    internal AppView(Context context, Session session, bool keyboard, Func<System.Text.Json.JsonElement, Task<object?>> windowAction) : base(context)
    {
        SetBackgroundColor(global::Android.Graphics.Color.Rgb(248, 249, 251));
        SetLayerType(global::Android.Views.LayerType.Software, null);
        Settings.JavaScriptEnabled = true;
        Settings.AllowFileAccess = false;
        Settings.AllowContentAccess = false;
        Settings.MixedContentMode = MixedContentHandling.NeverAllow;
        Settings.SetSupportMultipleWindows(false);
        Bridge = new(this, session, keyboard, windowAction);
        AddJavascriptInterface(Bridge, "AndroidBridge");
        SetWebViewClient(new AssetsClient(context));
        LoadUrl("https://dictate.local/index.html");
    }

    internal void Close()
    {
        Bridge.Close();
        RemoveJavascriptInterface("AndroidBridge");
        StopLoading(); Destroy();
    }

    private sealed class AssetsClient(Context context) : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request) => true;
        public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url;
            if (url?.Scheme != "https" || url.Host != "dictate.local") return Empty();
            var path = url.Path?.TrimStart('/') ?? "";
            if (path.Contains("..", StringComparison.Ordinal)) return Empty();
            var mime = Path.GetExtension(path) switch
            {
                ".html" => "text/html", ".js" => "text/javascript", ".css" => "text/css", ".woff2" => "font/woff2", _ => null
            };
            if (mime is null) return Empty();
            try { return new(mime, mime == "font/woff2" ? null : "UTF-8", context.Assets!.Open("ui/" + path)); }
            catch (Java.IO.IOException) { return Empty(); }
        }
        private static WebResourceResponse Empty() => new("text/plain", "UTF-8", new MemoryStream());
    }
}
