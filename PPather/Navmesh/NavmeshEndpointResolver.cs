#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

using DotRecast.Detour;

namespace PPather.Navmesh;

/// <summary>
/// Resolves a WoW position (whose Z may be missing - the client API exposes
/// none) to a navmesh polygon and a corrected position.
///
/// z != 0: trusted-ish height -> FindNearestPoly with TrinityCore extents
/// (horizontal +-3, vertical +-5, retry +-50).
///
/// z == 0: column scan. A single huge-extent nearest-poly snap (Ameisen's
/// {3,10000,3}) picks the floor closest to z=0, which is wrong in multi-floor
/// WMOs. Instead all polys in a +-2000 vertical column are collected and the
/// floor is picked with the Search.CreateWorldLocation semantics: outdoors
/// prefer the highest surface, indoors prefer an interior floor that has a
/// ceiling above it within headroom distance.
/// </summary>
public sealed class NavmeshEndpointResolver
{
    public const float HorizontalExtent = 3f;
    public const float VerticalExtentNear = 5f;
    public const float VerticalExtentFar = 50f;
    public const float VerticalExtentColumn = 2000f;

    /// <summary>Minimum clear space above a floor for the toon (Search.minHeadroom).</summary>
    public const float MinHeadroom = 2.6f;

    private const int MaxColumnCandidates = 128;

    private readonly DtNavMeshQuery query;
    private readonly IDtQueryFilter filter;

    private readonly ColumnCollector collector = new();
    private readonly List<Candidate> candidates = new(MaxColumnCandidates);

    private readonly record struct Candidate(long PolyRef, Vector3 RcPos);

    public NavmeshEndpointResolver(DtNavMeshQuery query, IDtQueryFilter filter)
    {
        this.query = query;
        this.filter = filter;
    }

    /// <summary>
    /// Height that a column pick is drawn to when the caller knows roughly
    /// which floor it wants - the surface the player was last resolved onto.
    /// A candidate this close to it wins over the geometric rules below.
    /// </summary>
    public const float PreferZToleranceYd = 5f;

    public bool TryResolve(Vector3 wow, bool? startIndoors, out long polyRef, out Vector3 resolvedWow,
        float? preferZ = null)
    {
        polyRef = 0;
        resolvedWow = wow;

        return wow.Z != 0
            ? TryResolveNear(wow, ref polyRef, ref resolvedWow)
            : TryResolveColumn(wow, startIndoors, preferZ, ref polyRef, ref resolvedWow);
    }

    /// <summary>
    /// Every walkable surface stacked in the column around an rc-space point,
    /// nearest-to-<paramref name="preferRcY"/> first. The pathfinder uses this
    /// to re-aim at a destination it could not reach: a position with no
    /// trustworthy height can land on a roof or ledge above the floor the
    /// caller meant, and only connectivity can tell those apart.
    /// </summary>
    public int CollectColumn(Vector3 rcCenter, float preferRcY, List<(long PolyRef, Vector3 RcPos)> into)
    {
        into.Clear();

        collector.Reset();
        DtStatus status = query.QueryPolygons(rcCenter,
            new Vector3(HorizontalExtent, VerticalExtentColumn, HorizontalExtent),
            filter, collector);

        if (!status.Succeeded())
        {
            return 0;
        }

        foreach (long refs in collector.Refs)
        {
            if (query.ClosestPointOnPoly(refs, rcCenter, out Vector3 closest, out _).Succeeded())
            {
                into.Add((refs, closest));
            }
        }

        into.Sort((a, b) =>
            MathF.Abs(a.RcPos.Y - preferRcY).CompareTo(MathF.Abs(b.RcPos.Y - preferRcY)));

        return into.Count;
    }

    private bool TryResolveNear(Vector3 wow, ref long polyRef, ref Vector3 resolvedWow)
    {
        Vector3 rc = NavmeshCoords.ToRc(wow);

        DtStatus status = query.FindNearestPoly(rc,
            new Vector3(HorizontalExtent, VerticalExtentNear, HorizontalExtent),
            filter, out long nearestRef, out Vector3 nearestPt, out _);

        if (!status.Succeeded() || nearestRef == 0)
        {
            status = query.FindNearestPoly(rc,
                new Vector3(HorizontalExtent, VerticalExtentFar, HorizontalExtent),
                filter, out nearestRef, out nearestPt, out _);
        }

        if (!status.Succeeded() || nearestRef == 0)
        {
            return false;
        }

        polyRef = nearestRef;
        resolvedWow = NavmeshCoords.ToWow(nearestPt);
        return true;
    }

    private bool TryResolveColumn(Vector3 wow, bool? startIndoors, float? preferZ,
        ref long polyRef, ref Vector3 resolvedWow)
    {
        Vector3 rcCenter = NavmeshCoords.ToRc(wow);

        collector.Reset();
        DtStatus status = query.QueryPolygons(rcCenter,
            new Vector3(HorizontalExtent, VerticalExtentColumn, HorizontalExtent),
            filter, collector);

        if (!status.Succeeded() || collector.Refs.Count == 0)
        {
            return false;
        }

        candidates.Clear();
        foreach (long refs in collector.Refs)
        {
            if (query.ClosestPointOnPoly(refs, rcCenter, out Vector3 closest, out _).Succeeded())
            {
                candidates.Add(new Candidate(refs, closest));
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        // rc Y = wow Z (up). Highest first.
        candidates.Sort(static (a, b) => b.RcPos.Y.CompareTo(a.RcPos.Y));

        // A caller that knows which floor it is on - the player walked there,
        // so the surface under them barely moved between requests - beats every
        // geometric guess below. IsIndoors reports the WMO's authored flag, not
        // whether a roof happens to be overhead, so the headroom rule can pick
        // a different floor than the client would call the player's own.
        if (preferZ.HasValue)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (MathF.Abs(candidates[i].RcPos.Y - preferZ.Value) <= PreferZToleranceYd)
                {
                    polyRef = candidates[i].PolyRef;
                    resolvedWow = NavmeshCoords.ToWow(candidates[i].RcPos);
                    return true;
                }
            }
        }

        Candidate pick = candidates[0];

        if (startIndoors == true && candidates.Count > 1)
        {
            // Indoors: prefer the highest floor that still has a ceiling
            // (another candidate) above it with standing headroom - i.e. an
            // interior floor rather than the roof.
            for (int i = 1; i < candidates.Count; i++)
            {
                float gapToAbove = candidates[i - 1].RcPos.Y - candidates[i].RcPos.Y;
                if (gapToAbove >= MinHeadroom)
                {
                    pick = candidates[i];
                    break;
                }
            }
        }

        polyRef = pick.PolyRef;
        resolvedWow = NavmeshCoords.ToWow(pick.RcPos);
        return true;
    }

    private sealed class ColumnCollector : IDtPolyQuery
    {
        public List<long> Refs { get; } = new(MaxColumnCandidates);

        public void Reset()
        {
            Refs.Clear();
        }

        public void Process(DtMeshTile tile, ReadOnlySpan<int> polys, ReadOnlySpan<long> polyRefs, int count)
        {
            for (int i = 0; i < count && Refs.Count < MaxColumnCandidates; i++)
            {
                Refs.Add(polyRefs[i]);
            }
        }
    }
}
