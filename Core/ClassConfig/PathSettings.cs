using SharedLib;
using SharedLib.Extensions;

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Core;

public sealed class PathSettings
{
    public int Id { get; set; }
    public string PathFilename { get; set; } = string.Empty;
    public string? OverridePathFilename { get; set; } = string.Empty;
    public bool PathThereAndBack { get; set; } = true;
    public bool PathReduceSteps { get; set; }

    public Vector3[] Path = Array.Empty<Vector3>();

    public string FileName =>
        !string.IsNullOrEmpty(OverridePathFilename)
        ? OverridePathFilename
        : PathFilename;

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

        if (Path.Length == 1)
        {
            Vector3 a = WorldMapAreaDB.ToWorld_FlipXY(path[0], playerReader.WorldMapArea);
            return (int)Vector2.Distance(a.AsVector2(), playerPosition);
        }

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
