using SharedLib;

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Core;

/// <summary>
/// The zone a generated route is confined to, resolved from
/// <see cref="RouteGenSettings"/> once and then asked "is this world point inside".
/// </summary>
public sealed class RouteTarget
{
    /// <summary>The drawable map, for world/map coordinate conversion.</summary>
    public WorldMapArea Zone { get; }

    /// <summary>Continent map id, which keys the spawn dump and the navmesh.</summary>
    public float MapId => Zone.MapID;

    public int UIMapId => Zone.UIMapId;

    /// <summary>Human-readable, for logs and the goal name.</summary>
    public string Name { get; }

    /// <summary>
    /// Area ids that count as inside. A subzone target holds exactly one; a whole-zone
    /// target holds the zone plus each of its subzones, because the baked
    /// <c>AreaGrid</c> stores the finest id at a point and never the parent's.
    /// </summary>
    private readonly HashSet<int> areaIds;

    private RouteTarget(WorldMapArea zone, string name, HashSet<int> areaIds)
    {
        Zone = zone;
        Name = name;
        this.areaIds = areaIds;
    }

    public bool AcceptsAreaId(int areaId) => areaIds.Contains(areaId);

    /// <summary>
    /// Fallback containment for points the area grid cannot answer for (id 0 = outside the
    /// baked bounds, which is the whole of Kalimdor on the precata grid). Mirrors the map
    /// coordinate bounds check <see cref="Core.Database.AreaDB"/> uses for the same purpose.
    /// </summary>
    public bool WithinMapBounds(Vector3 worldPos)
    {
        Vector3 map = WorldMapAreaDB.ToMap_FlipXY(worldPos, Zone);
        return map.X > 0 && map.X < 100 && map.Y > 0 && map.Y < 100;
    }

    /// <summary>
    /// Resolution order mirrors <see cref="PathSettings.ConvertToWorldCoords"/>: explicit
    /// UIMapId, then subzone name, then zone name, then the player's race starting zone,
    /// then wherever the player currently is.
    /// </summary>
    public static RouteTarget? Resolve(RouteGenSettings settings, WorldMapAreaDB db,
        UnitRace playerRace, int playerUIMapId)
    {
        if (settings.UIMapId > 0 && db.TryGet(settings.UIMapId, out WorldMapArea explicitZone))
        {
            return ForZone(explicitZone, db);
        }

        if (!string.IsNullOrEmpty(settings.Subzone) &&
            db.TryFindSubzone(settings.Subzone, out WorldMapArea subzone))
        {
            WorldMapArea parent = db.GetByAreaId(subzone.ParentAreaId);
            if (parent.UIMapId > 0)
            {
                return new RouteTarget(parent, subzone.AreaName, [subzone.AreaID]);
            }
        }

        if (!string.IsNullOrEmpty(settings.Zone) &&
            db.TryFindByAreaName(settings.Zone, out WorldMapArea named))
        {
            return ForZone(named, db);
        }

        if (PathSettings.TryFindRaceZone(playerRace.ToString(), db, out int raceUIMapId) &&
            db.TryGet(raceUIMapId, out WorldMapArea raceZone))
        {
            return ForZone(raceZone, db);
        }

        return playerUIMapId > 0 && db.TryGet(playerUIMapId, out WorldMapArea current)
            ? ForZone(current, db)
            : null;
    }

    private static RouteTarget ForZone(WorldMapArea zone, WorldMapAreaDB db)
    {
        HashSet<int> ids = [zone.AreaID];

        ReadOnlySpan<WorldMapArea> subzones = db.Subzones;
        for (int i = 0; i < subzones.Length; i++)
        {
            if (subzones[i].ParentAreaId == zone.AreaID)
            {
                ids.Add(subzones[i].AreaID);
            }
        }

        return new RouteTarget(zone, zone.AreaName, ids);
    }
}
