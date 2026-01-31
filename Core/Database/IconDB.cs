using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;

using static Newtonsoft.Json.JsonConvert;
using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

public sealed class IconDB
{
    private const string SpellIconMapFile = "spelliconmap.json";
    private const string IconNamesFile = "iconnames.json";

    private static readonly FrozenDictionary<CdnRegion, string> RegionUrls = new Dictionary<CdnRegion, string>
    {
        [CdnRegion.US] = "https://render-us.worldofwarcraft.com/icons",
        [CdnRegion.EU] = "https://render-eu.worldofwarcraft.com/icons",
        [CdnRegion.KR] = "https://render-kr.worldofwarcraft.com/icons",
        [CdnRegion.TW] = "https://render-tw.worldofwarcraft.com/icons",
    }.ToFrozenDictionary();

    private CdnRegion region = CdnRegion.US;
    public CdnRegion Region
    {
        get => region;
        set
        {
            region = value;
            iconUrlBase = RegionUrls.GetValueOrDefault(value, RegionUrls[CdnRegion.US]);
        }
    }

    private string iconUrlBase = RegionUrls[CdnRegion.US];

    private readonly SpellDB spellDB;

    public FrozenDictionary<int, int[]> IconToSpells { get; }
    public FrozenDictionary<int, string> IconNames { get; }

    // Reverse index: spell name (lowercase, no rank) -> texture IDs
    private readonly FrozenDictionary<string, int[]> spellNameToTextures;

    // Reverse index: spell ID -> texture ID
    private readonly FrozenDictionary<int, int> spellIdToTexture;

    // Precomputed family textures for dynamic icon spells
    // Key: spell name prefix/suffix pattern, Value: all texture IDs for that family
    private readonly int[] aspectTextures;
    private readonly int[] auraTextures;

    public IconDB(ILogger<IconDB> logger, DataConfig dataConfig, SpellDB spellDB)
    {
        this.spellDB = spellDB;

        // Load spell icon map
        string spellMapPath = Join(dataConfig.ExpDbc, SpellIconMapFile);
        if (!File.Exists(spellMapPath))
        {
            logger.LogWarning("IconDB: {path} not found. Spell validation disabled.", spellMapPath);
            IconToSpells = FrozenDictionary<int, int[]>.Empty;
            IconNames = FrozenDictionary<int, string>.Empty;
            spellNameToTextures = FrozenDictionary<string, int[]>.Empty;
            spellIdToTexture = FrozenDictionary<int, int>.Empty;
            aspectTextures = [];
            auraTextures = [];
            return;
        }

        Dictionary<string, int[]> rawSpellMap = DeserializeObject<Dictionary<string, int[]>>(
            ReadAllText(spellMapPath))!;

        var spellMapBuilder = new Dictionary<int, int[]>(rawSpellMap.Count);
        foreach (var kvp in rawSpellMap)
        {
            spellMapBuilder[int.Parse(kvp.Key)] = kvp.Value;
        }

        IconToSpells = spellMapBuilder.ToFrozenDictionary();

        // Build reverse indexes
        var nameToTexturesBuilder = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var idToTextureBuilder = new Dictionary<int, int>(IconToSpells.Count * 5); // estimate ~5 spells per texture

        foreach (var (textureId, spellIds) in spellMapBuilder)
        {
            foreach (int spellId in spellIds)
            {
                // Reverse: spellId -> textureId
                idToTextureBuilder.TryAdd(spellId, textureId);

                // Reverse: spellName -> textureIds
                if (spellDB.Spells.TryGetValue(spellId, out Spell spell))
                {
                    string baseName = GetBaseSpellNameString(spell.Name);
                    if (!nameToTexturesBuilder.TryGetValue(baseName, out var list))
                    {
                        list = [];
                        nameToTexturesBuilder[baseName] = list;
                    }
                    if (!list.Contains(textureId))
                        list.Add(textureId);
                }
            }
        }

        spellNameToTextures = nameToTexturesBuilder
            .ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        spellIdToTexture = idToTextureBuilder.ToFrozenDictionary();

        // Build family textures for dynamic icon spells
        HashSet<int> aspectTextureSet = [];
        HashSet<int> auraTextureSet = [];

        foreach (var (spellName, textureIds) in spellNameToTextures)
        {
            if (spellName.StartsWith("Aspect of", StringComparison.OrdinalIgnoreCase))
            {
                foreach (int id in textureIds)
                    aspectTextureSet.Add(id);
            }
            else if (spellName.EndsWith(" Aura", StringComparison.OrdinalIgnoreCase))
            {
                foreach (int id in textureIds)
                    auraTextureSet.Add(id);
            }
        }

        aspectTextures = [.. aspectTextureSet];
        auraTextures = [.. auraTextureSet];

        logger.LogDebug("IconDB: Built {aspectCount} aspect textures, {auraCount} aura textures",
            aspectTextures.Length, auraTextures.Length);

        logger.LogInformation("IconDB: Loaded {count} texture mappings", IconToSpells.Count);

        // Load icon names
        string iconNamesPath = Join(dataConfig.ExpDbc, IconNamesFile);
        if (!File.Exists(iconNamesPath))
        {
            logger.LogWarning("IconDB: {path} not found. Icon URLs unavailable.", iconNamesPath);
            IconNames = FrozenDictionary<int, string>.Empty;
            return;
        }

        Dictionary<string, string> rawIconNames = DeserializeObject<Dictionary<string, string>>(
            ReadAllText(iconNamesPath))!;

        var iconNamesBuilder = new Dictionary<int, string>(rawIconNames.Count);
        foreach (var kvp in rawIconNames)
        {
            iconNamesBuilder[int.Parse(kvp.Key)] = kvp.Value;
        }

        IconNames = iconNamesBuilder.ToFrozenDictionary();

        logger.LogInformation("IconDB: Loaded {count} icon names", IconNames.Count);

        // Set static reference for KeyReader spell name resolution
        KeyReader.IconDB = this;
    }

