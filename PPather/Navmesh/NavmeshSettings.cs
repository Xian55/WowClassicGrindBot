using System;
using System.Security.Cryptography;
using System.Text;

namespace PPather.Navmesh;

/// <summary>
/// All recast bake parameters for the WoW navmesh engine.
///
/// Values mirror TrinityCore 3.3.5 mmaps_generator defaults (bigBaseUnit=false)
/// so the runtime filter semantics proven by AmeisenNavigation transfer 1:1.
/// Any change that alters produced mesh bytes must bump <see cref="FormatVersion"/>.
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
    public const float AgentRadius = 0.533f;
    /// <summary>1.6yd: walks over fences/small steps like the WoW client.</summary>
    public const float AgentMaxClimb = 1.6f;
    public const float WalkableSlopeAngle = 55f;

    // --- Region/contour/detail ------------------------------------------

    /// <summary>3600 cells^2 (TC minRegionArea) in world units.</summary>
    public const float MinRegionAreaWorld = 3600f * CellSize * CellSize;

    /// <summary>2500 cells^2 (TC mergeRegionArea) in world units.</summary>
    public const float MergeRegionAreaWorld = 2500f * CellSize * CellSize;

    /// <summary>81 cells (TC maxEdgeLen) in world units.</summary>
    public const float MaxEdgeLenWorld = 81f * CellSize;

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

    /// <summary>64 ADT * 4 tiles per side.</summary>
    public const int TilesPerSide = 256;
    public const int MaxTiles = TilesPerSide * TilesPerSide;
    public const int MaxPolysPerTile = 1 << 20;
    public const int MaxSearchNodes = 65535;

    /// <summary>
    /// Cache directory discriminator: same inputs => same tiles.
    /// Combines bake constants, format version, client expansion and the
    /// DotRecast fork commit (informational version).
    /// </summary>
    public static string ComputeSettingsHash(string clientExpansion, string dotRecastVersion)
    {
        StringBuilder sb = new();
        sb.Append(FormatVersion).Append('|')
          .Append(CellSize).Append('|')
          .Append(CellHeight).Append('|')
          .Append(TileWorldSize).Append('|')
          .Append(TileSizeCells).Append('|')
          .Append(AgentHeight).Append('|')
          .Append(AgentRadius).Append('|')
          .Append(AgentMaxClimb).Append('|')
          .Append(WalkableSlopeAngle).Append('|')
          .Append(MinRegionAreaWorld).Append('|')
          .Append(MergeRegionAreaWorld).Append('|')
          .Append(MaxEdgeLenWorld).Append('|')
          .Append(MaxSimplificationError).Append('|')
          .Append(DetailSampleDistFactor).Append('|')
          .Append(DetailSampleMaxErrorFactor).Append('|')
          .Append(VertsPerPoly).Append('|')
          .Append(clientExpansion).Append('|')
          .Append(dotRecastVersion);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }
}
