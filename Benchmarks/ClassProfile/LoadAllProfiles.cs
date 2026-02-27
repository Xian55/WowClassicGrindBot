using BenchmarkDotNet.Attributes;

using Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SharedLib;

using WinAPI;

using static Newtonsoft.Json.JsonConvert;

namespace Benchmarks.ClassProfile;

public class LoadAllProfiles
{
    private ServiceProvider serviceProvider = null!;
    private DataConfig dataConfig = null!;

    [Params(UnitRace.Human)]
    public UnitRace Race { get; set; }

    [Params(UnitClass.Warrior)]
    public UnitClass Class { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        SetWorkingDirectory();

        IServiceCollection services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ILogger>(sp =>
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(string.Empty));
        services.AddCoreLoadOnly("wrath");

        serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true });

        IAddonDataProvider provider = serviceProvider.GetRequiredService<IAddonDataProvider>();
        int raceClassVersion = (int)Race * 10000 + (int)Class * 100 + (int)ClientVersion.Wrath;
        provider.Data[46] = raceClassVersion;

        dataConfig = serviceProvider.GetRequiredService<DataConfig>();
    }

    [GlobalCleanup]
    public void Cleanup() => serviceProvider?.Dispose();

    [Benchmark]
    [ArgumentsSource(nameof(GetProfileNames))]
    public void LoadProfile(string profileName)
    {
        string json = File.ReadAllText(Path.Join(dataConfig.Class, profileName));
        ClassConfiguration classConfig = DeserializeObject<ClassConfiguration>(json)!;
        classConfig.Initialise(serviceProvider, []);
    }

    public static IEnumerable<string> GetProfileNames()
    {
        SetWorkingDirectory();

        DataConfig dc = DataConfig.Load();
        string root = Path.Join(dc.Class, Path.DirectorySeparatorChar.ToString());
        IOrderedEnumerable<string> files = Directory
            .EnumerateFiles(root, "*.json*", SearchOption.AllDirectories)
            .Select(path => path.Replace(root, string.Empty))
            .OrderBy(x => x, new NaturalStringComparer());

        foreach (string fileName in files)
        {
            yield return fileName;
        }
    }

    private static void SetWorkingDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MasterOfPuppets.sln")))
            dir = Path.GetDirectoryName(dir);

        Directory.SetCurrentDirectory(
            Path.Combine(dir ?? throw new DirectoryNotFoundException("Solution root not found"),
                "HeadlessServer"));
    }
}
