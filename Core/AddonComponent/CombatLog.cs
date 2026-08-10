using System;
using System.Collections.Generic;

namespace Core;

public sealed class CombatLog : IReader
{
    private const int PLAYER_DEATH_EVENT = 16777215;

    private readonly AddonBits bits;

    private bool wasInCombat;

    private int petTargetGuid;

    public event Action? KillCredit;
    public event Action? PlayerDeath;
    public event Action? TargetEvade;

    public HashSet<int> DamageDone { get; } = [];
    public HashSet<int> DamageTaken { get; } = [];
    public HashSet<int> EvadeMobs { get; } = [];
    public HashSet<int> EnemySummons { get; } = [];

    public HashSet<int> ToPull { get; } = [];
    public HashSet<int> RecentlyDead { get; } = [];

    public int DamageTakenCount() => DamageTaken.Count;
    public int DamageDoneCount() => DamageDone.Count;

    public int ToPullCount() => ToPull.Count;

    public RecordInt DamageDoneGuid { get; }
    public RecordInt DamageTakenGuid { get; }
    public RecordInt DeadGuid { get; }

    public RecordInt TargetMissType { get; }
    public RecordInt TargetDodge { get; }

    public RecordInt LastDamageDoneTime { get; }
    public RecordInt EnemySummonGuid { get; }

    /// <summary>
    /// The pet is holding a live mob that blows have already been traded with.
    ///
    /// <para>A pet opener does not flag the player as being in combat - from Wrath on,
    /// <c>UnitAffectingCombat("player")</c> only turns true once the mob reaches the
    /// player and swings, which can be many seconds after the pull connected. Until
    /// then <see cref="AddonBits.Combat"/> is the wrong question to ask about whether
    /// the bot is fighting.</para>
    ///
    /// <para>Landed damage is the earliest honest proof. Pet target alone is not:
    /// PetAttack sets it while the pet is still running, and
    /// <see cref="ToPull"/> is filled the moment PullTargetGoal has a target, both
    /// before anything has been hit. <see cref="DamageTaken"/> carries the mirror
    /// case - the addon reports blows landed on the pet as taken - so a mob that
    /// aggroes the pet before the pet swings counts too.</para>
    ///
    /// <para>The mob's own <see cref="AddonBits.Target_Combat"/> flag looks like it
    /// should work here and does not: it stayed false for the whole of a live pet
    /// pull, so it never once beat the combat log to the answer.</para>
    /// </summary>
    public bool PetEngaged =>
        bits.Pet() &&
        petTargetGuid != 0 &&
        !bits.PetTarget_Dead() &&
        (DamageDone.Contains(petTargetGuid) ||
        DamageTaken.Contains(petTargetGuid));

    /// <summary>
    /// Whether the bot is fighting, by the player's own combat flag or by its pet
    /// already being in the fight. Not the same thing as the user facing
    /// <c>InCombat</c> requirement, which stays bound to the raw player flag.
    /// </summary>
    public bool PlayerOrPetCombat() => bits.Combat() || PetEngaged;

    public CombatLog(AddonBits bits)
    {
        this.bits = bits;

        DamageDoneGuid = new RecordInt(64);
        DamageTakenGuid = new RecordInt(65);
        DeadGuid = new RecordInt(66);

        LastDamageDoneTime = new(109);
        EnemySummonGuid = new(110);

        TargetMissType = new(67);
        TargetDodge = new(67);
    }

    public void Reset()
    {
        wasInCombat = false;
        petTargetGuid = 0;

        DamageDone.Clear();
        DamageTaken.Clear();
        EnemySummons.Clear();
        RecentlyDead.Clear();

        DamageDoneGuid.Reset();
        DamageTakenGuid.Reset();
        DeadGuid.Reset();

        LastDamageDoneTime.Reset();
        EnemySummonGuid.Reset();

        TargetMissType.Reset();
        TargetDodge.Reset();
    }

    public void Update(IAddonDataProvider reader)
    {
        bool combat = bits.Combat();
        petTargetGuid = reader.GetInt(PlayerReader.PetTargetGuidCell);

        // A pet fights before the player is flagged, so gating on the player's own
        // combat flag alone drops the whole opener. Widening it to "a pet is out"
        // is not a floodgate: the addon only pushes rows whose combat log source or
        // destination is the player or one of its summons.
        bool record = combat || bits.Pet();

        LastDamageDoneTime.Update(reader);

        if (record && DamageTakenGuid.Updated(reader) && DamageTakenGuid.Value > 0)
        {
            DamageTaken.Add(DamageTakenGuid.Value);
        }

        if (record && DamageDoneGuid.Updated(reader) && DamageDoneGuid.Value > 0)
        {
            DamageDone.Add(DamageDoneGuid.Value);
        }

        if (record && EnemySummonGuid.Updated(reader) && EnemySummonGuid.Value > 0)
        {
            EnemySummons.Add(EnemySummonGuid.Value);
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
            RecentlyDead.Add(deadGuid);

            if (deadGuid == PLAYER_DEATH_EVENT)
            {
                PlayerDeath?.Invoke();
            }
            else
            {
                KillCredit?.Invoke();
            }
        }

        // Evaluated after the dead handling above, which is what takes a killed mob
        // back out of DamageDone / DamageTaken and so ends a pet only fight.
        bool engaged = combat || PetEngaged;

        if (wasInCombat && !engaged)
        {
            // left combat
            DamageTaken.Clear();
            DamageDone.Clear();
            EnemySummons.Clear();
            ToPull.Clear();
            RecentlyDead.Clear();
        }

        wasInCombat = engaged;
    }
}
