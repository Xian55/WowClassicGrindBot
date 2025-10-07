using System;
using System.Threading;

namespace Core;

public interface IAddonReader
{
    double AvgUpdateLatency { get; }

    string TargetName { get; }

    event Action? AddonDataChanged;

    ManualResetEventSlim DataReady { get; }

    void FullReset();

    void Update();
    void UpdateUI();
    void SessionReset();
}