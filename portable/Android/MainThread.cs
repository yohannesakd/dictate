using Android.OS;

namespace Dictate.Android;

internal static class MainThread
{
    private static readonly Handler Handler = new(Looper.MainLooper!);
    public static void Post(Action action) => Handler.Post(action);
    public static Task<T> Run<T>(Func<T> action)
    {
        if (Looper.MyLooper() == Looper.MainLooper) return Task.FromResult(action());
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Handler.Post(() => { try { completion.SetResult(action()); } catch (Exception error) { completion.SetException(error); } });
        return completion.Task;
    }
}
