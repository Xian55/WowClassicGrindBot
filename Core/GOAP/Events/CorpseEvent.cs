using System;
using System.Numerics;

namespace Core.GOAP;

public sealed class CorpseEvent : GoapEventArgs
{
    public const string NAME = "Corpse";
    public const string COLOR = "black";

    public Vector3 MapLoc { get; }
    public float Radius { get; }

    public float PlayerFacing { get; }

    public Vector3 PlayerLocation { get; }

    /// <summary>
    /// Bit-packed GUID containing NPC ID (high 18 bits) and spawn hash (low 6 bits).
    /// Use GuidUtils.GetNpcId() to extract the NPC ID.
    /// </summary>
    public int PackedGuid { get; }

    public CorpseEvent(Vector3 location, float radius, float playerFacing, Vector3 playerLocation, int packedGuid)
    {
        MapLoc = location;
        Radius = MathF.Max(1, radius);
        PlayerFacing = playerFacing;
        PlayerLocation = playerLocation;
        PackedGuid = packedGuid;
    }
}
