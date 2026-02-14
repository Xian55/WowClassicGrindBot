using System;
using System.Collections.Frozen;
using System.Linq;

namespace SharedLib.Data;

[Flags]
public enum NpcFlags : uint
{
    None = 0,

    // Interaction
    Gossip = 1 << 0,              // 0x00000001
    QuestGiver = 1 << 1,          // 0x00000002
    Spellclick = 1 << 24,         // 0x01000000
    PlayerVehicle = 1 << 25,      // 0x02000000
    Mailbox = 1 << 26,            // 0x04000000

    // Trainers
    Trainer = 1 << 4,             // 0x00000010
    ClassTrainer = 1 << 5,        // 0x00000020
    ProfessionTrainer = 1 << 6,   // 0x00000040

    // Vendors
    Vendor = 1 << 7,              // 0x00000080
    VendorAmmo = 1 << 8,          // 0x00000100
    VendorFood = 1 << 9,          // 0x00000200
    VendorPoison = 1 << 10,       // 0x00000400
    VendorReagent = 1 << 11,      // 0x00000800
    Repair = 1 << 12,             // 0x00001000

    // Services
    FlightMaster = 1 << 13,       // 0x00002000
    SpiritHealer = 1 << 14,       // 0x00004000
    SpiritGuide = 1 << 15,        // 0x00008000
    Innkeeper = 1 << 16,          // 0x00010000
    Banker = 1 << 17,             // 0x00020000
    GuildBanker = 1 << 23,        // 0x00800000
    TabardDesigner = 1 << 19,     // 0x00080000
    StableMaster = 1 << 22,       // 0x00400000
    Auctioneer = 1 << 21,         // 0x00200000
    Battlemaster = 1 << 20,       // 0x00100000
    Petitioner = 1 << 18,         // 0x00040000

    // Special
    ArtifactPowerRespec = 1 << 27, // 0x08000000
    Transmogrifier = 1 << 28,      // 0x10000000
    Vaultkeeper = 1 << 29,         // 0x20000000
    WildBattlePet = 1 << 30,       // 0x40000000
    BlackMarket = 1u << 31         // 0x80000000
}

public static class NpcFlagsExtensions
{
    private static readonly FrozenDictionary<string, uint> s_flagsDictionary =
        Enum.GetValues<NpcFlags>()
            .Where(f => f != NpcFlags.None)
            .ToFrozenDictionary(
                f => f.ToString().ToLowerInvariant(),
                f => (uint)f);

    public static FrozenDictionary<string, uint> ToDictionary() => s_flagsDictionary;

    public static string ToStringF(this NpcFlags flags) => flags switch
    {
        NpcFlags.None => nameof(NpcFlags.None),
        NpcFlags.Gossip => nameof(NpcFlags.Gossip),
        NpcFlags.QuestGiver => nameof(NpcFlags.QuestGiver),
        NpcFlags.Spellclick => nameof(NpcFlags.Spellclick),
        NpcFlags.PlayerVehicle => nameof(NpcFlags.PlayerVehicle),
        NpcFlags.Mailbox => nameof(NpcFlags.Mailbox),
        NpcFlags.Trainer => nameof(NpcFlags.Trainer),
        NpcFlags.ClassTrainer => nameof(NpcFlags.ClassTrainer),
        NpcFlags.ProfessionTrainer => nameof(NpcFlags.ProfessionTrainer),
        NpcFlags.Vendor => nameof(NpcFlags.Vendor),
        NpcFlags.VendorAmmo => nameof(NpcFlags.VendorAmmo),
        NpcFlags.VendorFood => nameof(NpcFlags.VendorFood),
        NpcFlags.VendorPoison => nameof(NpcFlags.VendorPoison),
        NpcFlags.VendorReagent => nameof(NpcFlags.VendorReagent),
        NpcFlags.Repair => nameof(NpcFlags.Repair),
        NpcFlags.FlightMaster => nameof(NpcFlags.FlightMaster),
        NpcFlags.SpiritHealer => nameof(NpcFlags.SpiritHealer),
        NpcFlags.SpiritGuide => nameof(NpcFlags.SpiritGuide),
        NpcFlags.Innkeeper => nameof(NpcFlags.Innkeeper),
        NpcFlags.Banker => nameof(NpcFlags.Banker),
        NpcFlags.GuildBanker => nameof(NpcFlags.GuildBanker),
        NpcFlags.TabardDesigner => nameof(NpcFlags.TabardDesigner),
        NpcFlags.StableMaster => nameof(NpcFlags.StableMaster),
        NpcFlags.Auctioneer => nameof(NpcFlags.Auctioneer),
        NpcFlags.Battlemaster => nameof(NpcFlags.Battlemaster),
        NpcFlags.Petitioner => nameof(NpcFlags.Petitioner),
        NpcFlags.ArtifactPowerRespec => nameof(NpcFlags.ArtifactPowerRespec),
        NpcFlags.Transmogrifier => nameof(NpcFlags.Transmogrifier),
        NpcFlags.Vaultkeeper => nameof(NpcFlags.Vaultkeeper),
        NpcFlags.WildBattlePet => nameof(NpcFlags.WildBattlePet),
        NpcFlags.BlackMarket => nameof(NpcFlags.BlackMarket),
        _ => throw new ArgumentOutOfRangeException(nameof(flags), flags, null)
    };

    public static bool Has(this NpcFlags flags, NpcFlags flag)
    {
        return (flags & flag) == flag;
    }
}