using System;
using System.Threading;

namespace Core.Addon;

public sealed class ConfigAddonReader : IAddonReader
{
    private readonly IAddonDataProvider reader;

    public double AvgUpdateLatency => throw new NotImplementedException();
    public string TargetName => throw new NotImplementedException();

    public ManualResetEventSlim DataReady { get; }

    public event Action? AddonDataChanged;

    public ConfigAddonReader(IAddonDataProvider reader, ManualResetEventSlim resetEvent)
    {
        this.reader = reader;
        DataReady = resetEvent;
    }

    public void FullReset()
    {
        throw new NotImplementedException();
    }

    public void Update()
    {
        reader.UpdateData();
        DataReady.Set();
    }

    public void UpdateUI()
    {
        AddonDataChanged?.Invoke();
    }

    public void SessionReset()
    {
        throw new NotImplementedException();
    }
}