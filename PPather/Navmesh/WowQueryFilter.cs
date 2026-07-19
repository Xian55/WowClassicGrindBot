#nullable enable
using System.Numerics;

using DotRecast.Detour;

namespace PPather.Navmesh;

/// <summary>
/// Query filter with TrinityCore/AmeisenNavigation semantics:
/// include GROUND | WATER | MAGMA_SLIME, area costs GROUND 1.0 / WATER 1.3 /
/// MAGMA_SLIME 4.0. Road preference and danger zones hook in here later via
/// the cost table.
/// </summary>
public sealed class WowQueryFilter : IDtQueryFilter
{
    public const int IncludeFlags =
        NavmeshSettings.FlagGround | NavmeshSettings.FlagWater | NavmeshSettings.FlagMagmaSlime;

    public const float CostGround = 1.0f;
    public const float CostWater = 1.3f;
    public const float CostMagmaSlime = 4.0f;

    private readonly float[] areaCost;

    public WowQueryFilter()
    {
        areaCost = new float[64];
        for (int i = 0; i < areaCost.Length; i++)
        {
            areaCost[i] = CostGround;
        }

        areaCost[NavmeshSettings.AreaWater] = CostWater;
        areaCost[NavmeshSettings.AreaMagmaSlime] = CostMagmaSlime;
    }

    public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly)
    {
        return (poly.flags & IncludeFlags) != 0;
    }

    public float GetCost(Vector3 pa, Vector3 pb,
        long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
        long curRef, DtMeshTile curTile, DtPoly curPoly,
        long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
    {
        return Vector3.Distance(pa, pb) * areaCost[curPoly.GetArea()];
    }
}
