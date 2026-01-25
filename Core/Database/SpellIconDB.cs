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

public sealed class SpellIconDB
{
    private const string SpellIconMapFile = "spelliconmap.json";
    private const string IconNamesFile = "iconnames.json";
    private const string IconUrlBase = "https://render.worldofwarcraft.com/icons";

    private readonly SpellDB spellDB;

    public FrozenDictionary<int, int[]> IconToSpells { get; }
    public FrozenDictionary<int, string> IconNames { get; }

    public SpellIconDB(ILogger<SpellIconDB> logger, DataConfig dataConfig, SpellDB spellDB)
    {
        this.spellDB = spellDB;

        // Load spell icon map
        string spellMapPath = Join(dataConfig.ExpDbc, SpellIconMapFile);
        if (!File.Exists(spellMapPath))
        {
            logger.LogWarning("SpellIconDB: {path} not found. Spell validation disabled.", spellMapPath);
            IconToSpells = FrozenDictionary<int, int[]>.Empty;
            IconNames = FrozenDictionary<int, string>.Empty;
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

        logger.LogInformation("SpellIconDB: Loaded {count} texture mappings", IconToSpells.Count);

        // Load icon names
        string iconNamesPath = Join(dataConfig.ExpDbc, IconNamesFile);
        if (!File.Exists(iconNamesPath))
        {
            logger.LogWarning("SpellIconDB: {path} not found. Icon URLs unavailable.", iconNamesPath);
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

        logger.LogInformation("SpellIconDB: Loaded {count} icon names", IconNames.Count);

        // Set static reference for KeyReader spell name resolution
        KeyReader.SpellIconDB = this;
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

        return $"{IconUrlBase}/{size}/{iconName}.jpg";
    }

    /// <summary>
    /// Gets the icon URL for a spell ID by looking up its texture.
    /// </summary>
    public string? GetIconUrlForSpell(int spellId, int size = 56)
    {
        // Find texture ID for this spell
        foreach (var (textureId, spellIds) in IconToSpells)
        {
            if (Array.IndexOf(spellIds, spellId) >= 0)
            {
                return GetIconUrl(textureId, size);
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the texture ID(s) for a spell name. Used for reverse lookup.
    /// Returns all textures that could represent this spell.
    /// </summary>
    public List<int> GetTexturesForSpellName(string spellName)
    {
        List<int> textures = [];

        ReadOnlySpan<char> normalizedInput = GetBaseSpellName(spellName.AsSpan());

        foreach (var (textureId, spellIds) in IconToSpells)
        {
            foreach (int spellId in spellIds)
            {
                if (spellDB.Spells.TryGetValue(spellId, out Spell spell))
                {
                    ReadOnlySpan<char> normalizedSpell = GetBaseSpellName(spell.Name);
                    if (normalizedInput.Equals(normalizedSpell, StringComparison.OrdinalIgnoreCase))
                    {
                        textures.Add(textureId);
                        break; // Found match for this texture, check next texture
                    }
                }
            }
        }

        return textures;
    }
}
