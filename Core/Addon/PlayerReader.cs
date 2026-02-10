using Core.Addon;
using Core.Database;
using Core.Goals;

using SharedLib;

using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Core;

public sealed partial class PlayerReader : IMouseOverReader, IReader
{
    private readonly IAddonDataProvider reader;
    private readonly WorldMapAreaDB worldMapAreaDB;
    private readonly AreaDB areaDb;
    private readonly AddonBits bits;

    private const int PartyPayloadCell = 111;
    private const int PartyPayloadMask = 0xFFFFF;

    private readonly PartyMemberState[] partyMembers = new PartyMemberState[4];

    private struct PartyMemberState
    {
        public int MapId;
        public float MapX;
        public float MapY;
        public bool Exists;
        public bool InCombat;
        public int NameHash;
        public int HealthPercent;
        public int PowerPercent;
        public int PowerType;
        public int BuffSpellId1;
        public int BuffSpellId2;
        public int BuffSpellId3;
        public int DebuffSpellId1;
        public int DebuffSpellId2;

        public bool HasCoordinates => MapId != 0 && (MapX != 0 || MapY != 0);
    }

    public readonly record struct PartyMemberDebug(int Slot, bool Exists, bool InCombat, int MapId, float MapX, float MapY, int NameHash);

    public readonly record struct PartyMemberStatus(
        int Slot,
        bool Exists,
        bool InCombat,
        int MapId,
        float MapX,
        float MapY,
        int NameHash,
        int HealthPercent,
        int PowerPercent,
        PowerType PowerType,
        int BuffSpellId1,
        int BuffSpellId2,
        int BuffSpellId3,
        int DebuffSpellId1,
        int DebuffSpellId2);

    public IReadOnlyList<PartyMemberDebug> PartyDebug =>
    [
        new PartyMemberDebug(1, partyMembers[0].Exists, partyMembers[0].InCombat, partyMembers[0].MapId, partyMembers[0].MapX, partyMembers[0].MapY, partyMembers[0].NameHash),
        new PartyMemberDebug(2, partyMembers[1].Exists, partyMembers[1].InCombat, partyMembers[1].MapId, partyMembers[1].MapX, partyMembers[1].MapY, partyMembers[1].NameHash),
        new PartyMemberDebug(3, partyMembers[2].Exists, partyMembers[2].InCombat, partyMembers[2].MapId, partyMembers[2].MapX, partyMembers[2].MapY, partyMembers[2].NameHash),
        new PartyMemberDebug(4, partyMembers[3].Exists, partyMembers[3].InCombat, partyMembers[3].MapId, partyMembers[3].MapX, partyMembers[3].MapY, partyMembers[3].NameHash)
    ];

    public IReadOnlyList<PartyMemberStatus> PartyStatus =>
    [
        new PartyMemberStatus(1, partyMembers[0].Exists, partyMembers[0].InCombat, partyMembers[0].MapId, partyMembers[0].MapX, partyMembers[0].MapY, partyMembers[0].NameHash, partyMembers[0].HealthPercent, partyMembers[0].PowerPercent, (PowerType)partyMembers[0].PowerType, partyMembers[0].BuffSpellId1, partyMembers[0].BuffSpellId2, partyMembers[0].BuffSpellId3, partyMembers[0].DebuffSpellId1, partyMembers[0].DebuffSpellId2),
        new PartyMemberStatus(2, partyMembers[1].Exists, partyMembers[1].InCombat, partyMembers[1].MapId, partyMembers[1].MapX, partyMembers[1].MapY, partyMembers[1].NameHash, partyMembers[1].HealthPercent, partyMembers[1].PowerPercent, (PowerType)partyMembers[1].PowerType, partyMembers[1].BuffSpellId1, partyMembers[1].BuffSpellId2, partyMembers[1].BuffSpellId3, partyMembers[1].DebuffSpellId1, partyMembers[1].DebuffSpellId2),
        new PartyMemberStatus(3, partyMembers[2].Exists, partyMembers[2].InCombat, partyMembers[2].MapId, partyMembers[2].MapX, partyMembers[2].MapY, partyMembers[2].NameHash, partyMembers[2].HealthPercent, partyMembers[2].PowerPercent, (PowerType)partyMembers[2].PowerType, partyMembers[2].BuffSpellId1, partyMembers[2].BuffSpellId2, partyMembers[2].BuffSpellId3, partyMembers[2].DebuffSpellId1, partyMembers[2].DebuffSpellId2),
        new PartyMemberStatus(4, partyMembers[3].Exists, partyMembers[3].InCombat, partyMembers[3].MapId, partyMembers[3].MapX, partyMembers[3].MapY, partyMembers[3].NameHash, partyMembers[3].HealthPercent, partyMembers[3].PowerPercent, (PowerType)partyMembers[3].PowerType, partyMembers[3].BuffSpellId1, partyMembers[3].BuffSpellId2, partyMembers[3].BuffSpellId3, partyMembers[3].DebuffSpellId1, partyMembers[3].DebuffSpellId2)
    ];

