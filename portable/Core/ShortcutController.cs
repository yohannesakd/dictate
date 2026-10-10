namespace Dictate.Core;

public sealed class ShortcutController(DictationController controller, Func<DictationSettings> settings) : IDisposable
{
    private enum Mode { Idle, Holding, AwaitingSecondTap, HandsFree }
    private Mode mode;
    private long pressed;
    private Timer? secondTap;
    private long tapId;
    private readonly object sync = new();
    public bool HandsFree { get { lock (sync) return mode == Mode.HandsFree; } }

    public void Down()
    {
        lock (sync)
        {
            if (controller.Phase is DictationPhase.Processing or DictationPhase.Delivering) return;
            if (controller.Phase == DictationPhase.Idle && mode != Mode.Idle) { secondTap?.Dispose(); mode = Mode.Idle; }
            if (mode == Mode.HandsFree) { mode = Mode.Idle; _ = controller.FinishAsync(); return; }
            if (mode == Mode.AwaitingSecondTap) { secondTap?.Dispose(); mode = Mode.HandsFree; return; }
            if (mode != Mode.Idle) return;
            controller.Start(settings());
            mode = Mode.Holding;
            pressed = Environment.TickCount64;
        }
    }

    public void Up()
    {
        lock (sync)
        {
            if (mode != Mode.Holding) return;
            if (Environment.TickCount64 - pressed >= 300) { mode = Mode.Idle; _ = controller.FinishAsync(); return; }
            mode = Mode.AwaitingSecondTap;
            var currentTap = ++tapId;
            secondTap = new Timer(_ =>
            {
                lock (sync)
                {
                    if (mode != Mode.AwaitingSecondTap || tapId != currentTap) return;
                    mode = Mode.Idle;
                    controller.Cancel();
                }
            }, null, 350, Timeout.Infinite);
        }
    }

    public void ToggleHandsFree()
    {
        lock (sync)
        {
            if (controller.Phase is DictationPhase.Processing or DictationPhase.Delivering) return;
            secondTap?.Dispose();
            if (controller.Phase == DictationPhase.Idle) { controller.Start(settings()); mode = Mode.HandsFree; }
            else if (mode == Mode.HandsFree) { mode = Mode.Idle; _ = controller.FinishAsync(); }
            else mode = Mode.HandsFree;
        }
    }

    public void Cancel()
    {
        lock (sync) { secondTap?.Dispose(); secondTap = null; mode = Mode.Idle; controller.Cancel(); }
    }

    public void Dispose() { secondTap?.Dispose(); }
}
