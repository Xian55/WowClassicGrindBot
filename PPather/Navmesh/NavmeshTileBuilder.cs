#nullable enable
using System;

using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Recast;

using SharedLib;

namespace PPather.Navmesh;

/// <summary>
/// Bakes one Detour tile from extracted WoW triangle soup.
///
/// Mirrors the TrinityCore mmaps pipeline: rasterize ground (slope-marked at
/// 55 deg) -> low-hanging/ledge/low-height filters -> rasterize liquids with
/// their areas -> compact -> erode by agent radius -> median filter ->
/// watershed regions -> contours -> polymesh + detail -> DtMeshData.
///
/// Liquid spans merge with the max-area rule, and GROUND(11) > WATER(9), so
/// shallow water over walkable terrain stays ground - swim-vs-walk is a
/// runtime filter decision, not baked geometry.
/// </summary>
public static class NavmeshTileBuilder
{
    public static DtMeshData? Bake(TileGeometry geom, int dtTileX, int dtTileZ,
        NavmeshBakeOptions? bake = null)
    {
        return Bake(geom, dtTileX, dtTileZ, new RcContext(), CreateConfig(bake), bake);
    }

    /// <summary>
    /// Bakes with a caller-supplied context so profiling harnesses can read the
    /// per-stage timings recast records into it (<see cref="RcContext.ToList"/>).
    /// </summary>
    public static DtMeshData? Bake(TileGeometry geom, int dtTileX, int dtTileZ, RcContext ctx,
        NavmeshBakeOptions? bake = null)
    {
        return Bake(geom, dtTileX, dtTileZ, ctx, CreateConfig(bake), bake);
    }

    /// <summary>The shipping configuration - TrinityCore-fidelity parameters.</summary>
    public static RcConfig CreateConfig(NavmeshBakeOptions? bake = null)
    {
        return CreateConfig(NavmeshSettings.CellSize, RcPartition.WATERSHED, buildMeshDetail: true, bake);
    }

    /// <summary>
    /// Variant config for speed-vs-quality sweeps. Tile world size is fixed, so
    /// the cell count per tile follows from the cell size.
    /// </summary>
    public static RcConfig CreateConfig(float cellSize, RcPartition partition, bool buildMeshDetail,
        NavmeshBakeOptions? bake = null)
    {
        bake ??= new NavmeshBakeOptions();

        int tileSizeCells = (int)MathF.Round(NavmeshSettings.TileWorldSize / cellSize);

        return new RcConfig(
            useTiles: true,
            tileSizeCells, tileSizeCells,
            RcConfig.CalcBorder(bake.AgentRadius, cellSize),
            partition,
            cellSize, cellSize,
            bake.WalkableSlope,
            NavmeshSettings.AgentHeight, bake.AgentRadius, bake.AgentMaxClimb,
            NavmeshSettings.MinRegionAreaWorld, NavmeshSettings.MergeRegionAreaWorld,
            NavmeshSettings.MaxEdgeLenWorld, NavmeshSettings.MaxSimplificationError,
            NavmeshSettings.VertsPerPoly,
            NavmeshSettings.DetailSampleDistFactor, NavmeshSettings.DetailSampleMaxErrorFactor,
            filterLowHangingObstacles: true, filterLedgeSpans: true, filterWalkableLowHeightSpans: true,
            new RcAreaModification(NavmeshSettings.AreaGround),
            buildMeshDetail);
    }

    /// <summary>
    /// Optional per-stage allocation probe. Null in normal operation; the bake
    /// profiler sets it to attribute gen0 churn to pipeline stages, which a
    /// heap snapshot cannot show because the short-lived objects are already
    /// collected by the time it runs.
    /// </summary>
    public static Action<string, long>? StageAllocProbe { get; set; }

    private static long ProbeMark()
    {
        return StageAllocProbe is null ? 0 : GC.GetTotalAllocatedBytes(precise: false);
    }

    private static long ProbeReport(string stage, long mark)
    {
        if (StageAllocProbe is null)
        {
            return 0;
        }

        long now = GC.GetTotalAllocatedBytes(precise: false);
        StageAllocProbe(stage, now - mark);
        return now;
    }

