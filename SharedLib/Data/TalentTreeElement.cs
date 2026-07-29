namespace SharedLib;

public readonly record struct TalentTreeElement
{
    public int TierID { get; init; }
    public int ColumnIndex { get; init; }
    public int TabID { get; init; }

    /// <summary>
    /// Only set from 5.0 on, where a talent belongs to a class rather than to one
    /// of its three tabs. Zero on every earlier client, whose rows are grouped by
    /// <see cref="TabID"/> through <see cref="TalentTab"/> instead.
    /// </summary>
    public int ClassID { get; init; }

    public int[] SpellIds { get; init; }
}