    public PlayerReader(
        IAddonDataProvider reader,
        WorldMapAreaDB mapAreaDB,
        AreaDB areaDb,
        AddonBits addonBits,
        SpellInRange spellInRange,
        Stance stance)
    {
        this.worldMapAreaDB = mapAreaDB;
        this.areaDb = areaDb;
        this.reader = reader;

        bits = addonBits;
        SpellInRange = spellInRange;
        Stance = stance;

        // TODO: inject! value type tho
        CustomTrigger1 = new(reader.GetInt(74));
    }

    public WorldMapArea WorldMapArea { get; private set; }

    public Vector3 MapPos => new(MapX, MapY, WorldPosZ);
    public Vector3 MapPosNoZ => new(MapX, MapY, 0);
    public Vector3 _MapPosNoZ() => MapPosNoZ;
    public Vector3 WorldPos => worldMapAreaDB.ToWorld_FlipXY(UIMapId.Value, MapPos);

    public float WorldPosZ { get; set; } // MapZ not exists. Alias for WorldLoc.Z

    public float MapX => reader.GetFixed(1) * 10;
    public float MapY => reader.GetFixed(2) * 10;

    public Vector3 TargetMapPos
    {
        get
        {
            if (!bits.Target())
            {
                return Vector3.Zero;
            }

            float targetDistance = (MaxRange() + MinRange()) / 2;

            return PointEstimator.GetMapPos(WorldMapArea, WorldPos, Direction, targetDistance);
        }
    }

    public float Direction => reader.GetFixed(3);

    public float _Direction() => Direction;

    public RecordInt UIMapId { get; } = new(4);

    public int MapId { get; private set; }

    public RecordInt Level { get; } = new(5);

    public Vector3 CorpseMapPos => new(CorpseMapX, CorpseMapY, 0);
    public float CorpseMapX => reader.GetFixed(6) * 10;
    public float CorpseMapY => reader.GetFixed(7) * 10;

    public int HealthMax() => reader.GetInt(10);
    public int HealthCurrent() => reader.GetInt(11);
    public int HealthPercent() => (1 + HealthCurrent()) * 100 / (1 + HealthMax());

    public int PTMax() => reader.GetInt(12); // Maximum amount of Power Type (dynamic)
    public int PTCurrent() => reader.GetInt(13); // Current amount of Power Type (dynamic)
    public int PTPercentage() => PTCurrent() * 100 / PTMax(); // Power Type (dynamic) in terms of a percentage

    public int ManaMax() => reader.GetInt(14);
    public int ManaCurrent() => reader.GetInt(15);
    public int ManaPercent() => (1 + ManaCurrent()) * 100 / (1 + ManaMax());

    public int MaxRune() => reader.GetInt(14);

    public int BloodRune() => reader.GetInt(15) / 100 % 10;
    public int FrostRune() => reader.GetInt(15) / 10 % 10;
    public int UnholyRune() => reader.GetInt(15) % 10;

    public int TargetMaxHealth() => reader.GetInt(18);
    public int TargetHealth() => reader.GetInt(19);
    public int TargetHealthPercent() => (1 + TargetHealth()) * 100 / (1 + TargetMaxHealth());

    public int PetMaxHealth() => reader.GetInt(38);
    public int PetHealth() => reader.GetInt(39);
    public int PetHealthPercent() => (1 + PetHealth()) * 100 / (1 + PetMaxHealth());
    public bool PetAlive() => PetHealth() > 0;

    public SpellInRange SpellInRange { get; }
    public bool WithInPullRange() => SpellInRange.WithinPullRange(this, Class);
    public bool WithInCombatRange() => SpellInRange.WithinCombatRange(this, Class);
    public bool OutOfCombatRange() => !SpellInRange.WithinCombatRange(this, Class);