    public static DtMeshData? Bake(TileGeometry geom, int dtTileX, int dtTileZ, RcContext ctx, RcConfig cfg,
        NavmeshBakeOptions? bake = null)
    {
        bake ??= new NavmeshBakeOptions();

        if (geom.IsEmpty)
        {
            return null;
        }

        RcBuilderConfig bcfg = new(cfg, geom.BMin, geom.BMax, 0, 0);

        RcHeightfield solid = new(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax,
            cfg.Cs, cfg.Ch, cfg.BorderSize);

        // Ground: slope decides walkability (area GROUND or null).
        long probe = ProbeMark();

        int groundTriCount = geom.GroundTriangleCount;
        int[] groundAreas = RcRecast.MarkWalkableTriangles(ctx, cfg.WalkableSlopeAngle,
            geom.GroundVerts, geom.GroundTris, groundTriCount, cfg.WalkableAreaMod);
        RcRasterizations.RasterizeTriangles(ctx, geom.GroundVerts, geom.GroundTris,
            groundAreas, groundTriCount, solid, cfg.WalkableClimb);

        probe = ProbeReport("rasterize+mark", probe);

        RcFilters.FilterLowHangingWalkableObstacles(ctx, cfg.WalkableClimb, solid);
        RcFilters.FilterLedgeSpans(ctx, cfg.WalkableHeight, cfg.WalkableClimb, solid);
        RcFilters.FilterWalkableLowHeightSpans(ctx, cfg.WalkableHeight, solid);

        probe = ProbeReport("filters", probe);

        // Liquids after the ground filters (TrinityCore order) with fixed areas.
        int liquidTriCount = geom.LiquidTriangleCount;
        if (liquidTriCount > 0)
        {
            RcRasterizations.RasterizeTriangles(ctx, geom.LiquidVerts, geom.LiquidTris,
                geom.LiquidAreas, liquidTriCount, solid, cfg.WalkableClimb);
        }

        probe = ProbeReport("rasterize liquid", probe);

        RcCompactHeightfield chf = RcCompacts.BuildCompactHeightfield(ctx,
            cfg.WalkableHeight, cfg.WalkableClimb, solid);

        probe = ProbeReport("compact heightfield", probe);

        RcAreas.ErodeWalkableArea(ctx, cfg.WalkableRadius, chf);
        RcAreas.MedianFilterWalkableArea(ctx, chf);

        probe = ProbeReport("erode+median", probe);

        RcRegions.BuildDistanceField(ctx, chf);
        probe = ProbeReport("distance field", probe);

        RcRegions.BuildRegions(ctx, chf, cfg.MinRegionArea, cfg.MergeRegionArea);

        probe = ProbeReport("regions", probe);

        RcContourSet cset = RcContours.BuildContours(ctx, chf,
            cfg.MaxSimplificationError, cfg.MaxEdgeLen,
            RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);

        probe = ProbeReport("contours", probe);

        RcPolyMesh pmesh = RcMeshs.BuildPolyMesh(ctx, cset, cfg.MaxVertsPerPoly);
        if (pmesh.npolys == 0)
        {
            return null;
        }

        for (int i = 0; i < pmesh.npolys; i++)
        {
            pmesh.flags[i] = pmesh.areas[i] switch
            {
                NavmeshSettings.AreaWater => NavmeshSettings.FlagWater,
                NavmeshSettings.AreaMagmaSlime => NavmeshSettings.FlagMagmaSlime,
                _ => NavmeshSettings.FlagGround,
            };
        }

        // Detail is optional so speed-vs-quality sweeps can price it; Detour
        // falls back to flat per-poly detail when the arrays are absent.
        probe = ProbeReport("polymesh", probe);

        RcPolyMeshDetail? dmesh = cfg.BuildMeshDetail
            ? RcMeshDetails.BuildPolyMeshDetail(ctx, pmesh, chf,
                cfg.DetailSampleDist, cfg.DetailSampleMaxError)
            : null;

        probe = ProbeReport("detail mesh", probe);

        // Last consumer of the compact heightfield. Returns its four bulk
        // arrays - ~10MB per tile - to the shared pool; the meshes below own
        // their own storage and do not alias them.
        chf.Dispose();

        DtNavMeshCreateParams option = new()
        {
            verts = pmesh.verts,
            vertCount = pmesh.nverts,
            polys = pmesh.polys,
            polyAreas = pmesh.areas,
            polyFlags = pmesh.flags,
            polyCount = pmesh.npolys,
            nvp = pmesh.nvp,
            detailMeshes = dmesh?.meshes,
            detailVerts = dmesh?.verts,
            detailVertsCount = dmesh?.nverts ?? 0,
            detailTris = dmesh?.tris,
            detailTriCount = dmesh?.ntris ?? 0,
            walkableHeight = NavmeshSettings.AgentHeight,
            walkableRadius = bake.AgentRadius,
            walkableClimb = bake.AgentMaxClimb,
            bmin = pmesh.bmin,
            bmax = pmesh.bmax,
            cs = cfg.Cs,
            ch = cfg.Ch,
            buildBvTree = true,
            tileX = dtTileX,
            tileZ = dtTileZ,
        };

        DtMeshData? result = DtNavMeshBuilder.CreateNavMeshData(option);
        ProbeReport("create tile", probe);
        return result;
    }
}
