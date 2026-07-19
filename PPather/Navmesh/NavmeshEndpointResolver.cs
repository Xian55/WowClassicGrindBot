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

    public bool TryResolve(Vector3 wow, bool? startIndoors, out long polyRef, out Vector3 resolvedWow)
    {
        polyRef = 0;
        resolvedWow = wow;

        return wow.Z != 0
            ? TryResolveNear(wow, ref polyRef, ref resolvedWow)
            : TryResolveColumn(wow, startIndoors, ref polyRef, ref resolvedWow);
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

    private bool TryResolveColumn(Vector3 wow, bool? startIndoors, ref long polyRef, ref Vector3 resolvedWow)
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
