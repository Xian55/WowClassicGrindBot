using PPather.Data;

using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace Core;

public interface IPPather
{
    /// <summary>
    /// True when returned paths are already smoothed and validated by the
    /// pathfinder (server-side spline + re-projection) and cheap to re-request,
    /// so consumers should drop partial routes and re-query instead of
    /// patching them up client-side.
    /// </summary>
    bool PathsAreSmoothed => false;

    Vector3[] FindMapRoute(int uiMap, Vector3 mapFrom, Vector3 mapTo);
    Vector3[] FindWorldRoute(int uiMap, bool startIndoors, Vector3 worldFrom, Vector3 worldTo);
    ValueTask DrawLines(List<LineArgs> lineArgs);
    ValueTask DrawSphere(SphereArgs args);
}