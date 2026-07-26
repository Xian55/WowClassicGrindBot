using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PPather;

using SharedLib;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace BakeTool;

/// <summary>
/// Cross-platform navmesh bake CLI - the only bake entry point that is not
/// net10.0-windows, so macOS/Linux hosts can bake. Wraps the same
/// <see cref="PPatherService.StartBake"/> the HTTP API drives.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        string? exp = Value(args, "--exp");
        string? root = Value(args, "--root");
        string? continent = Value(args, "--continent");
        string? adt = Value(args, "--adt");

        if (string.IsNullOrWhiteSpace(exp))
        {
            Console.Error.WriteLine("--exp is required (e.g. legacy_mop, legacy_cata, wrath)");
            return 1;
        }

        // Resolve the data root before anything reads DataConfig: it is relative
        // ("../json") by default, which only works from a project directory.
        DataConfig cfg = string.IsNullOrWhiteSpace(root)
            ? DataConfig.Load(exp)
            : new DataConfig { Root = root, Exp = exp };

        ConsoleLogger<PPatherService> log = new();

        Console.WriteLine($"platform  : {System.Runtime.InteropServices.RuntimeInformation.OSDescription} " +
                          $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"exp / era : {cfg.Exp} / {DataConfig.ClientEra(cfg.Exp)}");
        Console.WriteLine($"root      : {cfg.Root}");
        Console.WriteLine($"MPQ       : {cfg.MPQ}");
        Console.WriteLine($"navmesh   : {cfg.Navmesh}");

        WorldMapAreaDB wmaDb;
        try
        {
            wmaDb = new WorldMapAreaDB(cfg);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not load {cfg.ExpDbc}: {e.Message}");
            Console.Error.WriteLine($"Generate it with:  ReadDBC_CSV -v {cfg.Exp}");
            return 2;
        }

        NavmeshBakeOptions bake = new();

        // MinWorldZ must match whatever baked the rest of the era, because it
        // feeds the settings hash - a mismatch silently writes to a different
        // cache directory. It also matters for correctness: Outland is a floating
        // landmass whose MPQ still carries the low "base" terrain, which bakes as
        // a walkable death-fall floor unless filtered. PathingAPI supplies this
        // from appsettings.json; mirror that default here and let --min-world-z
        // override, so a bake from this tool lands in the same directory as one
        // from the API.
        bake.MinWorldZ["Expansion01"] = -700f;

        string? minZ = Value(args, "--min-world-z");
        if (!string.IsNullOrWhiteSpace(minZ))
        {
            foreach (string entry in minZ.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] kv = entry.Split('=', StringSplitOptions.TrimEntries);
                if (kv.Length != 2 || !float.TryParse(kv[1], CultureInfo.InvariantCulture, out float z))
                {
                    Console.Error.WriteLine($"--min-world-z expects Continent=value pairs, got '{entry}'");
                    return 1;
                }

                bake.MinWorldZ[kv[0]] = z;
            }
        }

        foreach ((string c, float z) in bake.MinWorldZ)
        {
            Console.WriteLine($"minWorldZ : {c} = {z}");
        }

        PPatherService service = new(log, cfg, wmaDb,
            Options.Create(bake),
            Options.Create(new NavmeshQueryOptions()));

        // Area-id grids + subzone bounds. Separate from the navmesh bake: it reads
        // ADT area ids rather than geometry, and its output is what lets
        // GetAreaIdAndZ answer without the client afterwards. Synchronous, because
        // unlike a tile bake there is no per-tile progress worth polling.
        if (Array.IndexOf(args, "--area-grid") >= 0)
        {
            Console.WriteLine($"area grid : {cfg.AreaGrid}");
            Console.WriteLine($"subzones  : {cfg.Subzones}");
            Console.WriteLine($"baking    : {continent ?? "all continents"}");
            Console.WriteLine();

            long areaStart = System.Diagnostics.Stopwatch.GetTimestamp();
            bool ok = service.BuildAreaGrid(continent);
            Console.WriteLine($"\ntotal {System.Diagnostics.Stopwatch.GetElapsedTime(areaStart):hh\\:mm\\:ss}");

            return ok ? 0 : 4;
        }

        (int x, int y)? adtPair = null;
        if (!string.IsNullOrWhiteSpace(adt))
        {
            string[] parts = adt.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], CultureInfo.InvariantCulture, out int ax) ||
                !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int ay))
            {
                Console.Error.WriteLine("--adt expects two integers, e.g. --adt 48,32");
                return 1;
            }

            adtPair = (ax, ay);
        }

        if (adtPair.HasValue && string.IsNullOrWhiteSpace(continent))
        {
            Console.Error.WriteLine("--continent is required when baking a single --adt");
            return 1;
        }

        string scope = adtPair.HasValue
            ? $"adt {adtPair.Value.x},{adtPair.Value.y} of {continent}"
            : continent ?? "all continents";
        Console.WriteLine($"baking    : {scope}");
        Console.WriteLine();

        if (!service.StartBake(continent, adtPair))
        {
            Console.Error.WriteLine("A bake is already running.");
            return 3;
        }

        DateTime start = DateTime.UtcNow;
        DateTime lastReport = DateTime.UtcNow;
        string lastContinent = string.Empty;

        while (true)
        {
            Thread.Sleep(2000);
            PPatherService.NavmeshBakeStatus s = service.BakeStatus;

            if (s.Continent != lastContinent && s.Continent.Length > 0)
            {
                lastContinent = s.Continent;
                Console.WriteLine($"[{Elapsed(start)}] BEGIN {s.Continent} ({s.AdtTotal} ADTs)");
                lastReport = DateTime.UtcNow;
            }

            if (!s.Running)
            {
                Console.WriteLine($"[{Elapsed(start)}] DONE {s.Continent} " +
                                  $"adts={s.AdtDone}/{s.AdtTotal} tilesBaked={s.TilesDone} " +
                                  $"onDisk={s.TilesAlreadyOnDisk} msg={s.Message}");
                break;
            }

            if ((DateTime.UtcNow - lastReport).TotalMinutes >= 1)
            {
                lastReport = DateTime.UtcNow;
                double pct = s.AdtTotal > 0 ? 100.0 * s.AdtDone / s.AdtTotal : 0;
                Console.WriteLine($"[{Elapsed(start)}] {s.Continent} {pct:F1}% " +
                                  $"adts={s.AdtDone}/{s.AdtTotal} tiles={s.TilesDone}");
            }
        }

        Console.WriteLine($"\ntotal {Elapsed(start)}");
        return 0;
    }

    private static string Elapsed(DateTime start) =>
        (DateTime.UtcNow - start).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static string? Value(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
                return args[i + 1];

            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                return args[i][(name.Length + 1)..];
        }

        return null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            BakeTool - cross-platform navmesh bake (macOS / Linux / Windows)

              --exp <client>        required, e.g. legacy_mop, legacy_cata, wrath
              --root <dir>          data root; default is DataConfig's (../json)
              --continent <name>    one continent; omit to bake every continent
                                    the client has (Azeroth, Kalimdor,
                                    Expansion01, Northrend, HawaiiMainLand)
              --adt <x,y>           one ADT only; needs --continent
              --min-world-z <list>  Continent=z pairs, e.g. Expansion01=-700
                                    (that one is the default; it feeds the
                                    settings hash, so it must match whatever
                                    baked the rest of the era)
              --area-grid           bake area-id grids + subzone bounds instead
                                    of navmesh tiles. Writes area_grid/<era>/
                                    and subzones/<exp>/; this is what lets
                                    GetAreaIdAndZ answer without the client.
                                    Honours --continent; omit it for all.

            The data root must contain MPQ/ (the client's archives) and
            dbc/<exp>/ (generate with ReadDBC_CSV -v <exp>).

            Examples
              BakeTool --exp legacy_mop --root ~/wcgb-data --continent HawaiiMainLand
              BakeTool --exp legacy_mop --root ~/wcgb-data --continent Azeroth --adt 48,32
              BakeTool --exp legacy_mop --root ~/wcgb-data --area-grid
            """);
    }
}