    // TargetLevel * 100 + TargetClass
    public int TargetLevel => reader.GetInt(43) / 100;
    public UnitClassification TargetClassification => (UnitClassification)(reader.GetInt(43) % 100);

    public bool TargetIsElite() => TargetClassification == UnitClassification.Elite;

    public int Money => reader.GetInt(44) + (reader.GetInt(45) * 1000000);

    // RACE_ID * 10000 + CLASS_ID * 100 + ClientVersion
    public UnitRace Race => (UnitRace)(reader.GetInt(46) / 10000);
    public UnitClass Class => (UnitClass)(reader.GetInt(46) / 100 % 100);
    public ClientVersion Version => (ClientVersion)(reader.GetInt(46) % 100);

    public PlayerFaction Faction => Race switch {
        UnitRace.Human => PlayerFaction.Alliance,
        UnitRace.Dwarf => PlayerFaction.Alliance,
        UnitRace.NightElf => PlayerFaction.Alliance,
        UnitRace.Gnome => PlayerFaction.Alliance,
        UnitRace.Draenei => PlayerFaction.Alliance,
        UnitRace.Worgen => PlayerFaction.Alliance,
        UnitRace.Orc => PlayerFaction.Horde,
        UnitRace.Tauren => PlayerFaction.Horde,
        UnitRace.Undead => PlayerFaction.Horde,
        UnitRace.Troll => PlayerFaction.Horde,
        UnitRace.BloodElf => PlayerFaction.Horde,
        UnitRace.Goblin => PlayerFaction.Horde,
        _ => throw new ArgumentNullException(nameof(Faction)),
    };

    // 47 empty

    public Stance Stance { get; }
    public Form Form => Stance.Get(Class, bits.Stealthed(), Version);

    public int MinRange() => reader.GetInt(49) % 1000;
    public int MaxRange() => reader.GetInt(49) / 1000 % 1000;
    public bool MinRangeZero() => MinRange() == 0;

    public bool IsInMeleeRange() => MinRange() == 0 && MaxRange() != 0 && MaxRange() <= 5;
    public bool InCloseMeleeRange() => MinRange() == 0 && MaxRange() <= 2;

    public bool IsInDeadZone() => MinRange() >= 5 && SpellInRange.Target_Trade; // between 5-8 yard - hunter and warrior

    public RecordInt PlayerXp { get; } = new(50);

    public int PlayerMaxXp => reader.GetInt(51);
    public int PlayerXpPercent => (1 + PlayerXp.Value) * 100 / (1 + PlayerMaxXp);

    public int _PlayerXpPercent() => PlayerXpPercent;

    public RecordInt UIErrorTime { get; } = new(47);

    private UI_ERROR UIError => (UI_ERROR)reader.GetInt(52);
    public UI_ERROR LastUIError { get; set; }

    public int SpellBeingCast => reader.GetInt(53);
    public bool IsCasting() => SpellBeingCast != 0;

    // avgEquipDurability * 100 + target combo points
    public int ComboPoints() => reader.GetInt(54) % 100;
    public int AvgEquipDurability() => reader.GetInt(54) / 100; // 0-99

    public AuraCount AuraCount => new(reader, 55);

    public int TargetId => reader.GetInt(56);
    public int TargetGuid => reader.GetInt(57);

    public int SpellBeingCastByTarget => reader.GetInt(58);
    public bool IsTargetCasting() => SpellBeingCastByTarget != 0;

    // 10 * MouseOverTarget + TargetTarget
    public UnitsTarget MouseOverTarget => (UnitsTarget)(reader.GetInt(59) / 10 % 10);
    public UnitsTarget TargetTarget => (UnitsTarget)(reader.GetInt(59) % 10);
    public bool TargetsMe() => TargetTarget == UnitsTarget.Me;
    public bool TargetsPet() => TargetTarget == UnitsTarget.Pet;
    public bool TargetsNone() => TargetTarget == UnitsTarget.None;

    public RecordInt AutoShot { get; } = new(60);
    public RecordInt MainHandSwing { get; } = new(61);
    public RecordInt CastEvent { get; } = new(62);
    public UI_ERROR CastState => (UI_ERROR)CastEvent.Value;
    public RecordInt CastSpellId { get; } = new(63);

    public int PetGuid => reader.GetInt(68);
    public int PetTargetGuid => reader.GetInt(69);
    public bool PetTarget() => PetTargetGuid != 0;

