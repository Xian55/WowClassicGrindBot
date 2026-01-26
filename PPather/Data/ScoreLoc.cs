using System.Numerics;

namespace PPather.Data;

public readonly record struct ScoreLoc
{
    public Vector3 Loc { get; init; }
    public float Range { get; init; }
    public float Score { get; init; }

    public ScoreLoc(Vector3 loc, float range, float score)
    {
        Loc = loc;
        Range = range;
        Score = score;
    }
}
