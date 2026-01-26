using Newtonsoft.Json;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.IO;

namespace Core;

public static class FrameConfigMeta
{
    public const int Version = 4;
    public const string DefaultFilename = "frame_config.json";
}

public static class FrameConfig
{
    public static bool Exists()
    {
        return File.Exists(FrameConfigMeta.DefaultFilename);
    }

    public static bool IsValid(Rectangle rect, Version addonVersion)
    {
        try
        {
            var config = Load();

            bool sameVersion = config.Version == FrameConfigMeta.Version;
            bool sameAddonVersion = config.AddonVersion == addonVersion;
            bool sameRect = config.Rect.Width == rect.Width && config.Rect.Height == rect.Height;
            return sameAddonVersion && sameVersion && sameRect && config.Frames.Length > 1;
        }
        catch
        {
            return false;
        }
    }

    public static DataFrameConfig Load()
    {
        return JsonConvert.DeserializeObject<DataFrameConfig>(File.ReadAllText(FrameConfigMeta.DefaultFilename));
    }

    public static DataFrame[] LoadFrames()
    {
        if (Exists())
        {
            var config = Load();
            if (config.Version == FrameConfigMeta.Version)
                return config.Frames;
        }

        return Array.Empty<DataFrame>();
    }

    public static DataFrameMeta LoadMeta()
    {
        var config = Load();
        if (config.Version == FrameConfigMeta.Version)
            return config.Meta;

        return DataFrameMeta.Empty;
    }

    public static void Save(Rectangle rect, Version addonVersion, DataFrameMeta meta, DataFrame[] dataFrames)
    {
        DataFrameConfig config = new(FrameConfigMeta.Version, addonVersion, rect, meta, dataFrames);

        string json = JsonConvert.SerializeObject(config);
        File.WriteAllText(FrameConfigMeta.DefaultFilename, json);
    }

    public static void Delete()
    {
        if (Exists())
        {
            File.Delete(FrameConfigMeta.DefaultFilename);
        }
    }

    public static DataFrameMeta GetMeta(Bgra32 color)
    {
        int hash = color.R * 65536 + color.G * 256 + color.B;
        if (hash == 0)
            return DataFrameMeta.Empty;

        // CELL_SPACING * 10000000 + CELL_SIZE * 100000 + 1000 * FRAME_ROWS + NUMBER_OF_FRAMES
        int spacing = hash / 10000000;
        int size = hash / 100000 % 100;
        int rows = hash / 1000 % 100;
        int count = hash % 1000;

        return new DataFrameMeta(hash, spacing, size, rows, count);
    }

    public static DataFrame[] CreateFrames(DataFrameMeta meta, Image<Bgra32> bmp)
    {
        DataFrame[] frames = new DataFrame[meta.Count];
        frames[0] = new(0, 0, 0);

        for (int i = 1; i < meta.Count; i++)
        {
            if (TryGetNextPoint(bmp, i, frames[i].X, out int x, out int y))
            {
                frames[i] = new(i, x, y);
            }
            else
            {
                break;
            }
        }

        return frames;
    }

    private static bool TryGetNextPoint(Image<Bgra32> bmp, int i, int startX, out int x, out int y)
    {
        for (int xi = startX; xi < bmp.Width; xi++)
        {
            for (int yi = 0; yi < bmp.Height; yi++)
            {
                Bgra32 pixel = bmp[xi, yi];
                if (pixel.B == i && pixel.R == 0 && pixel.G == 0)
                {
                    x = xi;
                    y = yi;
                    return true;
                }
            }
        }

        x = y = -1;
        return false;
    }
}