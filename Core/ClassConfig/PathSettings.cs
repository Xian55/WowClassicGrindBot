using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Extensions;

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Core;

public sealed partial class PathSettings
{
    public int Id { get; set; }
    public string PathFilename { get; set; } = string.Empty;
    public string? OverridePathFilename { get; set; } = string.Empty;
    public bool PathThereAndBack { get; set; } = true;
    public bool PathReduceSteps { get; set; }
    public int UIMapId { get; set; }

    public bool WorldCoords { get; private set; }
    public Vector3[] OriginalMapPath { get; private set; } = Array.Empty<Vector3>();

    public Vector3[] Path = Array.Empty<Vector3>();

    public string FileName =>
        !string.IsNullOrEmpty(OverridePathFilename)
        ? OverridePathFilename
        : PathFilename;

    private const int MaxRaceStartingZoneLevel = 20;

    private static readonly (string Race, int AreaId)[] RaceStartingZones =
    [
        ("NightElf", 141),   // Teldrassil
        ("BloodElf", 3430),  // Eversong Woods
        ("Draenei", 3524),   // Azuremyst Isle
        ("Undead", 85),      // Tirisfal Glades
        ("Tauren", 215),     // Mulgore
        ("Human", 12),       // Elwynn Forest
        ("Dwarf", 1),        // Dun Morogh
        ("Gnome", 1),        // Dun Morogh
        ("Orc", 14),         // Durotar
        ("Troll", 14),       // Durotar
    ];

    public List<string> Requirements = [];
    public Requirement[] RequirementsRuntime = [];

    private RecordInt globalTime = null!;
    private PlayerReader playerReader = null!;

    private int canRunTime;
    private bool canRun;

    public List<string> SideActivityRequirements = [];
    public Requirement[] SideActivityRequirementsRuntime = [];

    private int canSideActivityTime;
    private bool canSideActivity;

    public bool PathFinished() => Finished();
    public Func<bool> Finished = () => true;

    public void Init(RecordInt globalTime, PlayerReader playerReader, int id)
    {
        this.globalTime = globalTime;
        this.playerReader = playerReader;
        Id = Id == default ? id : Id;
    }

    public void ConvertToWorldCoords(ILogger logger, WorldMapAreaDB worldMapAreaDB)
    {
        if (Path.Length == 0)
            return;

        int uiMapId;
        if (UIMapId > 0)
        {
            uiMapId = UIMapId;
        }
        else
        {
            // 1. Try zone name from filepath (includes directories)
            if (worldMapAreaDB.TryFindByAreaName(FileName, out WorldMapArea matchedArea))
            {
                LogUIMapIdFromFilename(logger, FileName, matchedArea.AreaName, matchedArea.UIMapId);
                uiMapId = matchedArea.UIMapId;
            }
            // 2. Try subzone name from filepath → resolve to parent zone
            else if (worldMapAreaDB.TryFindBySubzoneName(FileName, out WorldMapArea parentZone))
            {
                LogUIMapIdFromSubzone(logger, FileName, parentZone.AreaName, parentZone.UIMapId);
                uiMapId = parentZone.UIMapId;
            }
            // 3. Try race name from filename only (not directories) → starting zone
            else if (TryFindRaceZone(System.IO.Path.GetFileNameWithoutExtension(FileName), worldMapAreaDB, out int raceUIMapId))
            {
                LogUIMapIdAutoDetect(logger, FileName, raceUIMapId);
                uiMapId = raceUIMapId;
            }
            // 4. Fallback to player's current zone
            else
            {
                uiMapId = playerReader.UIMapId.Value;
                LogUIMapIdAutoDetect(logger, FileName, uiMapId);
                if (uiMapId <= 0)
                    return;
            }
        }

        OriginalMapPath = new Vector3[Path.Length];
        Array.Copy(Path, OriginalMapPath, Path.Length);

        worldMapAreaDB.ToWorldXY_FlipXY(uiMapId, Path);
        WorldCoords = true;
    }

