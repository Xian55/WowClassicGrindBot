using System;
using System.Collections.Generic;

namespace Core;

public sealed class CombatLog : IReader
{
    private const int PLAYER_DEATH_EVENT = 16777215;

    private readonly AddonBits bits;

    private bool wasInCombat;

    public event Action? KillCredit;
    public event Action? PlayerDeath;
    public event Action? TargetEvade;

    public HashSet<int> DamageDone { get; } = [];
    public HashSet<int> DamageTaken { get; } = [];
    public HashSet<int> EvadeMobs { get; } = [];

    public HashSet<int> ToPull { get; } = [];

    public int DamageTakenCount() => DamageTaken.Count;
    public int DamageDoneCount() => DamageDone.Count;

    public int ToPullCount() => ToPull.Count;

    public RecordInt DamageDoneGuid { get; }
    public RecordInt DamageTakenGuid { get; }
    public RecordInt DeadGuid { get; }

    public RecordInt TargetMissType { get; }
    public RecordInt TargetDodge { get; }

    public CombatLog(AddonBits bits)
    {
        this.bits = bits;

        DamageDoneGuid = new RecordInt(64);
        DamageTakenGuid = new RecordInt(65);
        DeadGuid = new RecordInt(66);

        TargetMissType = new(67);
        TargetDodge = new(67);
    }

    public void Reset()
    {
        wasInCombat = false;

        DamageDone.Clear();
        DamageTaken.Clear();

        DamageDoneGuid.Reset();
        DamageTakenGuid.Reset();
        DeadGuid.Reset();

        TargetMissType.Reset();
        TargetDodge.Reset();
    }

    public void Update(IAddonDataProvider reader)
    {
        bool combat = bits.Combat();

        if (combat && DamageTakenGuid.Updated(reader) && DamageTakenGuid.Value > 0)
        {
            DamageTaken.Add(DamageTakenGuid.Value);
        }

        if (combat && DamageDoneGuid.Updated(reader) && DamageDoneGuid.Value > 0)
        {
            DamageDone.Add(DamageDoneGuid.Value);
        }

        if (TargetMissType.Updated(reader))
        {
            switch ((MissType)TargetMissType.Value)
            {
                case MissType.DODGE:
                    TargetDodge.UpdateTime();
                    break;
                case MissType.EVADE:
                    if (DamageDoneGuid.Value > 0)
                    {
                        EvadeMobs.Add(DamageDoneGuid.Value);
                        TargetEvade?.Invoke();
                    }
                    break;
            }
        }

        if (DeadGuid.Updated(reader) && DeadGuid.Value > 0)
        {
            int deadGuid = DeadGuid.Value;
            DamageDone.Remove(deadGuid);
            DamageTaken.Remove(deadGuid);
            ToPull.Remove(deadGuid);

            if (deadGuid == PLAYER_DEATH_EVENT)
            {
                PlayerDeath?.Invoke();
            }
            else
            {
                KillCredit?.Invoke();
            }
        }

        if (wasInCombat && !combat)
        {
            // left combat
            DamageTaken.Clear();
            DamageDone.Clear();
            ToPull.Clear();
        }

        wasInCombat = combat;
    }
}