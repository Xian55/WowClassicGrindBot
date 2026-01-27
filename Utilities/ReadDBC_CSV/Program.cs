using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using SharedLib;

namespace ReadDBC_CSV;

internal sealed class Program
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:25.0) Gecko/20100101 Firefox/25.0";

    // Default version if not specified
    private const ClientVersion DefaultVersion = ClientVersion.SoM;

    // Map ClientVersion to wago.tools build strings
    private static readonly Dictionary<ClientVersion, string> VersionBuilds = new()
    {
        { ClientVersion.SoM, "1.15.8.63829" },      // Season of Discovery / SoM
        { ClientVersion.TBC, "2.5.4.44833" },       // TBC Classic
        { ClientVersion.Wrath, "3.4.5.63697" },     // WotLK Classic
        { ClientVersion.Cata, "4.4.2.60895" },      // Cataclysm Classic
        { ClientVersion.Mop, "5.5.1.63698" },       // MoP Remix

        { ClientVersion.Legacy_Cata, "8.1.0.27826" }, // Legacy Cata data
    };

    // Available extractors with their names
    private static readonly string[] ExtractorNames =
    [
        "faction",
        "item",
        "consumable",
        "spell",
        "icon",
        "talent",
        "worldmap"
    ];

    public static async Task Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return;
        }

        // Parse version
        ClientVersion version = ParseVersion(args);
        if (!VersionBuilds.TryGetValue(version, out string? build))
        {
            Console.WriteLine($"Error: No build mapping for version '{version}'");
            Console.WriteLine($"Available versions: {string.Join(", ", VersionBuilds.Keys.Select(v => v.ToString().ToLowerInvariant()))}");
            return;
        }

        // Parse optional build override
        string? buildOverride = ParseBuild(args);
        if (buildOverride != null)
        {
            build = buildOverride;
        }

        // Parse extractors to run
        HashSet<string> extractorsToRun = ParseExtractors(args);
        bool runAll = extractorsToRun.Count == 0;

        string versionName = version.ToString().ToLowerInvariant();
        Console.WriteLine($"Version: {versionName} (build {build})");
        Console.WriteLine($"Extractors: {(runAll ? "all" : string.Join(", ", extractorsToRun))}");
        Console.WriteLine();

        // Setup paths relative to the project root
        string projectRoot = GetProjectRoot();
        string dataPath = Path.Join(projectRoot, "data");
        string outputPath = Path.GetFullPath(Path.Join(projectRoot, "..", "..", "Json", "dbc", versionName));

        Console.WriteLine($"Data path: {dataPath}");
        Console.WriteLine($"Output path: {outputPath}");
        Console.WriteLine();

        // Clean CSV files if requested
        if (args.Contains("--clean") || args.Contains("-c"))
        {
            CleanDataFolder(dataPath);
        }

        Directory.CreateDirectory(dataPath);
        Directory.CreateDirectory(outputPath);

        // Track generated files for copying
        List<string> generatedFiles = [];

        // Run extractors
        if (runAll || extractorsToRun.Contains("faction"))
        {
            generatedFiles.AddRange(await RunExtractor(
                new FactionTemplateExtractor(dataPath),
                dataPath, build,
                ["factiontemplate.json"]));
        }

        if (runAll || extractorsToRun.Contains("item"))
        {
            generatedFiles.AddRange(await RunExtractor(
                new ItemExtractor(dataPath),
                dataPath, build,
                ["items.json"]));
        }

        if (runAll || extractorsToRun.Contains("consumable"))
        {
            string foodDesc = "Restores $o1 health over $d";
            string waterDesc = Version.TryParse(build, out Version? v) && v.Major == 1
                ? "Restores $o1 mana over $d"
                : "mana over $d";

            generatedFiles.AddRange(await RunExtractor(
                new ConsumablesExtractor(dataPath, foodDesc, waterDesc),
                dataPath, build,
                ["foods.json", "waters.json"]));
        }

        if (runAll || extractorsToRun.Contains("spell"))
        {
            generatedFiles.AddRange(await RunExtractor(
                new SpellExtractor(dataPath),
                dataPath, build,
                ["spells.json"]));
        }

        if (runAll || extractorsToRun.Contains("icon"))
        {
            generatedFiles.AddRange(await RunExtractor(
                new IconExtractor(dataPath),
                dataPath, build,
                ["spelliconmap.json", "iconnames.json"]));
        }

        if (runAll || extractorsToRun.Contains("talent"))
        {
            generatedFiles.AddRange(await RunExtractor(
                new TalentExtractor(dataPath),
                dataPath, build,
                ["talents.json"]));
        }

        if (runAll || extractorsToRun.Contains("worldmap"))
        {
            generatedFiles.AddRange(await RunExtractor(
                new WorldMapAreaExtractor(dataPath),
                dataPath, build,
                ["worldmaparea.json"]));
        }

        // Copy generated files to output directory
        Console.WriteLine();
        Console.WriteLine($"Copying {generatedFiles.Count} files to {outputPath}");

        foreach (string file in generatedFiles)
        {
            string sourcePath = Path.Join(dataPath, file);
            string destPath = Path.Join(outputPath, file);

            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                Console.WriteLine($"  {file}");
            }
            else
            {
                Console.WriteLine($"  {file} (not found, skipped)");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Done!");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ReadDBC_CSV - Extract WoW DBC data to JSON");
        Console.WriteLine();
        Console.WriteLine("Usage: ReadDBC_CSV [options] [extractors...]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -v, --version <version>  Game version (default: som)");
        Console.WriteLine("  -b, --build <build>      Override build version (e.g., 3.4.3.12345)");
        Console.WriteLine("  -c, --clean              Clean downloaded CSV files before running");
        Console.WriteLine("  -h, --help               Show this help");
        Console.WriteLine();
        Console.WriteLine("Versions:");
        foreach (var (ver, build) in VersionBuilds)
        {
            string name = ver.ToString().ToLowerInvariant();
            string isDefault = ver == DefaultVersion ? " (default)" : "";
            Console.WriteLine($"  {name,-15} {build}{isDefault}");
        }
        Console.WriteLine();
        Console.WriteLine("Extractors:");
        foreach (string name in ExtractorNames)
        {
            Console.WriteLine($"  {name}");
        }
        Console.WriteLine();
        Console.WriteLine("If no extractors specified, all will run.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ReadDBC_CSV                              # Run all for default version");
        Console.WriteLine("  ReadDBC_CSV -v wrath                     # Run all for WotLK");
        Console.WriteLine("  ReadDBC_CSV -v wrath -b 3.4.3.54261      # WotLK with custom build");
        Console.WriteLine("  ReadDBC_CSV item consumable              # Run only item and consumable");
        Console.WriteLine("  ReadDBC_CSV -v tbc spell talent          # Run spell and talent for TBC");
    }

    private static ClientVersion ParseVersion(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-v" || args[i] == "--version")
            {
                string versionArg = args[i + 1].ToLowerInvariant();

                foreach (ClientVersion cv in VersionBuilds.Keys)
                {
                    if (cv.ToString().ToLowerInvariant() == versionArg)
                        return cv;
                }

                Console.WriteLine($"Warning: Unknown version '{versionArg}', using default '{DefaultVersion}'");
                break;
            }
        }

        return DefaultVersion;
    }

    private static string? ParseBuild(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-b" || args[i] == "--build")
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static HashSet<string> ParseExtractors(string[] args)
    {
        HashSet<string> extractors = new(StringComparer.OrdinalIgnoreCase);

        bool skipNext = false;
        foreach (string arg in args)
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (arg.StartsWith('-'))
            {
                if (arg is "-v" or "--version" or "-b" or "--build")
                    skipNext = true;
                continue;
            }

            // Check if it's a valid extractor name
            string lower = arg.ToLowerInvariant();
            if (ExtractorNames.Contains(lower))
            {
                extractors.Add(lower);
            }
            else
            {
                Console.WriteLine($"Warning: Unknown extractor '{arg}', ignoring");
            }
        }

        return extractors;
    }

    private static string GetProjectRoot()
    {
        // Find project root by looking for ReadDBC_CSV.csproj
        string? dir = AppContext.BaseDirectory;

        // When running via dotnet run, we're in bin/Debug/net*/
        // Walk up to find the project root
        while (dir != null)
        {
            if (File.Exists(Path.Join(dir, "ReadDBC_CSV.csproj")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        // Fallback to current directory
        return Directory.GetCurrentDirectory();
    }

    private static void CleanDataFolder(string dataPath)
    {
        if (!Directory.Exists(dataPath))
            return;

        string[] csvFiles = Directory.GetFiles(dataPath, "*.csv");
        if (csvFiles.Length == 0)
        {
            Console.WriteLine("No CSV files to clean.");
            return;
        }

        Console.WriteLine($"Cleaning {csvFiles.Length} CSV files...");
        foreach (string file in csvFiles)
        {
            File.Delete(file);
        }
        Console.WriteLine();
    }

    private static async Task<List<string>> RunExtractor(
        IExtractor extractor,
        string dataPath,
        string build,
        string[] outputFiles)
    {
        Console.WriteLine($"--- {extractor.GetType().Name} ---");

        try
        {
            await DownloadRequirements(dataPath, extractor, build);
            extractor.Run();
            return [.. outputFiles];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return [];
        }
    }

    #region Download files

    private static async Task DownloadRequirements(string path, IExtractor extractor, string build)
    {
        using HttpClient client = new();
        client.DefaultRequestHeaders.Add("user-agent", UserAgent);

        foreach (string file in extractor.FileRequirement)
        {
            string output = Path.Join(path, file);
            if (File.Exists(output))
            {
                Console.WriteLine($"  {file} - exists, skipping download");
                continue;
            }

            try
            {
                string url = DownloadURL(build, file);
                byte[] bytes = await client.GetByteArrayAsync(url);
                File.WriteAllBytes(output, bytes);

                Console.WriteLine($"  {file} - downloaded");
            }
            catch (Exception e)
            {
                if (File.Exists(output))
                {
                    File.Delete(output);
                }

                Console.WriteLine($"  {file} - {e.Message}");
            }

            await Task.Delay(Random.Shared.Next(100, 250));
        }
    }

    private static string DownloadURL(string build, string file)
    {
        string resource = file.Split(".")[0];
        return $"https://wago.tools/db2/{resource}/csv?build={build}";
    }

    #endregion
}
