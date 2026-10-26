using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Dictate.Desktop;

/// <summary>Owns the native main loop without creating a desktop window.</summary>
public static class DesktopRuntime
{
    private sealed class NativeContext : SynchronizationContext
    {
        private static readonly MacNative.DispatchCallback dispatch = pointer =>
        {
            var handle = GCHandle.FromIntPtr(pointer);
            var action = (Action)handle.Target!;
            handle.Free();
            action();
        };
        public override void Post(SendOrPostCallback callback, object? state)
        {
            var handle = GCHandle.Alloc((Action)(() => callback(state)));
            MacNative.Post(dispatch, GCHandle.ToIntPtr(handle));
        }
    }

    private sealed class QueueContext : SynchronizationContext, IDisposable
    {
        internal readonly BlockingCollection<Action> Queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => Queue.Add(() => callback(state));
        public void Dispose() => Queue.Dispose();
    }

    public static void Run(Func<Task> action)
    {
        Exception? failure = null;
        if (OperatingSystem.IsMacOS())
        {
            SynchronizationContext.SetSynchronizationContext(new NativeContext());
            MacNative.LoopCallback started = async () =>
            {
                try { await action(); }
                catch (Exception error) { failure = error; }
                finally { MacNative.StopLoop(); }
            };
            MacNative.RunLoop(started);
            GC.KeepAlive(started);
        }
        else
        {
            using var context = new QueueContext();
            SynchronizationContext.SetSynchronizationContext(context);
            context.Post(async _ =>
            {
                try { await action(); }
                catch (Exception error) { failure = error; }
                finally { context.Queue.CompleteAdding(); }
            }, null);
            foreach (var next in context.Queue.GetConsumingEnumerable()) next();
        }
        if (failure is not null) throw new InvalidOperationException("The native dictation service stopped.", failure);
    }
}
