using Newtonsoft.Json;

using SixLabors.ImageSharp;

using System;

namespace Core;

public readonly record struct DataFrameMeta
{
    [JsonIgnore]
    private static readonly DataFrameMeta empty = new(-1, 0, 0, 0, 0);
    [JsonIgnore]
    public static ref readonly DataFrameMeta Empty => ref empty;

    [JsonConstructor]
    public DataFrameMeta(int hash, int spacing, int sizes, int rows, int count)
    {
        this.Hash = hash;
        this.Spacing = spacing;
        this.Sizes = sizes;
        this.Rows = rows;
        this.Count = count;
    }

    public int Hash { get; }

    public int Spacing { get; }

    public int Sizes { get; }

    public int Rows { get; }

    public int Count { get; }

    public Size EstimatedSize(Rectangle screenRect)
    {
        const int error = 2;

        int cellSize = Sizes + error + (Spacing != 0 ? Spacing + error : 0);
        if (cellSize <= 0)
            return Size.Empty;

        SizeF estimated =
            new((float)Math.Ceiling(Count / (float)Rows) * cellSize, Rows * cellSize);

        return estimated.Width > screenRect.Width ||
            estimated.Height > screenRect.Height
            ? Size.Empty
            : (Size)estimated;
    }
}