    public int CastCount => reader.GetInt(70);

    public BitVector32 CustomTrigger1;

    // 10000 * off * 100 + main * 100
    public int MainHandSpeedMs() => reader.GetInt(75) % 10000 * 10;
    public int OffHandSpeed => reader.GetInt(75) / 10000 * 10;

    public int RemainCastMs => reader.GetInt(76);


    // MouseOverLevel * 100 + MouseOverClassification
    public int MouseOverLevel => reader.GetInt(85) / 100;
    public UnitClassification MouseOverClassification => (UnitClassification)(reader.GetInt(85) % 100);
    public int MouseOverId => reader.GetInt(86);
    public int MouseOverGuid => reader.GetInt(87);

    public int FocusHealthMax() => reader.GetInt(89);
    public int FocusHealthCurrent() => reader.GetInt(90);
    public int FocusHealthPercent() => (1 + FocusHealthCurrent()) * 100 / (1 + FocusHealthMax());

    public int LastCastGCD { get; private set; }
    public void ResetLastCastGCD()
    {
        LastCastGCD = 0;
    }
    public void ReadLastCastGCD()
    {
        LastCastGCD = reader.GetInt(94);
    }

    public RecordInt GCD { get; } = new(95);

    public int NetworkLatency => reader.GetInt(96) % 10000;

    public int DoubleNetworkLatency => 2 * NetworkLatency;

    public int HalfNetworkLatency => NetworkLatency / 2;

    public int SpellQueueTimeMs => reader.GetInt(96) / 10000 % 10000;

    public int _SpellQueueTimeMs() => SpellQueueTimeMs;

    public int _SpellQueueTimeMsNegative() => -SpellQueueTimeMs;

    public int HalfSpellQueueTimeMs => SpellQueueTimeMs / 2;

    // Formula (10 * LootWindowCount) + LootEvent(0-9)
    public RecordInt LootEvent { get; } = new(97);
    public RecordInt LootWindowCount { get; } = new(97);

    public int FocusGuid => reader.GetInt(77);
    public int FocusTargetGuid => reader.GetInt(78);

    public int RangedSpeedMs() => reader.GetInt(88) * 10;

    public int SoftInteract_Guid => reader.GetInt(101);

    public int SoftInteract_Id => reader.GetInt(102);

    public GuidType SoftInteract_Type => (GuidType)reader.GetInt(103);

    public void Update(IAddonDataProvider reader)
    {
        if (UIMapId.Updated(reader) && UIMapId.Value != 0 &&
            worldMapAreaDB.TryGet(UIMapId.Value, out WorldMapArea wma))
        {
            WorldMapArea = wma;
            MapId = wma.MapID;

            areaDb.Update(wma.AreaID);
        }

        CustomTrigger1 = new(reader.GetInt(74));

        PlayerXp.Update(reader);
        Level.Update(reader);

        AutoShot.Update(reader);
        MainHandSwing.Update(reader);
        CastEvent.Update(reader);
        CastSpellId.Update(reader);

        LootEvent.UpdateIncludeLeastSignificantDigit(reader, 10);
        LootWindowCount.UpdateExcludingLeastSignificantDigits(reader, 10);

        GCD.Update(reader);

        UIErrorTime.Update(reader);

        UpdatePartyMembers(reader);

        if (UIError != UI_ERROR.NONE)
            LastUIError = UIError;
    }

    public void Reset()
    {
        UIMapId.Reset();

        Array.Clear(partyMembers);

        // Reset all RecordInt
        AutoShot.Reset();
        MainHandSwing.Reset();
        CastEvent.Reset();
        CastSpellId.Reset();

        PlayerXp.Reset();
        Level.Reset();

        LootEvent.Reset();
        LootWindowCount.Reset();
        UIErrorTime.Reset();

        GCD.Reset();
    }

