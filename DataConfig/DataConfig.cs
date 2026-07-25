using Newtonsoft.Json;

using System.IO;

using static Newtonsoft.Json.JsonConvert;
using static System.IO.File;
using static System.IO.Path;

public static class DataConfigMeta
{
    public const int Version = 14;
    public const string DefaultFileName = "data_config.json";
}

public sealed class DataConfig
{
    public int Version = DataConfigMeta.Version;
    public string Root { get; set; } = Join("..", "json");

    [JsonIgnore]
    public string Class => Join(Root, "class");
    [JsonIgnore]
    public string Path => Join(Root, "path");
    [JsonIgnore]
    public string ExpDbc => Join(Root, "dbc", Exp);
    [JsonIgnore]
    public string PathInfo => Join(Root, "PathInfo");
    [JsonIgnore]
    public string Navmesh => Join(Root, "PathInfo", "navmesh", ClientEra(Exp));
    [JsonIgnore]
    public string MPQ => Join(Root, "MPQ");
    [JsonIgnore]
    public string Road => Join(Root, "road", ClientEra(Exp));
    [JsonIgnore]
    public string ExpArea => Join(Root, "area", Exp);
    [JsonIgnore]
    public string PPather => Join(Root, "PPather");
    [JsonIgnore]
    public string Screenshot => Join(Root, "cap");
    [JsonIgnore]
    public string ExpHistory => Join(Root, "History", Exp);
    [JsonIgnore]
    public string ExpExperience => Join(Root, "experience", Exp);
    [JsonIgnore]
    public string Leaflet => Join(Root, "leaflet", ClientEra(Exp));
    [JsonIgnore]
    public string AreaGrid => Join(Root, "area_grid", ClientEra(Exp));

    /// <summary>
    /// Groups clients whose art/geometry is shared, so geometry-derived assets
    /// (leaflet tiles, navmesh, area grids, cost zones) live in one folder per
    /// era instead of one per expansion. Mirrors PPather NavmeshSettings.MeshEra:
    /// vanilla..wotlk (incl. their legacy_* clients) read the same pre-Cataclysm
    /// world -> "precata"; Cataclysm rewrote it and switched MPQ -> CASC -> "cata".
    /// An unrecognized client gets its own era.
    /// </summary>
    public static string ClientEra(string client)
    {
        return client.ToLowerInvariant() switch
        {
            "vanilla" or "classic" or "som" or "tbc" or "bcc" or "wrath" or "wotlk"
                or "legacy_vanilla" or "legacy_tbc" or "legacy_wrath" => "precata",
            "cata" or "mop" or "legacy_cata" or "legacy_mop" => "cata",
            _ => client.ToLowerInvariant(),
        };
    }
    [JsonIgnore]
    public string Subzones => Join(Root, "subzones", Exp);

    [JsonIgnore]
    public string NpcSpawnLocations => Join(Root, "npcspawnlocations", Exp);

    [JsonIgnore]
    public string MailboxLocations => Join(Root, "mailboxlocations", Exp);

    [JsonIgnore]
    public string Mail => Join(Root, "mail");

    // at runtime - determined from the running exe file version
    [JsonIgnore]
    public string Exp { get; set; } = "wrath"; // hardcoded default

    public static DataConfig Load()
    {
        if (File.Exists(DataConfigMeta.DefaultFileName))
        {
            var loaded = DeserializeObject<DataConfig>(ReadAllText(DataConfigMeta.DefaultFileName));
            if (loaded.Version == DataConfigMeta.Version)
                return loaded;
        }

        return new DataConfig().Save();
    }

    public static DataConfig Load(string client)
    {
        if (File.Exists(DataConfigMeta.DefaultFileName))
        {
            var loaded = DeserializeObject<DataConfig>(ReadAllText(DataConfigMeta.DefaultFileName));
            if (loaded.Version == DataConfigMeta.Version)
            {
                loaded.Exp = client.ToLowerInvariant();
                return loaded;
            }
        }

        DataConfig newConfig = new DataConfig().Save();
        newConfig.Exp = client.ToLowerInvariant();
        return newConfig;
    }

    private DataConfig Save()
    {
        WriteAllText(DataConfigMeta.DefaultFileName, SerializeObject(this));

        return this;
    }

    public void DeletePPatherCache()
    {
        if (!Directory.Exists(PathInfo))
        {
            return;
        }

        var directories = Directory.GetDirectories(PathInfo);
        foreach (string directory in directories)
        {
            Directory.Delete(directory, true);
        }
    }
}