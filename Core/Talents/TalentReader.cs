using Core.Database;
using Core.Talents;

using System.Collections.Generic;

namespace Core;

public sealed class TalentReader : IReader
{
    private const int cTalent = 72;

    private readonly PlayerReader playerReader;
    private readonly TalentDB talentDB;
    public int Count { get; private set; }

    public Dictionary<int, Talent> Talents { get; } = new();
    public Dictionary<int, int> Spells { get; } = new();

    // A batch arrives one hash per addon tick, so it is staged and swapped in whole.
    // Clearing Talents up front instead would leave it empty or half filled for as
    // many frames as the batch is long, and SPELLS_CHANGED resends one on every
    // spell learnt - a Talent requirement would flip false mid-combat.
    private readonly Dictionary<int, Talent> pendingTalents = new();
    private readonly Dictionary<int, int> pendingSpells = new();
    private int pendingCount;

    private int expectedCount = -1;
    private int receivedCount;

    public int ExpectedCount => expectedCount;
    public int ReceivedCount => receivedCount;
    public bool IsInitialized => expectedCount >= 0 && receivedCount >= expectedCount;

    public TalentReader(PlayerReader playerReader, TalentDB talentDB)
    {
        this.playerReader = playerReader;
        this.talentDB = talentDB;
    }

    public void Update(IAddonDataProvider reader)
    {
        int hash = reader.GetInt(cTalent);
        if (hash == 0) return;

        // Batch header. What follows is the complete talent set, which is what makes
        // a respec resolvable: it picks different talents and the hashes of the
        // previous ones are simply never sent again, so nothing else retires them.
        if (hash >= AddonTicks.QUEUE_COUNT_MARKER)
        {
            expectedCount = hash - AddonTicks.QUEUE_COUNT_MARKER;
            receivedCount = 0;

            pendingTalents.Clear();
            pendingSpells.Clear();
            pendingCount = 0;

            // Unlearning everything sends a header and nothing else.
            if (expectedCount == 0)
                Commit();

            return;
        }

        receivedCount++;

        //           1-3 +         1-11 +         1-4 +         1-5
        // tab * 1000000 + tier * 10000 + column * 10 + currentRank
        int tab = hash / 1000000;
        int tier = hash / 10000 % 100;
        int column = hash / 10 % 10;
        int rank = hash % 10;

        Talent talent = new()
        {
            Hash = hash,
            TabNum = tab,
            TierNum = tier,
            ColumnNum = column,
            CurrentRank = rank
        };

        // An addon older than the batch header never sends one, so keep the
        // accumulating behaviour rather than showing nothing at all.
        if (expectedCount < 0)
        {
            if (!Talents.ContainsKey(hash) &&
                talentDB.Update(ref talent, playerReader.Class, out int legacyId))
            {
                Talents.Add(hash, talent);
                Spells.Add(hash, legacyId);
                Count += talent.CurrentRank;
            }

            return;
        }

        if (!pendingTalents.ContainsKey(hash) &&
            talentDB.Update(ref talent, playerReader.Class, out int id))
        {
            pendingTalents.Add(hash, talent);
            pendingSpells.Add(hash, id);
            pendingCount += talent.CurrentRank;
        }

        if (receivedCount >= expectedCount)
            Commit();
    }

    /// <summary>
    /// Swaps the staged batch in. An incomplete batch - a dropped pixel, a reload
    /// mid-flight - simply never commits and the previous set stays live.
    /// </summary>
    private void Commit()
    {
        Talents.Clear();
        Spells.Clear();

        foreach ((int hash, Talent talent) in pendingTalents)
            Talents.Add(hash, talent);

        foreach ((int hash, int spellId) in pendingSpells)
            Spells.Add(hash, spellId);

        Count = pendingCount;
    }

    public void Reset()
    {
        Count = 0;
        Talents.Clear();
        Spells.Clear();

        expectedCount = -1;
        receivedCount = 0;

        pendingCount = 0;
        pendingTalents.Clear();
        pendingSpells.Clear();
    }

    public bool HasTalent(string name, int rank)
    {
        foreach ((int _, Talent t) in Talents)
        {
            if (t.CurrentRank >= rank &&
                t.Name.Contains(name, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
