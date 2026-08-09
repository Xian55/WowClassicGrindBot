using Core;

using Game;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;
using Serilog.Extensions.Logging;

using SharedLib;
using SharedLib.NpcFinder;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CoreTests;

internal sealed class Program
{
    private static Microsoft.Extensions.Logging.ILogger logger;
    private static ILoggerFactory loggerFactory;

    private static CancellationTokenSource cts;
    private static WowProcess process;
    private static IWowScreen screen;

    private static bool LogOverallTimes;
    private static bool LogEachUpdate = true;
    private static bool UseGpu = true;
    private static bool UseDxgi;
    private static int delay = 150;

    private static readonly Dictionary<string, Action<string[]>> suites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["npc"] = Test_NPCNameFinder,
        ["input"] = Test_Input,
        ["cursor-grab"] = Test_CursorGrabber,
        ["cursor-compare"] = Test_CursorCompare,
        ["minimap"] = Test_MinimapNodeFinder,
        ["find-target"] = Test_FindTargetByCursor,
        ["pather"] = Test_PPather,
        ["navmesh"] = Test_NavmeshCoords,
        ["routegen"] = Test_RouteGeneration,
    };

    /// <summary>Suites that need no WoW process - see the attach decision in Main.</summary>
    private static readonly HashSet<string> offlineSuites =
        new(StringComparer.OrdinalIgnoreCase) { "navmesh", "routegen" };

    public static void Main(string[] args)
    {
        var logConfig = new LoggerConfiguration()
            .WriteTo.File("names.log")
            .WriteTo.Debug()
            .WriteTo.Console()
            .CreateLogger();

        Log.Logger = logConfig;
        logger = new SerilogLoggerProvider(Log.Logger).CreateLogger(nameof(Program));

        loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders().AddSerilog();
        });

        List<string> remaining = [];
        for (int a = 0; a < args.Length; a++)
        {
            switch (args[a].ToLowerInvariant())
            {
                case "--log-times":
                    LogOverallTimes = true;
                    break;
                case "--no-gpu":
                    UseGpu = false;
                    break;
                case "--delay" when a + 1 < args.Length && int.TryParse(args[a + 1], out int d):
                    delay = d;
                    a++;
                    break;
                case "--no-log-update":
                    LogEachUpdate = false;
                    break;
                case "--dxgi":
                    UseDxgi = true;
                    break;
                default:
                    remaining.Add(args[a]);
                    break;
            }
        }

        // Suites that need neither a running WoW process nor screen capture.
        if (remaining.Count > 0 && remaining[0].Equals("navmesh", StringComparison.OrdinalIgnoreCase))
        {
            Test_NavmeshCoords(remaining.GetRange(1, remaining.Count - 1).ToArray());
            Test_CostZones(remaining.GetRange(1, remaining.Count - 1).ToArray());
            Log.CloseAndFlush();
            return;
        }

        if (remaining.Count > 0 && remaining[0].Equals("spline-sim", StringComparison.OrdinalIgnoreCase))
        {
            SplineSim.SplineSim.Run(logger);
            Log.CloseAndFlush();
            return;
        }

        // Suites that read only from Json/ and the baked navmesh. Attaching to the game
        // would be the only thing that could fail, so do not attach at all - otherwise
        // every offline suite needs WoW running to say anything.
        bool needsGame = remaining.Count == 0 ||
            !offlineSuites.Contains(remaining[0]);

        if (needsGame)
        {
            // its expected to have at least 2 DataFrame
            DataFrame[] mockFrames =
            [
                new DataFrame(0, 0, 0),
                new DataFrame(1, 0, 0),
            ];

            cts = new CancellationTokenSource();
            process = new(cts, Options.Create<StartupConfigPid>(new() { Id = -1 }));
            screen = UseDxgi
                ? new WowScreenDXGI(loggerFactory.CreateLogger<WowScreenDXGI>(), process, mockFrames)
                : new WowScreenWGC(loggerFactory.CreateLogger<WowScreenWGC>(), process, mockFrames);
        }

        if (remaining.Count > 0 && suites.TryGetValue(remaining[0], out Action<string[]> suite))
        {
            string[] suiteArgs = remaining.GetRange(1, remaining.Count - 1).ToArray();
            Log.Logger.Information("Suite: {Suite} | Args: {Args}", remaining[0], string.Join(", ", suiteArgs));
            suite(suiteArgs);
        }
        else
        {
            Log.Logger.Information("Available suites: {Suites}", string.Join(", ", suites.Keys));
            Log.Logger.Information("Global flags: --log-times, --no-gpu, --no-log-update, --dxgi, --delay <ms>");
            Log.Logger.Information("Example: dotnet run -c Release -- --log-times npc enemy neutral 10000");
        }

        Log.CloseAndFlush();
        Environment.Exit(0);
    }

    private static void Test_NPCNameFinder(string[] args)
    {
        NpcNames types = NpcNames.None;
        int count = 100;

        foreach (string arg in args)
        {
            if (int.TryParse(arg, out int n))
                count = n;
            else if (Enum.TryParse(arg, true, out NpcNames flag))
                types |= flag;
        }

        if (types == NpcNames.None)
            types = NpcNames.Friendly | NpcNames.Neutral;

        using Test_NpcNameFinder test = new(logger, process, screen, loggerFactory, types, UseGpu, LogEachUpdate);
        int i = 0;

        long timestamp = Stopwatch.GetTimestamp();
        double[] sample = new double[count];

        double[] captures = new double[count];
        double[] updates = new double[count];

        Log.Logger.Information($"running {count} samples...");

        screen.Enabled = true;

        while (i < count)
        {
            if (LogOverallTimes)
                timestamp = Stopwatch.GetTimestamp();

            (captures[i], updates[i]) = test.Execute(delay);

            if (LogOverallTimes)
                sample[i] = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            i++;
            Thread.Sleep(4);
        }

        screen.Enabled = false;

        if (LogOverallTimes)
        {
            Array.Sort(sample);
            Array.Sort(captures);
            Array.Sort(updates);

            Log.Logger.Information($"overall | n: {count} | avg: {sample.Average():F2} | min: {sample.Min():F2} | p50: {Percentile(sample, 0.50):F2} | p95: {Percentile(sample, 0.95):F2} | p99: {Percentile(sample, 0.99):F2} | max: {sample.Max():F2}");
            Log.Logger.Information($"capture | n: {count} | avg: {captures.Average():F2} | min: {captures.Min():F2} | p50: {Percentile(captures, 0.50):F2} | p95: {Percentile(captures, 0.95):F2} | p99: {Percentile(captures, 0.99):F2} | max: {captures.Max():F2}");
            Log.Logger.Information($"updates | n: {count} | avg: {updates.Average():F2} | min: {updates.Min():F2} | p50: {Percentile(updates, 0.50):F2} | p95: {Percentile(updates, 0.95):F2} | p99: {Percentile(updates, 0.99):F2} | max: {updates.Max():F2}");
        }
    }

    private static void Test_Input(string[] args)
    {
        Test_Input test = new(logger, cts, process, screen, loggerFactory);
        test.Mouse_Movement();
        test.Mouse_Clicks();
        test.SendText();
    }

    private static void Test_CursorGrabber(string[] args)
    {
        using CursorClassifier classifier = new();
        int i = 5;
        while (i > 0)
        {
            Thread.Sleep(1000);

            classifier.Classify(out CursorType cursorType, out _);
            Log.Logger.Information($"{cursorType.ToString()}");

            i--;
        }
    }

    private static void Test_CursorCompare(string[] args)
    {
        using CursorClassifier classifier = new();
        const int count = 50;
        int i = 0;

        Span<double> times = stackalloc double[count];

        while (i < count)
        {
            Thread.Sleep(100);

            long startTime = Stopwatch.GetTimestamp();

            classifier.Classify(out CursorType cursorType, out double similarity);

            times[i] = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            Log.Logger.Information($"{cursorType.ToString()} {similarity} {times[i]:F6}ms");
            i++;
        }

        double sum = 0;
        double max = double.MinValue;
        double min = double.MaxValue;

        for (i = 0; i < times.Length - 1; i++)
        {
            double val = times[i];
            sum += val;
            if (val > max) max = val;
            else if (val < min) min = val;
        }

        Log.Logger.Information($"min:{min:F6} | max: {max:F5} | avg:{(sum / count):F6}");
    }

    private static void Test_MinimapNodeFinder(string[] args)
    {
        void nodeEvent(object sender, MinimapNodeEventArgs e)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("[{X},{Y}] {Amount}", e.X, e.Y, e.Amount);
        }

        Test_MinimapNodeFinder test = new(logger, screen, nodeEvent);

        int count = 100;
        int i = 0;

        long timestamp = Stopwatch.GetTimestamp();
        double[] sample = new double[count];

        Log.Logger.Information($"running {count} samples...");

        screen.MinimapEnabled = true;

        while (i < count)
        {
            if (LogOverallTimes)
                timestamp = Stopwatch.GetTimestamp();

            test.Execute();

            if (LogOverallTimes)
                sample[i] = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            i++;
            Thread.Sleep(delay);
        }

        screen.MinimapEnabled = false;

        if (LogOverallTimes)
            Log.Logger.Information($"sample: {count} | avg: {sample.Average():F2} | min: {sample.Min():F2} | max: {sample.Max():F2} | total: {sample.Sum()}");
    }

    private static void Test_FindTargetByCursor(string[] args)
    {
        ReadOnlySpan<CursorType> cursorType = [CursorType.Vendor];

        NpcNames types = NpcNames.None;
        int count = 2;

        foreach (string arg in args)
        {
            if (int.TryParse(arg, out int n))
                count = n;
            else if (Enum.TryParse(arg, true, out NpcNames flag))
                types |= flag;
        }

        if (types == NpcNames.None)
            types = NpcNames.Friendly | NpcNames.Neutral;

        using Test_NpcNameFinder test = new(logger, process, screen, loggerFactory, types, UseGpu, LogEachUpdate);
        int i = 0;

        screen.Enabled = true;

        while (i < count)
        {
            test.Execute(delay);
            if (test.Execute_FindTargetBy(cursorType))
            {
                break;
            }

            i++;
            Thread.Sleep(delay);
        }

        screen.Enabled = false;
    }

    private static void Test_PPather(string[] args)
    {
        string expansion = args.Length > 0 ? args[0] : "SoM";
        PPatherV2.PPatherV2 pPather = new(logger, DataConfig.Load(expansion));
    }

    /// <summary>
    /// The zone files are hand-editable and hot-reloaded by a file watcher, so
    /// the loader has to reject bad data rather than apply it. Each case here is
    /// something that reaches production code as a hang or a NaN cost, not just
    /// a wrong route.
    /// </summary>
    private static int Test_CostZoneValidation(Microsoft.Extensions.Logging.ILogger logger)
    {
        int failures = 0;
        string root = System.IO.Path.Join(System.IO.Path.GetTempPath(), "costzone_validation_" + Guid.NewGuid().ToString("N"));
        string continent = "Azeroth";
        string dir = System.IO.Path.Join(root, continent);
        System.IO.Directory.CreateDirectory(dir);

        void Case(string name, string json, bool expectAccepted)
        {
            string file = System.IO.Path.Join(dir, "1.json");
            System.IO.File.WriteAllText(file, json);

            bool accepted = PPather.Navmesh.CostZoneLoader.TryLoad(
                logger, root, continent, out PPather.Navmesh.CostZones _);

            if (accepted != expectAccepted)
            {
                logger.LogError("CostZoneValidation: '{Case}' expected accepted={Expected}, got {Actual}",
                    name, expectAccepted, accepted);
                failures++;
            }
        }

        try
        {
            Case("well-formed road",
                """{"Roads":[{"Name":"a","Width":2,"Points":[{"X":100,"Y":100},{"X":200,"Y":200}]}]}""", true);

            // Truncated mid-write - exactly what the watcher can catch.
            Case("truncated json",
                """{"Roads":[{"Name":"a","Width":2,"Points":[{"X":100,""", false);

            Case("NaN coordinate",
                """{"Roads":[{"Name":"a","Width":2,"Points":[{"X":"NaN","Y":100}]}]}""", false);

            Case("zero width",
                """{"Roads":[{"Name":"a","Width":0,"Points":[{"X":100,"Y":100}]}]}""", false);

            Case("no points",
                """{"Roads":[{"Name":"a","Width":2,"Points":[]}]}""", false);

            Case("out-of-world coordinate",
                """{"Roads":[{"Name":"a","Width":2,"Points":[{"X":1e9,"Y":100}]}]}""", false);

            // Would rasterize ~10^15 chunks and hang the reload thread.
            System.IO.File.WriteAllText(System.IO.Path.Join(dir, "1.json"), """{"Roads":[]}""");
            string dangerDir = System.IO.Path.Join(dir, PPather.Navmesh.CostZoneLoader.DangerZoneFolder);
            System.IO.Directory.CreateDirectory(dangerDir);

            void DangerCase(string name, string json, bool expectAccepted)
            {
                System.IO.File.WriteAllText(System.IO.Path.Join(dangerDir, "1.json"), json);
                bool accepted = PPather.Navmesh.CostZoneLoader.TryLoad(
                    logger, root, continent, out PPather.Navmesh.CostZones _);
                if (accepted != expectAccepted)
                {
                    logger.LogError("CostZoneValidation: '{Case}' expected accepted={Expected}, got {Actual}",
                        name, expectAccepted, accepted);
                    failures++;
                }
            }

            DangerCase("well-formed circle",
                """{"Circles":[{"Name":"c","CenterX":100,"CenterY":100,"Radius":50,"Penalty":40}],"Rectangles":[]}""", true);

            DangerCase("giant radius",
                """{"Circles":[{"Name":"c","CenterX":100,"CenterY":100,"Radius":1e9,"Penalty":40}],"Rectangles":[]}""", false);

            DangerCase("NaN penalty",
                """{"Circles":[{"Name":"c","CenterX":100,"CenterY":100,"Radius":50,"Penalty":"NaN"}],"Rectangles":[]}""", false);

            DangerCase("inverted rectangle",
                """{"Circles":[],"Rectangles":[{"Name":"r","MinX":500,"MinY":500,"MaxX":100,"MaxY":100,"Penalty":40}]}""", false);
        }
        finally
        {
            try { System.IO.Directory.Delete(root, recursive: true); } catch { /* temp dir */ }
        }

        if (failures == 0)
        {
            logger.LogInformation("CostZoneValidation: malformed zone files rejected, valid ones accepted");
        }

        return failures;
    }

    private static void Test_CostZones(string[] args)
    {
        // Pure rasterization checks - no game data, no navmesh required.
        int failures = 0;

        // A straight road with width 2 (2 * 33.33yd half-width).
        PPather.Graph.RoadSegment road = new("test-road", 2,
        [
            new System.Numerics.Vector2(-9800f, 800f),
            new System.Numerics.Vector2(-9800f, 400f),
        ]);

        PPather.Graph.RoadData roadData = new() { Roads = [road] };
        PPather.Navmesh.CostZones zones =
            PPather.Navmesh.CostZones.Build([roadData], []);

        if (zones.CostChunkCount == 0)
        {
            logger.LogError("CostZones: road rasterized to no chunks");
            failures++;
        }

        // A road must be cheaper than open ground, but nothing may ever be
        // cheaper than 1.0: Detour's A* heuristic assumes that, and pricing
        // below it truncates long routes.
        float onRoad = zones.CostFactor(-9800f, 600f);
        float offRoad = zones.CostFactor(-9800f + 5000f, 600f);

        if (onRoad >= offRoad)
        {
            logger.LogError("CostZones: road {OnRoad} should be cheaper than open ground {OffRoad}",
                onRoad, offRoad);
            failures++;
        }

        if (onRoad < 1f)
        {
            logger.LogError("CostZones: no chunk may cost below 1, road got {Factor}", onRoad);
            failures++;
        }

        // Avoid mode prices up but stays passable.
        PPather.Graph.DangerZoneData avoidData = new()
        {
            Circles =
            [
                new PPather.Graph.CircleDangerZone("camp", 1000f, 1000f, 100f, 500f)
                {
                    Mode = PPather.Graph.DangerZoneMode.Avoid
                }
            ]
        };

        PPather.Navmesh.CostZones avoid = PPather.Navmesh.CostZones.Build([], [avoidData]);
        if (avoid.CostFactor(1000f, 1000f) <= 1f)
        {
            logger.LogError("CostZones: avoid zone should cost more than 1");
            failures++;
        }

        if (avoid.IsBlocked(1000f, 1000f))
        {
            logger.LogError("CostZones: avoid zone must stay passable");
            failures++;
        }

        // Block mode is impassable inside and neutral outside.
        PPather.Graph.DangerZoneData blockData = new()
        {
            Rectangles =
            [
                new PPather.Graph.RectangleDangerZone("wall", 2000f, 2000f, 2200f, 2200f, 0f)
                {
                    Mode = PPather.Graph.DangerZoneMode.Block
                }
            ]
        };

        PPather.Navmesh.CostZones block = PPather.Navmesh.CostZones.Build([], [blockData]);
        if (!block.IsBlocked(2100f, 2100f))
        {
            logger.LogError("CostZones: block zone interior should be blocked");
            failures++;
        }

        if (block.IsBlocked(5000f, 5000f))
        {
            logger.LogError("CostZones: outside a block zone must stay open");
            failures++;
        }

        failures += Test_CostZoneValidation(logger);

        if (failures == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "CostZones: road/avoid/block rasterization OK ({Chunks} priced chunks, {Blocked} blocked)",
                    zones.CostChunkCount, block.BlockedChunkCount);
            }
        }
        else
        {
            logger.LogError("CostZones: {Failures} failures", failures);
        }
    }

    /// <summary>
    /// Route generation against the on-disk data, with the player stubbed - no game client.
    ///
    /// <code>
    /// CoreTests routegen
    /// CoreTests routegen --zone "Elwynn Forest" --mobs "Type:Humanoid &amp;&amp; !Elite"
    /// CoreTests routegen --subzone "Northshire Valley" --mobs "Name:Wolf || Name:Kobold"
    /// CoreTests routegen --profile "Json/class/_/Warrior_1-10.json" --output routegen-out
    /// </code>
    /// </summary>
    private static void Test_RouteGeneration(string[] args)
    {
        string zone = "Elwynn Forest";
        string? subzone = null;
        string mobs = string.Empty;
        int stops = 8;
        int level = 5;
        string race = "Human";
        string faction = "Alliance";
        int uiMapId = 0;
        string exp = "som";
        string focus = "Density";
        string mode = "Wander";
        string? profile = null;
        string? outputDir = null;
        string format = "svg";
        int? seedArg = null;

        // Loops to args.Length, not args.Length - 1: the old bound silently ignored any
        // flag in the final position, so "--output" at the end of the line produced nothing
        // and said nothing about it.
        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i].ToLowerInvariant();

            // Reads the value after a flag, or reports the flag as incomplete.
            bool Value(out string value)
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                    return true;
                }

                value = string.Empty;
                return false;
            }

            switch (flag)
            {
                case "--zone": if (Value(out string z)) zone = z; else Missing(flag); break;
                case "--subzone": if (Value(out string sz)) subzone = sz; else Missing(flag); break;
                case "--mobs": if (Value(out string m)) mobs = m; else Missing(flag); break;
                case "--race": if (Value(out string r)) race = r; else Missing(flag); break;
                case "--faction": if (Value(out string f)) faction = f; else Missing(flag); break;
                case "--exp": if (Value(out string e)) exp = e; else Missing(flag); break;
                case "--focus": if (Value(out string fo)) focus = fo; else Missing(flag); break;
                case "--mode": if (Value(out string mo)) mode = mo; else Missing(flag); break;
                case "--profile": if (Value(out string p)) profile = p; else Missing(flag); break;

                case "--stops": if (Value(out string st)) stops = int.Parse(st); else Missing(flag); break;
                case "--level": if (Value(out string lv)) level = int.Parse(lv); else Missing(flag); break;
                case "--uimap": if (Value(out string um)) uiMapId = int.Parse(um); else Missing(flag); break;

                case "--svg":
                    // Kept as an alias: it predates --format, when SVG was the only output.
                    Log.Logger.Warning("--svg is now --output - still honoured");
                    goto case "--output";

                case "--output":
                    // A bare --output still means "write them"; only the folder is optional.
                    // Named routegen-out, not routegen: the source folder is RouteGen/, and
                    // Windows paths are case-insensitive, so an output folder called
                    // "routegen" is the same directory as the source. Deleting the output
                    // then deletes the source.
                    outputDir = Value(out string dir) && dir.Length > 0 ? dir : "routegen-out";
                    break;

                case "--format":
                    // Output encoding for --output. SVG stays the default: it is the only one
                    // that keeps the tiles as tiles and stays sharp at any zoom.
                    if (!Value(out string fmt))
                    {
                        Missing(flag);
                        break;
                    }

                    fmt = fmt.ToLowerInvariant();
                    if (fmt is "svg" or "png" or "webp")
                    {
                        format = fmt;
                    }
                    else
                    {
                        Log.Logger.Warning(
                            "--format {Value} is not svg|png|webp - keeping {Current}", fmt, format);
                    }
                    break;

                case "--seed":
                    // "random" is explicit on purpose: omitting --seed keeps the fixed
                    // default, so a harness run stays comparable to the last one unless
                    // asked otherwise.
                    if (!Value(out string seedText))
                    {
                        Missing(flag);
                        break;
                    }

                    seedArg = seedText.Equals("random", StringComparison.OrdinalIgnoreCase)
                        ? Random.Shared.Next()
                        : int.Parse(seedText);
                    break;

                default:
                    if (flag.StartsWith("--", StringComparison.Ordinal))
                        Log.Logger.Warning("Unknown option {Flag}", args[i]);
                    break;
            }
        }

        void Missing(string flag) =>
            Log.Logger.Warning("{Flag} needs a value - ignored", flag);

        if (outputDir != null)
        {
            outputDir = System.IO.Path.GetFullPath(outputDir);
            Log.Logger.Information("{Format} output: {Dir}", format, outputDir);
        }
        else if (format != "svg")
        {
            Log.Logger.Warning("--format {Format} does nothing without --output <dir>", format);
        }

        using Test_RouteGen test = new(logger, loggerFactory, level, race, faction, uiMapId, exp);

        if (profile != null)
        {
            test.TestProfile(profile, seedArg, outputDir, format);
            return;
        }

        test.TestExpressionScope();
        test.TestGenerate(zone, subzone, mobs, stops, Enum.Parse<RouteFocus>(focus, true),
            Enum.Parse<RouteGenMode>(mode, true), seedArg, outputDir, format);
    }

    private static void Test_NavmeshCoords(string[] args)
    {
        // Pure math checks - no game data required. Sign conventions in the
        // wow<->rc mapping are the likeliest navmesh bug source, so landmarks
        // and a full-grid index sweep are asserted here.
        (string name, System.Numerics.Vector3 wow)[] landmarks =
        [
            ("Stormwind gate", new(-9170.7f, 361.9f, 92.6f)),
            ("Orgrimmar bank", new(1631.5f, -4375.0f, 30.9f)),
            ("Goldshire", new(-9464.0f, 62.0f, 56.0f)),
            ("Booty Bay", new(-14297.0f, 530.0f, 8.0f)),
            ("Everlook", new(6721.0f, -4657.0f, 721.0f)),
        ];

        int failures = 0;

        foreach ((string name, System.Numerics.Vector3 wow) in landmarks)
        {
            System.Numerics.Vector3 roundTrip =
                PPather.Navmesh.NavmeshCoords.ToWow(PPather.Navmesh.NavmeshCoords.ToRc(wow));

            if (roundTrip != wow)
            {
                logger.LogError("{Name}: round-trip mismatch {Wow} -> {RoundTrip}", name, wow, roundTrip);
                failures++;
            }

            PPather.Navmesh.NavmeshCoords.GetTileIndex(wow.X, wow.Y, out int tx, out int tz);
            PPather.Navmesh.NavmeshCoords.GetTileWowBounds(tx, tz,
                out float minX, out float minY, out float maxX, out float maxY);

            if (wow.X < minX || wow.X >= maxX || wow.Y < minY || wow.Y >= maxY)
            {
                logger.LogError("{Name}: tile ({TileX},{TileZ}) bounds [{MinX},{MinY}]..[{MaxX},{MaxY}] exclude {Wow}",
                    name, tx, tz, minX, minY, maxX, maxY, wow);
                failures++;
            }

            if (!PPather.Navmesh.NavmeshCoords.IsValidTile(tx, tz))
            {
                logger.LogError("{Name}: tile ({TileX},{TileZ}) out of range", name, tx, tz);
                failures++;
            }
        }

        // Full-grid inversion sweep: index -> bounds center -> same index.
        for (int tx = 0; tx < PPather.Navmesh.NavmeshSettings.TilesPerSide; tx += 5)
        {
            for (int tz = 0; tz < PPather.Navmesh.NavmeshSettings.TilesPerSide; tz += 5)
            {
                PPather.Navmesh.NavmeshCoords.GetTileWowBounds(tx, tz,
                    out float minX, out float minY, out float maxX, out float maxY);

                float cx = (minX + maxX) * 0.5f;
                float cy = (minY + maxY) * 0.5f;

                PPather.Navmesh.NavmeshCoords.GetTileIndex(cx, cy, out int rtx, out int rtz);
                if (rtx != tx || rtz != tz)
                {
                    logger.LogError("Tile inversion mismatch: ({TileX},{TileZ}) -> center ({CenterX},{CenterY}) -> ({RTileX},{RTileZ})",
                        tx, tz, cx, cy, rtx, rtz);
                    failures++;
                }
            }
        }

        if (failures == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("NavmeshCoords: all landmark round-trips, tile bounds and {Count} grid inversions OK",
                    (PPather.Navmesh.NavmeshSettings.TilesPerSide / 5) * (PPather.Navmesh.NavmeshSettings.TilesPerSide / 5));
            }
        }
        else
        {
            logger.LogError("NavmeshCoords: {Failures} failures", failures);
        }
    }

    private static double Percentile(double[] sorted, double p)
    {
        double index = p * (sorted.Length - 1);
        int lower = (int)index;
        double fraction = index - lower;

        if (lower + 1 < sorted.Length)
            return sorted[lower] + fraction * (sorted[lower + 1] - sorted[lower]);

        return sorted[lower];
    }
}
