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
}
