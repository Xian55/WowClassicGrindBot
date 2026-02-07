using Microsoft.Extensions.Logging;

using PPather.Triangles.GameV2;

using System.Buffers;
using System.Collections.Generic;
using System.Collections.Frozen;

using static System.Diagnostics.Stopwatch;
using System;
using System.Text.Json;
using System.IO;
using SharedLib;
using SharedLib.Converters;

using StormDll;
using System.Numerics;

namespace CoreTests.PPatherV2;

public class PPatherV2
{
    private static readonly JsonSerializerOptions options = new()
    {
        Converters = { new Vector3Converter(true) }
    };

    private readonly FrozenSet<int> unusedMapIds =
    [
        13,  169, 25,  29,
        42,  451, 573, 582,
        584, 586, 587, 588,
        589, 590, 591, 592,
        593, 594, 596, 597,
        605, 606, 610, 612,
        613, 614, 620, 621,
        622, 623, 641, 642,
        647, 672, 673, 712,
        713, 718,
    ];

    private readonly Dictionary<int, string> maps = new()
    {
        [0] = "Azeroth",
        [1] = "Kalimdor",
        [530] = "Expansion01",
        [571] = "Northrend",
    };

    private static Dictionary<int, string> GenerateMaps(string exp)
    {
        Dictionary<int, string> maps = new()
        {
            [0] = "Azeroth",
            [1] = "Kalimdor",
        };

        if (exp is "tbc")
        {
            maps[530] = "Expansion01";
        }

        if (exp == "wrath")
        {
            maps[530] = "Expansion01";
            maps[571] = "Northrend";
        }

        return maps;
    }

    public PPatherV2(ILogger logger, DataConfig dataConfig)
    {
        string[] mpqFiles = Directory.GetFiles(dataConfig.MPQ, "*.MPQ");
        if (mpqFiles.Length == 0)
        {
            logger.LogError("No MPQ files found in {MpqPath}", dataConfig.MPQ);
            return;
        }
        ArchiveSet archive = new(logger, mpqFiles);

        bool SubZoneBounds = true;

        if (SubZoneBounds)
        {
            GenerateBoundingBoxForSubZones(logger, dataConfig, archive);
        }
        else
        {
            ExtractGeometry(logger, archive);
        }
    }

    private void GenerateBoundingBoxForSubZones(ILogger logger, DataConfig dataConfig, ArchiveSet archive)
    {
        var mapDict = GenerateMaps(dataConfig.Exp);

        foreach ((int continentId, string value) in mapDict)
        {
            // skip building trash maps
            if (unusedMapIds.Contains(continentId))
                continue;

            ReadOnlySpan<char> mapName = value;
            ReadOnlySpan<char> mapsPath = $"World\\Maps\\{mapName}\\{mapName}";
            ReadOnlySpan<char> wdtPath = $"{mapsPath}.wdt";

            var wdtFileStream = archive.GetStream(wdtPath);
            int wdtLength = (int)wdtFileStream.Length;
            byte[] wdtData = new byte[wdtLength];
            wdtFileStream.ReadAllBytesTo(wdtData);

            Wdt wdt = new(wdtData, wdtLength);

            Dictionary<int, SubZoneArea> areaBounds = [];

            for (int i = 0; i < Const.WDT_MAP_SIZE * Const.WDT_MAP_SIZE; ++i)
            {
                int x = i % Const.WDT_MAP_SIZE;
                int y = i / Const.WDT_MAP_SIZE;

                var main = wdt.Main();
                if (main[y, x].exists != 1)
                {
                    continue;
                }

                ReadOnlySpan<char> adtPath = $"{mapsPath}_{x}_{y}.adt";
                if (!archive.Exists(adtPath))
                {
                    continue;
                }

                var adtFileStream = archive.GetStream(adtPath);

                ArrayPool<byte> pooler = ArrayPool<byte>.Shared;
                int length = (int)adtFileStream.Length;
                byte[] adtData = pooler.Rent(length);

                adtFileStream.ReadAllBytesTo(adtData);

                Adt adt = new(adtData, (uint)length);

                for (int a = 0; a < Adt.ADT_CELLS_PER_GRID * Adt.ADT_CELLS_PER_GRID; ++a)
                {
                    int cx = a % Adt.ADT_CELLS_PER_GRID;
                    int cy = a / Adt.ADT_CELLS_PER_GRID;

                    adt.CalculateAreaBoundingBox((uint)cx, (uint)cy, areaBounds);
                }

                pooler.Return(adtData);
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("[{MapName}] area bounds: {AreaBoundsCount}", mapName.ToString(), areaBounds.Count);

                foreach (var (areaId, bb) in areaBounds)
                {
                    logger.LogInformation("[{MapName}] area {ContinentId} {BoundingBox}", mapName.ToString(), continentId, bb);
                }
            }

            string json = JsonSerializer.Serialize(areaBounds.Values, options);
            File.WriteAllText(Path.Combine(dataConfig.Subzones, $"{continentId}.json"), json);
        }
    }