    private void UpdatePartyMembers(IAddonDataProvider provider)
    {
        int encoded = provider.GetInt(PartyPayloadCell);

        int prefix = encoded >> 20;
        int payload = encoded & PartyPayloadMask;

        int memberIndex = prefix & 0x3;
        int phase = prefix >> 2;

        if ((uint)memberIndex >= partyMembers.Length)
        {
            return;
        }

        ref PartyMemberState state = ref partyMembers[memberIndex];

        switch (phase)
        {
            case 0:
                int mapId = payload >> 2;
                bool inCombat = (payload & 0x2) != 0;
                bool exists = (payload & 0x1) != 0;

                state.Exists = exists;
                state.InCombat = exists && inCombat;
                state.MapId = exists ? mapId : 0;

                if (!exists)
                {
                    state.MapX = 0;
                    state.MapY = 0;
                    state.NameHash = 0;
                    state.HealthPercent = 0;
                    state.PowerPercent = 0;
                    state.PowerType = 0;
                    state.BuffSpellId1 = 0;
                    state.BuffSpellId2 = 0;
                    state.BuffSpellId3 = 0;
                    state.DebuffSpellId1 = 0;
                    state.DebuffSpellId2 = 0;
                }
                break;

            case 1:
                if (state.Exists)
                {
                    state.MapX = payload / 1_000_000f;
                }
                break;

            case 2:
                if (state.Exists)
                {
                    state.MapY = payload / 1_000_000f;
                }
                break;

            case 3:
                if (state.Exists)
                {
                    state.NameHash = payload;
                }
                break;

            case 4:
                if (state.Exists)
                {
                    state.HealthPercent = payload >> 13;
                    state.PowerPercent = (payload >> 6) & 0x7F;
                    state.PowerType = payload & 0x3F;
                }
                break;

            case 5:
                if (state.Exists)
                {
                    state.BuffSpellId1 = payload;
                }
                break;

            case 6:
                if (state.Exists)
                {
                    state.BuffSpellId2 = payload;
                }
                break;

            case 7:
                if (state.Exists)
                {
                    state.BuffSpellId3 = payload;
                }
                break;

            case 8:
                if (state.Exists)
                {
                    state.DebuffSpellId1 = payload;
                }
                break;

            case 9:
                if (state.Exists)
                {
                    state.DebuffSpellId2 = payload;
                }
                break;
        }
    }

    private bool TryCreatePartySnapshot(in PartyMemberState state, out PartyLeaderSnapshot snapshot)
    {
        if (!state.Exists || state.MapId == 0 || !state.HasCoordinates)
        {
            snapshot = PartyLeaderSnapshot.None;
            return false;
        }

        Vector3 map = new(state.MapX, state.MapY, 0);

        if (worldMapAreaDB.TryGet(state.MapId, out _))
        {
            Vector3 world = worldMapAreaDB.ToWorld_FlipXY(state.MapId, map);
            snapshot = PartyLeaderSnapshot.FromBoth(map, world, state.InCombat, state.MapId);
        }
        else
        {
            snapshot = PartyLeaderSnapshot.FromMap(map, state.InCombat, state.MapId);
        }

        return snapshot.HasPosition;
    }

    private static int HashName20(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(name.ToLowerInvariant());
        const uint offset = 2166136261;
        const uint prime = 16777619;

        uint hash = offset;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }

        return (int)(hash & PartyPayloadMask);
    }

    public bool TryGetPartySnapshotBySlot(int slot, out PartyLeaderSnapshot snapshot)
    {
        snapshot = PartyLeaderSnapshot.None;

        if (slot < 1 || slot > partyMembers.Length)
        {
            return false;
        }

        return TryCreatePartySnapshot(partyMembers[slot - 1], out snapshot);
    }

    public bool TryGetPartySnapshotByName(string name, out PartyLeaderSnapshot snapshot)
    {
        snapshot = PartyLeaderSnapshot.None;

        int hash = HashName20(name);
        if (hash == 0)
        {
            return false;
        }

        for (int i = 0; i < partyMembers.Length; i++)
        {
            ref readonly PartyMemberState state = ref partyMembers[i];
            if (!state.Exists || state.NameHash != hash)
            {
                continue;
            }

            return TryCreatePartySnapshot(state, out snapshot);
        }

        return false;
    }

    public bool IsMeleeSwingingDefault() => IsMeleeSwinging(500);

    public bool IsMeleeSwinging(int extraMarginMs)
    {
        // Only relevant when in melee range
        if (!IsInMeleeRange())
            return false;

        // Check if swing timer shows recent activity
        int swingSpeed = MainHandSpeedMs();
        int elapsed = MainHandSwing.ElapsedMs();

        // Buffer: swing speed + network latency + 250ms margin
        int maxExpectedElapsed = swingSpeed + NetworkLatency + extraMarginMs;

        return elapsed < maxExpectedElapsed;
    }
}