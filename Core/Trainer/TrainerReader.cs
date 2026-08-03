using System.Collections.Generic;

namespace Core;

/// <summary>
/// Decodes what the addon's Trainer.lua reports after a class trainer visit.
/// <para>
/// A visit always ends with a batch header and the ids that were bought, even when that
/// is none - the header is the only completion signal, so a goal waiting on it cannot
/// hang on a trainer that had nothing to sell. The reason sentinels arrive first and say
/// why the batch is empty.
/// </para>
/// </summary>
public sealed class TrainerReader : IReader
{
    private const int cTrainer = 115;

    // Both sit above any spell id and below QUEUE_COUNT_MARKER, and are only ever read
    // on this cell - the binding queue's encoding does span this range, on its own cell.
    private const int TRAINER_NO_MATCH = 16_776_001;
    private const int TRAINER_NO_MONEY = 16_776_002;

    private readonly List<int> pending = [];
    private readonly List<int> bought = [];

    private int expectedCount = -1;
    private int receivedCount;

    /// <summary>Spell ids bought during the last completed visit.</summary>
    public IReadOnlyList<int> Bought => bought;

    /// <summary>The trainer offered nothing on the whitelist.</summary>
    public bool NoMatch { get; private set; }

    /// <summary>A whitelisted service was offered but could not be afforded.</summary>
    public bool NoMoney { get; private set; }

    /// <summary>A batch arrived, so the visit is over however it went.</summary>
    public bool Completed { get; private set; }

    public void Update(IAddonDataProvider reader)
    {
        int value = reader.GetInt(cTrainer);
        if (value == 0)
            return;

        if (value == TRAINER_NO_MATCH)
        {
            NoMatch = true;
            return;
        }

        if (value == TRAINER_NO_MONEY)
        {
            NoMoney = true;
            return;
        }

        if (value >= AddonTicks.QUEUE_COUNT_MARKER)
        {
            expectedCount = value - AddonTicks.QUEUE_COUNT_MARKER;
            receivedCount = 0;
            pending.Clear();

            // Bought nothing: the header is the whole batch.
            if (expectedCount == 0)
                Commit();

            return;
        }

        // A stray id with no header in front of it - a value left over from before a
        // Reset, or the tail of a batch whose header was missed. Either way it belongs
        // to no visit.
        if (expectedCount < 0)
            return;

        // The same value can be captured on two consecutive frames; counting it twice
        // would satisfy the header early and drop the ids that had not arrived yet.
        if (pending.Contains(value))
            return;

        receivedCount++;
        pending.Add(value);

        if (receivedCount >= expectedCount)
            Commit();
    }

    /// <summary>
    /// Swaps the staged batch in. An incomplete one - a dropped frame, a reload
    /// mid-visit - simply never commits and <see cref="Completed"/> stays false.
    /// </summary>
    private void Commit()
    {
        bought.Clear();
        bought.AddRange(pending);

        expectedCount = -1;
        receivedCount = 0;
        Completed = true;
    }

    /// <summary>
    /// Called before a visit so the previous one's verdict cannot be mistaken for it.
    /// </summary>
    public void Reset()
    {
        pending.Clear();
        bought.Clear();

        expectedCount = -1;
        receivedCount = 0;

        NoMatch = false;
        NoMoney = false;
        Completed = false;
    }
}