    /// <summary>
    /// Gets all spell IDs that use a given texture/icon ID.
    /// Returns the stored array directly - do not modify.
    /// </summary>
    public ReadOnlySpan<int> GetSpellIds(int textureId)
    {
        if (IconToSpells.TryGetValue(textureId, out int[]? spellIds))
            return spellIds;

        return ReadOnlySpan<int>.Empty;
    }

    /// <summary>
    /// Checks if a spell ID uses the given texture ID.
    /// Zero allocation.
    /// </summary>
    public bool SpellUsesTexture(int spellId, int textureId)
    {
        ReadOnlySpan<int> spellIds = GetSpellIds(textureId);
        return spellIds.IndexOf(spellId) >= 0;
    }

    /// <summary>
    /// Checks if a spell name (any rank) uses the given texture ID.
    /// Zero allocation in the common path.
    /// </summary>
    public bool SpellNameUsesTexture(ReadOnlySpan<char> spellName, int textureId)
    {
        ReadOnlySpan<int> spellIds = GetSpellIds(textureId);
        if (spellIds.IsEmpty)
            return false;

        // Trim and get base name span (before any parenthesis)
        ReadOnlySpan<char> normalizedInput = GetBaseSpellName(spellName);

        foreach (int spellId in spellIds)
        {
            if (spellDB.Spells.TryGetValue(spellId, out Spell spell))
            {
                ReadOnlySpan<char> normalizedSpell = GetBaseSpellName(spell.Name);
                if (normalizedInput.Equals(normalizedSpell, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Gets base spell name without rank suffix. Zero allocation.
    /// "Frostbolt (Rank 3)" -> "Frostbolt"
    /// </summary>
    private static ReadOnlySpan<char> GetBaseSpellName(ReadOnlySpan<char> name)
    {
        name = name.Trim();
        int parenIndex = name.IndexOf('(');
        if (parenIndex > 0)
        {
            return name[..parenIndex].TrimEnd();
        }
        return name;
    }

    /// <summary>
    /// Gets spell names for a texture ID. Allocates - use only for logging/display.
    /// </summary>
    public string[] GetSpellNamesForDisplay(int textureId)
    {
        ReadOnlySpan<int> spellIds = GetSpellIds(textureId);
        if (spellIds.IsEmpty)
            return [];

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (int spellId in spellIds)
        {
            if (spellDB.Spells.TryGetValue(spellId, out Spell spell))
            {
                // Use base name for deduplication
                ReadOnlySpan<char> baseName = GetBaseSpellName(spell.Name);
                names.Add(baseName.ToString());
            }
        }

        string[] result = new string[names.Count];
        names.CopyTo(result);
        return result;
    }

    /// <summary>
    /// Gets the icon name for a texture ID (e.g., "ability_ambush").
    /// </summary>
    public bool TryGetIconName(int textureId, out string? iconName)
    {
        return IconNames.TryGetValue(textureId, out iconName);
    }

    /// <summary>
    /// Gets the icon URL for a texture ID.
    /// Size: 18, 36, or 56 pixels.
    /// </summary>
    public string? GetIconUrl(int textureId, int size = 56)
    {
        if (!IconNames.TryGetValue(textureId, out string? iconName))
            return null;

        return $"{iconUrlBase}/{size}/{iconName}.jpg";
    }

    /// <summary>
    /// Gets the icon URL for a spell ID by looking up its texture.
    /// </summary>
    public string? GetIconUrlForSpell(int spellId, int size = 56)
    {
        if (spellIdToTexture.TryGetValue(spellId, out int textureId))
            return GetIconUrl(textureId, size);
        return null;
    }

    /// <summary>
    /// Gets the texture ID(s) for a spell name. Used for reverse lookup.
    /// Returns all textures that could represent this spell.
    /// </summary>
    public int[] GetTexturesForSpellName(string spellName)
    {
        string baseName = GetBaseSpellNameString(spellName);
        if (spellNameToTextures.TryGetValue(baseName, out int[]? textures))
            return textures;
        return [];
    }

    /// <summary>
    /// Gets base spell name without rank suffix. Allocates a string.
    /// "Frostbolt (Rank 3)" -> "Frostbolt"
    /// </summary>
    private static string GetBaseSpellNameString(string name)
    {
        int parenIndex = name.IndexOf('(');
        if (parenIndex > 0)
            return name[..parenIndex].TrimEnd();
        return name.Trim();
    }

    /// <summary>
    /// Gets precomputed texture IDs for a spell family (all Aspects or all Auras).
    /// Returns empty array if not a family spell.
    /// </summary>
    public int[] GetFamilyTextures(string spellName)
    {
        if (spellName.StartsWith("Aspect of", StringComparison.OrdinalIgnoreCase))
            return aspectTextures;
        if (spellName.EndsWith(" Aura", StringComparison.OrdinalIgnoreCase))
            return auraTextures;
        return [];
    }

    /// <summary>
    /// Checks if the spell has a dynamic icon (Aspects, Auras).
    /// </summary>
    public static bool HasDynamicIcon(string name)
    {
        if (name.StartsWith("Aspect of", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(" Aura", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
