using BenchmarkDotNet.Attributes;

using WinAPI;

namespace Benchmarks.ClassProfile;

public class LoadAllProfiles
{
    [Benchmark]
    [ArgumentsSource(nameof(GetProfileNames))]
    public void LoadProfile(string profileName)
    {
        // TODO: fix loading error frame_config.json not exists
        HeadlessServer.Program.Main([$"{profileName}", "-m Local", "--loadonly"]);
    }

    public static IEnumerable<string> GetProfileNames()
    {
        var dataConfig = DataConfig.Load();

        Directory.SetCurrentDirectory("..\\..\\..\\..\\HeadlessServer");

        var root = Path.Join(dataConfig.Class, Path.DirectorySeparatorChar.ToString());
        var files = Directory.EnumerateFiles(root, "*.json*", SearchOption.AllDirectories)
            .Select(path => path.Replace(root, string.Empty))
            .OrderBy(x => x, new NaturalStringComparer());

        yield return files.First();

        //foreach (var fileName in files)
        //{
        //    yield return fileName;
        //}
    }
}
