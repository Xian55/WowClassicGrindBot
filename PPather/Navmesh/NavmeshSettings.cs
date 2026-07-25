using System;
using System.Security.Cryptography;
using System.Text;

using Wmo;

namespace PPather.Navmesh;

/// <summary>
/// Compile-time recast bake constants for the WoW navmesh engine.
///
/// The baseline mirrors TrinityCore 3.3.5 mmaps_generator defaults
/// (bigBaseUnit=false) so the runtime filter semantics proven by
/// AmeisenNavigation transfer 1:1. The agent tunables that deliberately deviate
/// (radius, climb, slope) now live in <see cref="SharedLib.NavmeshBakeOptions"/>
/// so they can be configured; they are fed into <see cref="ComputeSettingsHash"/>
/// as parameters. Any change that alters produced mesh bytes must bump
/// <see cref="FormatVersion"/>.
/// </summary>
public sealed class NavmeshSettings
{
    /// <summary>Bump when serialized tile bytes change (params, DotRecast fork, areas).</summary>
    public const int FormatVersion = 1;

    // --- Voxel grid -----------------------------------------------------

    /// <summary>Cell size == cell height (isotropic voxels), yards.</summary>
    public const float CellSize = 0.26666667f;
    public const float CellHeight = 0.26666667f;

    /// <summary>Detour tile edge, yards: 1/4 ADT => 16 tiles per ADT.</summary>
    public const float TileWorldSize = 133.33333f;

    /// <summary>Tile edge in cells (TileWorldSize / CellSize).</summary>
    public const int TileSizeCells = 500;

    // --- Agent (toon) ---------------------------------------------------

    public const float AgentHeight = 1.6f;

    // Agent radius / climb / slope are configurable and live in
    // SharedLib.NavmeshBakeOptions - they are passed into ComputeSettingsHash
    // and the bake, not read from a static here.

    // --- Region/contour/detail ------------------------------------------

    /// <summary>TrinityCore minRegionArea, in cells^2.</summary>
    public const float MinRegionAreaCells = 3600f;

    /// <summary>TrinityCore mergeRegionArea, in cells^2.</summary>
    public const float MergeRegionAreaCells = 2500f;

    /// <summary>TrinityCore maxEdgeLen, in cells.</summary>
    public const float MaxEdgeLenCells = 81f;

    /// <summary>minRegionArea in world units.</summary>
    public const float MinRegionAreaWorld = MinRegionAreaCells * CellSize * CellSize;

    /// <summary>mergeRegionArea in world units.</summary>
    public const float MergeRegionAreaWorld = MergeRegionAreaCells * CellSize * CellSize;

    /// <summary>maxEdgeLen in world units.</summary>
    public const float MaxEdgeLenWorld = MaxEdgeLenCells * CellSize;

    public const float MaxSimplificationError = 1.8f;

    /// <summary>Multiplier over CellSize (RcConfig semantics): cs*16 ~ 4.27yd.</summary>
    public const float DetailSampleDistFactor = 16f;

    /// <summary>Multiplier over CellHeight (RcConfig semantics).</summary>
    public const float DetailSampleMaxErrorFactor = 1f;

    public const int VertsPerPoly = 6;

    // --- Areas & poly flags (TrinityCore NavArea/NavTerrainFlag values) --

    public const int AreaMagmaSlime = 8;
    public const int AreaWater = 9;
    /// <summary>Highest area value wins on span merge: shallow water stays walkable ground.</summary>
    public const int AreaGround = 11;

    public const int FlagGround = 0x01;
    public const int FlagGroundSteep = 0x02; // reserved, no steep band is baked
    public const int FlagWater = 0x04;
    public const int FlagMagmaSlime = 0x08;

    // --- Detour runtime -------------------------------------------------

