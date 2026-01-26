using System;
using System.Threading;

namespace SharedLib;

public sealed class NpcResetEvent : ManualResetEventSlim, INpcResetEvent
{
    private readonly ManualResetEventSlim changeEvent;

    public NpcResetEvent() : base()
    {
        changeEvent = new(false);
    }

    public new WaitHandle WaitHandle => changeEvent.WaitHandle;

    public new void Dispose()
    {
        changeEvent.Dispose();
        base.Dispose();
    }

    public void ChangeReset()
    {
        changeEvent.Reset();
    }

    public void ChangeSet()
    {
        changeEvent.Set();
    }
}

public interface INpcResetEvent : IDisposable
{
    void Reset();
    void Wait();
    void Wait(CancellationToken token);

    void Set();

    void ChangeReset();
    void ChangeSet();

    WaitHandle WaitHandle { get; }
}