    private static bool TryFindRaceZone(ReadOnlySpan<char> input, WorldMapAreaDB worldMapAreaDB, out int uiMapId)
    {
        if (TryParseMinLevel(input, out int minLevel) && minLevel > MaxRaceStartingZoneLevel)
        {
            uiMapId = 0;
            return false;
        }

        for (int i = 0; i < RaceStartingZones.Length; i++)
        {
            (string race, int areaId) = RaceStartingZones[i];
            if (input.Contains(race, StringComparison.OrdinalIgnoreCase))
            {
                WorldMapArea wma = worldMapAreaDB.GetByAreaId(areaId);
                if (wma.UIMapId > 0)
                {
                    uiMapId = wma.UIMapId;
                    return true;
                }
            }
        }

        uiMapId = 0;
        return false;
    }

    /// <summary>
    /// Parses the minimum level from a filename starting with "NN-NN" pattern.
    /// e.g. "37-42 Gorillas" → 37, "01-04_Durotar" → 1
    /// </summary>
    private static bool TryParseMinLevel(ReadOnlySpan<char> fileName, out int minLevel)
    {
        int dashIndex = fileName.IndexOf('-');
        if (dashIndex > 0)
            return int.TryParse(fileName[..dashIndex], out minLevel);

        minLevel = 0;
        return false;
    }

    public bool CanRun()
    {
        if (canRunTime == globalTime.Value)
            return canRun;

        canRunTime = globalTime.Value;

        ReadOnlySpan<Requirement> span = RequirementsRuntime;
        for (int i = 0; i < span.Length; i++)
        {
            if (!span[i].HasRequirement())
                return canRun = false;
        }

        return canRun = true;
    }

    public bool CanRunSideActivity()
    {
        if (canSideActivityTime == globalTime.Value)
            return canSideActivity;

        canSideActivityTime = globalTime.Value;

        ReadOnlySpan<Requirement> span = SideActivityRequirementsRuntime;
        for (int i = 0; i < span.Length; i++)
        {
            if (!span[i].HasRequirement())
                return canSideActivity = false;
        }

        return canSideActivity = true;
    }

    public int GetDistanceXYFromPath()
    {
        if (Path.Length == 0)
            return int.MaxValue;

        ReadOnlySpan<Vector3> path = Path;
        Vector2 playerPosition = playerReader.WorldPos.AsVector2();

        if (WorldCoords)
        {
            if (Path.Length == 1)
                return (int)Vector2.Distance(path[0].AsVector2(), playerPosition);

            float distance = float.MaxValue;
            for (int i = 1; i < path.Length; i++)
            {
                Vector2 closestPoint = VectorExt.GetClosestPointOnLineSegment(
                    path[i - 1].AsVector2(), path[i].AsVector2(), playerPosition);
                float d = Vector2.Distance(closestPoint, playerPosition);
                if (d < distance)
                    distance = d;
            }

            return (int)distance;
        }

        if (Path.Length == 1)
        {
            Vector3 a = WorldMapAreaDB.ToWorld_FlipXY(path[0], playerReader.WorldMapArea);
            return (int)Vector2.Distance(a.AsVector2(), playerPosition);
        }

        {
            float distance = float.MaxValue;

            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = WorldMapAreaDB.ToWorld_FlipXY(path[i - 1], playerReader.WorldMapArea);
                Vector3 b = WorldMapAreaDB.ToWorld_FlipXY(path[i], playerReader.WorldMapArea);

                Vector2 closestPoint = VectorExt.GetClosestPointOnLineSegment(a.AsVector2(), b.AsVector2(), playerPosition);
                float d = Vector2.Distance(closestPoint, playerPosition);
                if (d < distance)
                    distance = d;
            }

            return (int)distance;
        }
    }

    #region Logging

    [LoggerMessage(
        EventId = 0020,
        Level = LogLevel.Information,
        Message = "[{fileName}] UIMapId auto-detect fallback {playerUIMapId}")]
    static partial void LogUIMapIdAutoDetect(ILogger logger, string fileName, int playerUIMapId);

    [LoggerMessage(
        EventId = 0021,
        Level = LogLevel.Information,
        Message = "[{fileName}] UIMapId {uiMapId} detected from subzone → parent zone '{areaName}'")]
    static partial void LogUIMapIdFromSubzone(ILogger logger, string fileName, string areaName, int uiMapId);

    [LoggerMessage(
        EventId = 0022,
        Level = LogLevel.Information,
        Message = "[{fileName}] UIMapId {uiMapId} detected from zone '{areaName}'")]
    static partial void LogUIMapIdFromFilename(ILogger logger, string fileName, string areaName, int uiMapId);

    #endregion
}
