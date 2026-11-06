namespace BetterTaskManager.Core;

/// <summary>Serializes background startup/shutdown, reconciling the latest request even while startup is blocked.</summary>
public sealed class BackgroundActivity(Func<bool> start, Action stop, Action<Exception> report) : IDisposable
{
    private readonly object gate = new();
    private bool wanted, active, disposed;
    private Task? worker;

    public Task Completion { get { lock (gate) return worker ?? Task.CompletedTask; } }

    public void SetEnabled(bool enabled)
    {
        lock (gate)
        {
            if (disposed) return;
            wanted = enabled;
            worker ??= Task.Run(Reconcile);
        }
    }

    private void Reconcile()
    {
        while (true)
        {
            bool target;
            lock (gate)
            {
                target = wanted;
                if (active == target) { worker = null; return; }
            }
            bool succeeded = true;
            try { if (target) succeeded = start(); else stop(); }
            catch (Exception ex) { succeeded = false; report(ex); }
            lock (gate)
            {
                if (succeeded) active = target;
                // Retry only on a later request, not in a tight loop after a trace error.
                if (!succeeded && wanted == target) { worker = null; return; }
            }
        }
    }

    public void Dispose()
    {
        Task completion;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            wanted = false;
            completion = worker ??= Task.Run(Reconcile);
        }
        completion.GetAwaiter().GetResult();
    }
}