    private void ExtractGeometry(ILogger logger, ArchiveSet archive)
    {
        //for (int mapIndex = 0; mapIndex < maps.Count; ++mapIndex)
        foreach ((int continentId, string value) in maps)
        {
            // skip building trash maps
            if (unusedMapIds.Contains(continentId))
                continue;

            ReadOnlySpan<char> mapName = value;
            ReadOnlySpan<char> mapsPath = $"World\\Maps\\{mapName}\\{mapName}";
            ReadOnlySpan<char> wdtPath = $"{mapsPath}.wdt";

            var wdtFileStream = archive.GetStream(wdtPath);
            int wdtLength = (int)wdtFileStream.Length;
            byte[] wdtData = new byte[wdtLength];
            wdtFileStream.ReadAllBytesTo(wdtData);

            Wdt wdt = new(wdtData, wdtLength);

            Structure mapGeometry = new();

            long totalStartTime = GetTimestamp();

            //Parallel.For(0, 16, i =>
            for (int i = 0; i < 16; ++i) // 16 Const.WDT_MAP_SIZE * Const.WDT_MAP_SIZE
            {
                long startTime = GetTimestamp();

                int x = 31 + i % 4;// % Const.WDT_MAP_SIZE; // 32     
                int y = 59 + i / 4;// / Const.WDT_MAP_SIZE; // 48

                var main = wdt.Main();
                if (main[y, x].exists != 1)
                {
                    continue; //return
                }

                ReadOnlySpan<char> adtPath = $"{mapsPath}_{x}_{y}.adt";
                if (!archive.Exists(adtPath))
                {
                    continue;
                }

                var adtFileStream = archive.GetStream(adtPath);

                ArrayPool<byte> pooler = ArrayPool<byte>.Shared;
                int length = (int)adtFileStream.Length;
                byte[] adtData = pooler.Rent(length);

                adtFileStream.ReadAllBytesTo(adtData);

                Adt adt = new(adtData, (uint)length);

                Structure terrain = new();

                for (int a = 0; a < Adt.ADT_CELLS_PER_GRID * Adt.ADT_CELLS_PER_GRID; ++a)
                {
                    int cx = a % Adt.ADT_CELLS_PER_GRID;
                    int cy = a / Adt.ADT_CELLS_PER_GRID;

                    adt.GetTerrainVertsAndTris((uint)cx, (uint)cy, terrain);
                    // TODO: fix this memory leak
                    //adt.GetLiquidVertsAndTris((uint)cx, (uint)cy, terrain);
                }

                pooler.Return(adtData);

                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("[{MapName}] [{X},{Y}] verts: {VertCount} tris: {TriCount} - {ElapsedMs}ms", mapName.ToString(), x, y, terrain.Verts.Count, terrain.Tris.Count, GetElapsedTime(startTime).TotalMilliseconds);

                terrain.ExportDebugObjFile($"X:\\Programming\\WowClassicGrindBot\\Json\\obj\\terrain_{mapName}_{x}_{y}.obj");
            }
            //);

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("[{MapName}] total: {ElapsedMs}ms", mapName.ToString(), GetElapsedTime(totalStartTime).TotalMilliseconds);
        }
    }

}
