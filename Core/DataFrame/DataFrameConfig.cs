using SixLabors.ImageSharp;

using System;

namespace Core;

public readonly record struct DataFrameConfig
{
    public int Version { get; }
    public Version AddonVersion { get; }
    public Rectangle Rect { get; }
    public DataFrameMeta Meta { get; }
    public DataFrame[] Frames { get; }

    public DataFrameConfig(int version, Version addonVersion, Rectangle rect, DataFrameMeta meta, DataFrame[] frames)
    {
        Version = version;
        this.AddonVersion = addonVersion;
        this.Rect = rect;
        this.Meta = meta;
        this.Frames = frames;
    }
}
