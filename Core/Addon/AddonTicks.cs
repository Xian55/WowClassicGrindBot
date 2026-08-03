namespace Core;

public static class AddonTicks
{
    public const int GLOBAL_QUEUE_UPDATE = 5;

    public const int INIT_PHASE = 2 * GLOBAL_QUEUE_UPDATE;

    public const int BAG_UPDATE = GLOBAL_QUEUE_UPDATE;

    /// <summary>
    /// Marker value for queue count headers.
    /// First item in a queue batch encodes COUNT_MARKER + expectedCount.
    /// Must be above max possible queue data value (texture max = 16,149,999).
    /// </summary>
    public const int QUEUE_COUNT_MARKER = 16_777_000;

    /// <summary>
    /// Terminates the lower-rank half of the spellbook batch.
    /// A count header cannot express that block: a cell tops out at 16,777,215, so
    /// QUEUE_COUNT_MARKER + n saturates at n = 215 and a level 60+ spellbook holds more
    /// ranks. The marker cannot be lowered to make room either - a binding encoding spans
    /// the full 24 bits. This value is only read on the spellbook cell, so it cannot
    /// collide with the binding queue.
    /// </summary>
    public const int SPELLBOOK_ALL_RANKS_END = 16_776_999;
}
