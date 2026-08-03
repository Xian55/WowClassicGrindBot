namespace Core;

/// <summary>
/// Utility methods for extracting data from bit-packed GUIDs.
/// GUID encoding: High 18 bits = NPC ID, Low 6 bits = spawn hash
///
/// <para>
/// Produced by <c>DataToColor:uniqueGuid</c> - a full WoW GUID does not fit
/// through the pixel channel, so the addon packs the entry id with a small
/// spawn hash that keeps two of the same mob distinguishable:
/// <code>
/// -- Addons/DataToColor/Versions.lua
/// return bit.bor(bit.lshift(band(npcId, 0x3FFFF), 6), spawnHash)
/// </code>
/// Legacy and modern clients both go through <c>uniqueGuid</c>, so this decode
/// is version independent. Player GUIDs carry no entry id and pack with
/// <c>npcId = 0</c>, so <see cref="GetNpcId"/> returning 0 means "not a creature".
/// </para>
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
