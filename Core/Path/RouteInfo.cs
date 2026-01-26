using Core.Database;

using SharedLib;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Text;

using WowheadDB;

namespace Core;

public readonly struct RouteInfoPoi
{
    public readonly Vector3 MapLoc;
    public readonly string Name;
    public readonly string Color;
    public readonly float Radius;

    public RouteInfoPoi(NPC npc, string color)
    {
        MapLoc = npc.MapCoords[0];
        Name = npc.name;
        Color = color;
        Radius = 1;
    }

    public RouteInfoPoi(Vector3 mapLoc, string name, string color, float radius)
    {
        MapLoc = mapLoc;
        Name = name;
        Color = color;
        Radius = radius;
    }
}

public sealed class RouteInfo : IDisposable
{
    public IEnumerable<Vector3> RouteSrc { get; private set; }

    public Vector3[] Route { get; private set; }

    public Vector3[] RouteToWaypoint =>
        pathedRoutes.Length > 0 ? pathedRoutes
            .OrderByDescending(MostRecent)
            .First().PathingRoute()
        : [];

    private static DateTime MostRecent(IRouteProvider x) => x.LastActive;

    private readonly ImmutableArray<IRouteProvider> pathedRoutes;
    private readonly AreaDB areaDB;
    private readonly PlayerReader playerReader;
    private readonly WorldMapAreaDB worldmapAreaDB;

    public List<RouteInfoPoi> PoiList { get; } = new();

    private float min;
    private float diff;

    private float addY;
    private float addX;

    private int margin;
    private int canvasSize;

    private float pointToGrid;

    private const int dSize = 2;

    public RouteInfo(
        IEnumerable<IRouteProvider> pathedRoutes,
        PlayerReader playerReader, AreaDB areaDB,
        WorldMapAreaDB worldmapAreaDB)
    {
        this.pathedRoutes = pathedRoutes.ToImmutableArray();
        this.playerReader = playerReader;
        this.areaDB = areaDB;
        this.worldmapAreaDB = worldmapAreaDB;

        RouteSrc = pathedRoutes.Any()
            ? (this.Route = pathedRoutes.First().MapRoute() ?? [])
            : this.Route = [];

        this.areaDB.Changed += OnZoneChanged;
        OnZoneChanged();

        CalculateDiffs();
    }

    public void Dispose()
    {
        areaDB.Changed -= OnZoneChanged;
    }

    public void SetRouteSource(IEnumerable<Vector3>? src = null)
    {
        RouteSrc = src ?? Route;
    }

    public void UpdateRoute(Vector3[] mapRoute)
    {
        Route = mapRoute;

        foreach (var r in pathedRoutes.OfType<IEditedRouteReceiver>())
        {
            r.ReceivePath(RouteSrc.ToArray(), Route);
        }
    }

    public void SetMargin(int margin)
    {
        this.margin = margin;
        CalculatePointToGrid();
    }

    public void SetCanvasSize(int size)
    {
        this.canvasSize = size;
        CalculatePointToGrid();
    }

    public void CalculatePointToGrid()
    {
        pointToGrid = ((float)canvasSize - (margin * 2)) / diff;
        CalculateDiffs();
    }

    public int ToCanvasPointX(float value)
    {
        return (int)(margin + ((value + addX - min) * pointToGrid));
    }

    public int ToCanvasPointY(float value)
    {
        return (int)(margin + ((value + addY - min) * pointToGrid));
    }

    public float DistanceToGrid(int value)
    {
        return value / 100f * pointToGrid;
    }

    private void OnZoneChanged()
    {
        if (areaDB.CurrentArea == null)
            return;

        PoiList.Clear();
        /*
        // Visualize the zone pois
        for (int i = 0; i < areaDB.CurrentArea.vendor?.Count; i++)
        {
            NPC npc = areaDB.CurrentArea.vendor[i];
            PoiList.Add(new RouteInfoPoi(npc, "green"));
        }

        for (int i = 0; i < areaDB.CurrentArea.repair?.Count; i++)
        {
            NPC npc = areaDB.CurrentArea.repair[i];
            PoiList.Add(new RouteInfoPoi(npc, "purple"));
        }

        for (int i = 0; i < areaDB.CurrentArea.innkeeper?.Count; i++)
        {
            NPC npc = areaDB.CurrentArea.innkeeper[i];
            PoiList.Add(new RouteInfoPoi(npc, "blue"));
        }

        for (int i = 0; i < areaDB.CurrentArea.flightmaster?.Count; i++)
        {
            NPC npc = areaDB.CurrentArea.flightmaster[i];
            PoiList.Add(new RouteInfoPoi(npc, "orange"));
        }
        */
    }

