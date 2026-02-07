using SixLabors.ImageSharp;

using System;

namespace SharedLib.NpcFinder;

public interface INpcLineSegmentProvider
{
    ReadOnlySpan<LineSegment> GetLineSegments(Rectangle area, float minLength, float minEndLength);
}

public interface IConfigurableLineSegmentProvider : INpcLineSegmentProvider
{
    void Configure(SearchMode searchMode, NpcNames nameType);
}
