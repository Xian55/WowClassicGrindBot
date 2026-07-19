#nullable enable
using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Recast;

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
    public static DtMeshData? Bake(TileGeometry geom, int dtTileX, int dtTileZ)
    {
        if (geom.IsEmpty)
        {
            return null;
        }

        RcConfig cfg = new(
            useTiles: true,
            NavmeshSettings.TileSizeCells, NavmeshSettings.TileSizeCells,
            RcConfig.CalcBorder(NavmeshSettings.AgentRadius, NavmeshSettings.CellSize),
            RcPartition.WATERSHED,
            NavmeshSettings.CellSize, NavmeshSettings.CellHeight,
            NavmeshSettings.WalkableSlopeAngle,
            NavmeshSettings.AgentHeight, NavmeshSettings.AgentRadius, NavmeshSettings.AgentMaxClimb,
            NavmeshSettings.MinRegionAreaWorld, NavmeshSettings.MergeRegionAreaWorld,
            NavmeshSettings.MaxEdgeLenWorld, NavmeshSettings.MaxSimplificationError,
            NavmeshSettings.VertsPerPoly,
            NavmeshSettings.DetailSampleDistFactor, NavmeshSettings.DetailSampleMaxErrorFactor,
            filterLowHangingObstacles: true, filterLedgeSpans: true, filterWalkableLowHeightSpans: true,
            new RcAreaModification(NavmeshSettings.AreaGround),
            buildMeshDetail: true);

        RcBuilderConfig bcfg = new(cfg, geom.BMin, geom.BMax, 0, 0);
        RcContext ctx = new();

        RcHeightfield solid = new(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax,
            cfg.Cs, cfg.Ch, cfg.BorderSize);

        // Ground: slope decides walkability (area GROUND or null).
        int groundTriCount = geom.GroundTriangleCount;
        int[] groundAreas = RcRecast.MarkWalkableTriangles(ctx, cfg.WalkableSlopeAngle,
            geom.GroundVerts, geom.GroundTris, groundTriCount, cfg.WalkableAreaMod);
        RcRasterizations.RasterizeTriangles(ctx, geom.GroundVerts, geom.GroundTris,
            groundAreas, groundTriCount, solid, cfg.WalkableClimb);

        RcFilters.FilterLowHangingWalkableObstacles(ctx, cfg.WalkableClimb, solid);
        RcFilters.FilterLedgeSpans(ctx, cfg.WalkableHeight, cfg.WalkableClimb, solid);
        RcFilters.FilterWalkableLowHeightSpans(ctx, cfg.WalkableHeight, solid);

        // Liquids after the ground filters (TrinityCore order) with fixed areas.
        int liquidTriCount = geom.LiquidTriangleCount;
        if (liquidTriCount > 0)
        {
            RcRasterizations.RasterizeTriangles(ctx, geom.LiquidVerts, geom.LiquidTris,
                geom.LiquidAreas, liquidTriCount, solid, cfg.WalkableClimb);
        }

        RcCompactHeightfield chf = RcCompacts.BuildCompactHeightfield(ctx,
            cfg.WalkableHeight, cfg.WalkableClimb, solid);

        RcAreas.ErodeWalkableArea(ctx, cfg.WalkableRadius, chf);
        RcAreas.MedianFilterWalkableArea(ctx, chf);

        RcRegions.BuildDistanceField(ctx, chf);
        RcRegions.BuildRegions(ctx, chf, cfg.MinRegionArea, cfg.MergeRegionArea);

        RcContourSet cset = RcContours.BuildContours(ctx, chf,
            cfg.MaxSimplificationError, cfg.MaxEdgeLen,
            RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);

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

        RcPolyMeshDetail dmesh = RcMeshDetails.BuildPolyMeshDetail(ctx, pmesh, chf,
            cfg.DetailSampleDist, cfg.DetailSampleMaxError);

        DtNavMeshCreateParams option = new()
        {
            verts = pmesh.verts,
            vertCount = pmesh.nverts,
            polys = pmesh.polys,
            polyAreas = pmesh.areas,
            polyFlags = pmesh.flags,
            polyCount = pmesh.npolys,
            nvp = pmesh.nvp,
            detailMeshes = dmesh.meshes,
            detailVerts = dmesh.verts,
            detailVertsCount = dmesh.nverts,
            detailTris = dmesh.tris,
            detailTriCount = dmesh.ntris,
            walkableHeight = NavmeshSettings.AgentHeight,
            walkableRadius = NavmeshSettings.AgentRadius,
            walkableClimb = NavmeshSettings.AgentMaxClimb,
            bmin = pmesh.bmin,
            bmax = pmesh.bmax,
            cs = cfg.Cs,
            ch = cfg.Ch,
            buildBvTree = true,
            tileX = dtTileX,
            tileZ = dtTileZ,
        };

        return DtNavMeshBuilder.CreateNavMeshData(option);
    }
}