    private void CalculateDiffs()
    {
        int routeLength = RouteSrc.Count();
        int navLength = RouteToWaypoint.Length;
        int poiCount = PoiList.Count;

        bool hasTarget = playerReader.TargetId != 0;
        int extraCount = hasTarget ? 2 : 1;

        int length = routeLength + navLength + poiCount + extraCount;
        Span<Vector3> total = stackalloc Vector3[length];

        RouteToWaypoint.AsSpan().CopyTo(total);
        worldmapAreaDB.ToMap_FlipXY(playerReader.UIMapId.Value, total[..navLength]);

        for (int i = 0; i < poiCount; i++)
        {
            total[navLength + i] = PoiList[i].MapLoc;
        }

        int idx = 0;
        foreach (Vector3 p in RouteSrc)
        {
            total[navLength + poiCount + idx++] = p;
        }

        if (hasTarget)
            total[^2] = playerReader.TargetMapPos;

        total[^1] = playerReader.MapPos;

        static float X(Vector3 s) => s.X;
        float maxX = Max(total, X);
        float minX = Min(total, X);
        float diffX = maxX - minX;

        static float Y(Vector3 s) => s.Y;
        float maxY = Max(total, Y);
        float minY = Min(total, Y);
        float diffY = maxY - minY;

        this.addY = 0;
        this.addX = 0;

        if (diffX > diffY)
        {
            this.addY = minX - minY;
            this.min = minX;
            this.diff = diffX;
        }
        else
        {
            this.addX = minY - minX;
            this.min = minY;
            this.diff = diffY;
        }

        static float Max(ReadOnlySpan<Vector3> span, Func<Vector3, float> selector)
        {
            float max = float.MinValue;

            for (int i = 0; i < span.Length; i++)
            {
                float v = selector(span[i]);
                if (v > max)
                    max = v;
            }

            return max;
        }

        static float Min(ReadOnlySpan<Vector3> span, Func<Vector3, float> selector)
        {
            float max = float.MaxValue;

            for (int i = 0; i < span.Length; i++)
            {
                float v = selector(span[i]);
                if (v < max)
                    max = v;
            }

            return max;
        }
    }


    public string RenderPathLines(ReadOnlySpan<Vector3> path)
    {
        if (path.Length <= 1)
            return string.Empty;

        StringBuilder sb = new();
        for (int i = 1; i < path.Length; i++)
        {
            Vector3 p1 = path[i];
            Vector3 p2 = path[i - 1];

            sb.AppendLine(
                $"<line " +
                $"x1='{ToCanvasPointX(p1.X)}' " +
                $"y1='{ToCanvasPointY(p1.Y)}' " +
                $"x2='{ToCanvasPointX(p2.X)}' " +
                $"y2='{ToCanvasPointY(p2.Y)}' />");
        }
        return sb.ToString();
    }

    private const string FIRST = "<br><b>First</b>";
    private const string LAST = "<br><b>Last</b>";

    public string RenderPathPoints(ReadOnlySpan<Vector3> path)
    {
        StringBuilder sb = new();
        int last = path.Length - 1;
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 p = path[i];
            float x = p.X;
            float y = p.Y;
            sb.AppendLine(
                $"<circle onmousedown=\"pointClick(evt,{x},{y},{i});\" " +
                $"onmousemove=\"showTooltip(evt,'{x},{y}{(
                    i == 0
                    ? FIRST
                    : i == last
                    ? LAST
                    : string.Empty)}');\" " +
                $"onmouseout=\"hideTooltip();\" " +
                $"cx='{ToCanvasPointX(x)}' " +
                $"cy='{ToCanvasPointY(y)}' r='{dSize}' />");
        }
        return sb.ToString();
    }

    public Vector3 NextPoint()
    {
        IRouteProvider? mostRecent = pathedRoutes
            .OrderByDescending(MostRecent)
            .FirstOrDefault();

        if (mostRecent == null || !mostRecent.HasNext())
            return Vector3.Zero;

        // dynamically update the path based on source
        if (mostRecent.MapRoute() != Array.Empty<Vector3>())
        {
            RouteSrc = Route = mostRecent.MapRoute();
        }

        return mostRecent.NextMapPoint();
    }

    public string RenderNextPoint()
    {
        Vector3 pt = NextPoint();
        if (pt == Vector3.Zero)
            return string.Empty;

        return $"<circle " +
            $"cx='{ToCanvasPointX(pt.X)}' " +
            $"cy='{ToCanvasPointY(pt.Y)}'" +
            $"r='{dSize + 1}' />";
    }

    public string RenderPoint(Vector3 pt)
    {
        if (pt == Vector3.Zero)
            return string.Empty;

        return $"<circle " +
            $"cx='{ToCanvasPointX(pt.X)}' " +
            $"cy='{ToCanvasPointY(pt.Y)}'" +
            $"r='{dSize + 1}' />";
    }

    public string DeathImage(Vector3 pt)
    {
        var size = this.canvasSize / 25;
        return pt == Vector3.Zero
            ? string.Empty
            : $"<image href='_content/Frontend/img/death.svg' " +
            $"x='{ToCanvasPointX(pt.X) - size / 2}' " +
            $"y='{ToCanvasPointY(pt.Y) - size / 2}' " +
            $"height='{size}' " +
            $"width='{size}' />";
    }

    public string DrawPoi(RouteInfoPoi poi)
    {
        return $"<circle " +
            $"onmousemove=\"showTooltip(evt, '{poi.Name}<br/>{poi.MapLoc.X},{poi.MapLoc.Y}');\" " +
            $"onmouseout=\"hideTooltip();\" " +
            $"cx='{ToCanvasPointX(poi.MapLoc.X)}' " +
            $"cy='{ToCanvasPointY(poi.MapLoc.Y)}' " +
            $"r='{(poi.Radius == 1 ? dSize : DistanceToGrid((int)poi.Radius))}' " + (
            poi.Radius == 1
            ? $"fill='{poi.Color}'"
            : $"stroke='{poi.Color}' " +
            $"stroke-width='1' fill='none'") + " />";
    }
}