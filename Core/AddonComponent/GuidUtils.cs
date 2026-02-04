namespace Core;

/// <summary>
/// Utility methods for extracting data from bit-packed GUIDs.
/// GUID encoding: High 18 bits = NPC ID, Low 6 bits = spawn hash
/// </summary>
public static class GuidUtils
{
    private const int NPC_ID_SHIFT = 6;
    private const int SPAWN_HASH_MASK = 0x3F;

    /// <summary>
    /// Extract NPC ID from packed GUID (high 18 bits).
    /// </summary>
    public static int GetNpcId(int packedGuid) => packedGuid >> NPC_ID_SHIFT;

    /// <summary>
    /// Extract spawn hash from packed GUID (low 6 bits).
    /// </summary>
    public static int GetSpawnHash(int packedGuid) => packedGuid & SPAWN_HASH_MASK;
}
