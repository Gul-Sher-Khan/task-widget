namespace TaskWidget.Core;

// One named mutex per session. A second launch signals the owner, then leaves.
public sealed class InstanceGate : IDisposable
{
    public const string DefaultName = "TaskWidget";
    const int AbandonedGrace = 15;

    readonly Mutex? mutex;
    readonly EventWaitHandle? raise;
    readonly CancellationTokenSource? stop;
    Thread? listener;
    int disposed;

    InstanceGate(Mutex? mutex, EventWaitHandle? raise, bool isFirstInstance)
    {
        this.mutex = mutex;
        this.raise = raise;
        IsFirstInstance = isFirstInstance;
        if (isFirstInstance)
            stop = new CancellationTokenSource();
    }

    public bool IsFirstInstance { get; }

    public event Action? RaiseRequested;

    public static bool TryAcquire(string name, out InstanceGate gate)
    {
        var mutex = new Mutex(false, MutexName(name));
        var owned = false;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        // A zero timeout can miss a mutex a dead owner just abandoned, so the
        // next launch looks blocked. A short wait still loses to a live instance.
        if (!owned)
        {
            try
            {
                owned = mutex.WaitOne(AbandonedGrace);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }
        }

        if (!owned)
        {
            mutex.Dispose();
            gate = new InstanceGate(null, null, false);
            return false;
        }

        var raise = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(name));
        gate = new InstanceGate(mutex, raise, true);
        return true;
    }

    public static void SignalRaise(string name)
    {
        using var raise = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(name));
        raise.Set();
    }

    public void Listen()
    {
        if (!IsFirstInstance || listener is not null)
            return;

        listener = new Thread(WaitForRaise)
        {
            IsBackground = true,
            Name = "TaskWidget raise",
        };
        listener.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        stop?.Cancel();
        if (listener is not null && listener != Thread.CurrentThread)
            listener.Join(TimeSpan.FromSeconds(2));

        raise?.Dispose();
        if (mutex is not null)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Already abandoned or released.
            }

            mutex.Dispose();
        }

        stop?.Dispose();
    }

    void WaitForRaise()
    {
        var token = stop!.Token;
        var handle = raise!;
        while (!token.IsCancellationRequested)
        {
            bool signaled;
            try
            {
                signaled = handle.WaitOne(TimeSpan.FromMilliseconds(200));
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (signaled && !token.IsCancellationRequested)
                RaiseRequested?.Invoke();
        }
    }

    static string MutexName(string name) => @"Local\TaskWidget.SingleInstance." + name;

    static string EventName(string name) => @"Local\TaskWidget.Raise." + name;
}
