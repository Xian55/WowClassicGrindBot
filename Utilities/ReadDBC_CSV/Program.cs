using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace ReadDBC_CSV;

public enum Locale
{
    enUS,
    koKR,
    frFR,
    deDE,
    zhCN,
    esES,
    zhTW,
    enGB,
    esMX,
    ruRU,
    ptBR,
    itIT
}

internal sealed class Program
{
    private const string userAgent = " Mozilla/5.0 (Windows NT 6.1; WOW64; rv:25.0) Gecko/20100101 Firefox/25.0";

    private const Locale locale = Locale.enUS;
    private const string path = "../../../data/";
    // SoM 1.14.4.51829
    // SoD 1.15.8.63829
    // TBCC 2.5.4.44833
    // WOTLK 3.4.5.63697
    // Cata 4.4.2.60895
    // Mop 5.5.1.63698
    private const string build = "1.15.8.63829";

    public static void Main()
    {
        MainAsync().GetAwaiter().GetResult();
    }

    private static async Task MainAsync()
    {
        await GenerateFactionTemplate(path);

        await GenerateItems(path);
        await GenerateConsumables(path);
        await GenerateSpells(path);
        await GenerateTalents(path);
        await GenerateWorldMapArea(path);
    }

    private static async Task GenerateFactionTemplate(string path)
    {
        FactionTemplateExtractor extractor = new(path);
        await DownloadRequirements(path, extractor, build);
        extractor.Run();
    }

    private static async Task GenerateItems(string path)
    {
        ItemExtractor extractor = new(path);
        await DownloadRequirements(path, extractor, build);
        extractor.Run();
    }

    private static async Task GenerateConsumables(string path)
    {
        string foodDesc = "Restores $o1 health over $d";
        string waterDesc = "mana over $d";

        if (Version.TryParse(build, out Version version) && version.Major == 1)
        {
            waterDesc = "Restores $o1 mana over $d";
        }

        ConsumablesExtractor extractor = new(path, foodDesc, waterDesc);
        await DownloadRequirements(path, extractor, build);
        extractor.Run();
    }

    private static async Task GenerateSpells(string path)
    {
        SpellExtractor extractor = new(path);
        await DownloadRequirements(path, extractor, build);
        extractor.Run();
    }

    private static async Task GenerateTalents(string path)
    {
        TalentExtractor extractor = new(path);
        await DownloadRequirements(path, extractor, build);
        extractor.Run();
    }

    private static async Task GenerateWorldMapArea(string path)
    {
        WorldMapAreaExtractor extractor = new(path);
        await DownloadRequirements(path, extractor, build);
        extractor.Run();
    }


    #region Download files

    private static async Task DownloadRequirements(string path, IExtractor extractor, string build)
    {
        using HttpClient client = new();
        client.DefaultRequestHeaders.Add("user-agent", userAgent);

        foreach (string file in extractor.FileRequirement)
        {
            string output = Path.Join(path, file);
            if (File.Exists(output))
            {
                Console.WriteLine($"{build} - {file} already exists. Skip downloading.");
                continue;
            }

            try
            {
                string url = DownloadURL(build, file);
                byte[] bytes = await client.GetByteArrayAsync(url);
                File.WriteAllBytes(output, bytes);

                Console.WriteLine($"{build} - {file} - Downloaded - {url}");
            }
            catch (Exception e)
            {
                if (File.Exists(output))
                {
                    File.Delete(output);
                }

                Console.WriteLine($"{build} - {file} - {e.Message}");
            }

            await Task.Delay(Random.Shared.Next(100, 250));
        }
    }

    private static string DownloadURL(string build, string file)
    {
        string resource = file.Split(".")[0];
        //return $"https://wow.tools/dbc/api/export/?name={resource}&build={build}&locale={locale}";
        return $"https://wago.tools/db2/{resource}/csv?&build={build}";
    }

    #endregion
}