    /// <summary>
    /// World span in tiles per side, derived from <see cref="TileWorldSize"/>.
    ///
    /// Derived rather than hard-coded because <see cref="NavmeshCoords.IsValidTile"/>
    /// bounds every tile request against it and <see cref="NavmeshTileCache"/>
    /// drops out-of-range requests silently. A constant tuned for one tile size
    /// turns any smaller tile into "most of the world quietly has no navmesh":
    /// at 66yd tiles the world needs 512 a side, at 33yd it needs 1024.
    /// </summary>
    public static readonly int TilesPerSide =
        (int)MathF.Ceiling((2f * ChunkReader.ZEROPOINT) / TileWorldSize);

    /// <summary>
    /// Detour's tile pool size - how many tiles may be *resident* at once, not
    /// how many exist in the world. Tiles are looked up by hashed (x, z), so
    /// this is unrelated to <see cref="TilesPerSide"/>; we hold hundreds.
    /// </summary>
    public const int MaxTiles = 1 << 16;
    public const int MaxPolysPerTile = 1 << 20;
    public const int MaxSearchNodes = 65535;

    /// <summary>
    /// Groups clients whose world geometry is the same, so one bake serves all
    /// of them instead of one per expansion.
    ///
    /// Vanilla through WotLK read the same MPQ-era continents: the zones TBC
    /// and WotLK add (Quel'Thalas and the Draenei isles sit on the vanilla
    /// continent maps) are extra tiles rather than edits to existing ones, and
    /// a client that has no geometry for a tile simply never bakes it - the
    /// "this tile is empty" marker is per-session, never written to disk, so it
    /// cannot leak from one client to another. Cataclysm rewrote the old world,
    /// which is also where the storage changes to CASC, so it starts a new era.
    ///
    /// An unrecognised client gets an era of its own: silently handing it
    /// another client's mesh is the one failure that would be hard to notice.
    /// </summary>
    public static string MeshEra(string clientExpansion) => DataConfig.ClientEra(clientExpansion);

    /// <summary>
    /// Leading SHA-256 bytes kept as the cache-dir discriminator. 4 bytes (8 hex
    /// chars) is ample to separate the handful of live bake configurations.
    /// </summary>
    public const int HashPrefixBytes = 4;

    /// <summary>
    /// Cache directory discriminator: same inputs => same tiles.
    /// Combines bake constants, format version, the configurable agent values
    /// (from <see cref="SharedLib.NavmeshBakeOptions"/>), the client's geometry
    /// era (see <see cref="MeshEra"/>) and the DotRecast fork commit
    /// (informational version).
    /// </summary>
    public static string ComputeSettingsHash(string clientExpansion, string dotRecastVersion,
        float agentRadius, float agentMaxClimb, float walkableSlope, float? minWorldZ = null)
    {
        StringBuilder sb = new();
        sb.Append(FormatVersion).Append('|')
          .Append(CellSize).Append('|')
          .Append(CellHeight).Append('|')
          .Append(TileWorldSize).Append('|')
          .Append(TileSizeCells).Append('|')
          .Append(AgentHeight).Append('|')
          .Append(agentRadius).Append('|')
          .Append(agentMaxClimb).Append('|')
          .Append(walkableSlope).Append('|')
          .Append(MinRegionAreaWorld).Append('|')
          .Append(MergeRegionAreaWorld).Append('|')
          .Append(MaxEdgeLenWorld).Append('|')
          .Append(MaxSimplificationError).Append('|')
          .Append(DetailSampleDistFactor).Append('|')
          .Append(DetailSampleMaxErrorFactor).Append('|')
          .Append(VertsPerPoly).Append('|')
          .Append(clientExpansion).Append('|')
          .Append(dotRecastVersion);

        // Appended only when set, so the default (no floor) reproduces the
        // pre-existing hash and its baked tiles stay valid.
        if (minWorldZ.HasValue)
        {
            sb.Append("|minZ").Append(minWorldZ.Value);
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash.AsSpan(0, HashPrefixBytes)).ToLowerInvariant();
    }
}
