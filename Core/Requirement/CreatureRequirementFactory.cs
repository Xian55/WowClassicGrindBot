using Core.Database;

using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Core;

/// <summary>
/// Compiles <see cref="RouteGenSettings.Mobs"/> - the same expression language the class
/// profile uses for <c>Requirements</c>, but over a different subject.
///
/// <para><b>Two scopes, one syntax.</b> <see cref="RequirementFactory"/>'s variables read
/// the <i>player</i> and decide whether a path runs. These read a <i>candidate creature
/// row</i> and decide which mobs shape the route's polygon. <c>"Level &lt; 5"</c> is legal
/// in both and means different things: here <c>Level</c> is the creature's level and the
/// player's is <c>PlayerLevel</c>, so the common case reads the way you would say it -
/// <c>Level &lt;= PlayerLevel + 2</c>. Player-only names such as <c>Health%</c> are simply
/// absent, so a misplaced one fails at parse time rather than silently.</para>
///
/// <para>This is a separate <see cref="ExpressionParser"/> instance rather than new entries
/// in the global map, which also sidesteps a live defect: <c>ParseParameterized</c>
/// dispatches by <c>text.Contains(key)</c> over an unordered dictionary, so short keys added
/// globally can be claimed by the wrong handler. A separate map cannot collide with
/// player-scope keys at all.</para>
/// </summary>
public sealed class CreatureRequirementFactory
{
    private const char SEP1 = ':';

    /// <summary>
    /// The row currently under test. The compiled delegates close over this instance, so
    /// evaluation is "assign cursor, call HasRequirement" - no per-row allocation and no
    /// re-parsing. Not thread safe by construction; generation is single threaded.
    /// </summary>
    private sealed class Cursor
    {
        public Creature Creature;
        public int SpawnCount;
    }

    private readonly Cursor cursor = new();
    private readonly ExpressionParser parser;

    public CreatureRequirementFactory(ILogger logger, IRouteGenPlayer player,
        FactionTemplateDB factionDB)
    {
        Cursor c = cursor;

        Dictionary<string, Func<int>> intVariables = new(StringComparer.OrdinalIgnoreCase)
        {
            // Creature scope. 'Level' deliberately shadows the player's.
            { "Level", () => (c.Creature.MinLevel + c.Creature.MaxLevel) / 2 },
            { "MinLevel", () => c.Creature.MinLevel },
            { "MaxLevel", () => c.Creature.MaxLevel },
            { "Rank", () => c.Creature.Rank },
            { "NpcId", () => c.Creature.Entry },
            { "SpawnCount", () => c.SpawnCount },

            // The one player-scope value that belongs here, named so it cannot be mistaken.
            { "PlayerLevel", () => player.Level },
        };

        FrozenDictionary<string, Func<bool>> boolVariables =
            new Dictionary<string, Func<bool>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Elite", () => c.Creature.Rank > 0 },
                { "Skinnable", () => c.Creature.SkinLoot != 0 },
                { "Hostile", () => FactionExt.HostileToPlayer(
                    c.Creature, player.Faction, factionDB) },
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        FrozenDictionary<string, Func<ReadOnlySpan<char>, Requirement>> requirementMap =
            new Dictionary<string, Func<ReadOnlySpan<char>, Requirement>>()
            {
                { "Type:", CreateType },
                { "Name:", CreateName },
                { "Faction:", CreateFaction },
                { "Family:", CreateFamily },
            }.ToFrozenDictionary();

        parser = new ExpressionParser(intVariables, boolVariables, requirementMap, logger);
    }

    /// <summary>
    /// Compiles the expression once. Throws <see cref="InvalidOperationException"/> on an
    /// unknown name - including a player-scope name used here by mistake.
    /// </summary>
    public Requirement Parse(ReadOnlySpan<char> expression)
    {
        return parser.Parse(expression);
    }

    /// <summary>Evaluates a compiled expression against one creature row.</summary>
    public bool Matches(Requirement requirement, in Creature creature, int spawnCount)
    {
        cursor.Creature = creature;
        cursor.SpawnCount = spawnCount;
        return requirement.HasRequirement();
    }

    private Requirement CreateType(ReadOnlySpan<char> requirement)
    {
        // 'Type:_TYPE_' - same enum and parse as RequirementFactory's 'Target:'.
        int sep = requirement.IndexOf(SEP1);
        CreatureType type = Enum.Parse<CreatureType>(requirement[(sep + 1)..].Trim(), true);

        Cursor c = cursor;

        bool f() => c.Creature.Type == type;
        string s() => $"Type:{type}";

        return new Requirement { HasRequirement = f, LogMessage = s };
    }

    private Requirement CreateName(ReadOnlySpan<char> requirement)
    {
        // 'Name:_SUBSTRING_' - contains, case-insensitive. Substring rather than equals so
        // "Name:Wolf" covers Young Wolf / Rabid Wolf / Diseased Wolf without listing them.
        int sep = requirement.IndexOf(SEP1);
        string needle = requirement[(sep + 1)..].Trim().ToString();

        Cursor c = cursor;

        bool f() => c.Creature.Name != null &&
            c.Creature.Name.Contains(needle, StringComparison.OrdinalIgnoreCase);
        string s() => $"Name:{needle}";

        return new Requirement { HasRequirement = f, LogMessage = s };
    }

    private Requirement CreateFaction(ReadOnlySpan<char> requirement)
    {
        // 'Faction:_ID_' - the emulator faction template id. Selects "everything hostile
        // around here" without naming mobs, so it survives localisation and renames.
        int sep = requirement.IndexOf(SEP1);
        int faction = int.Parse(requirement[(sep + 1)..].Trim());

        Cursor c = cursor;

        bool f() => c.Creature.Faction == faction;
        string s() => $"Faction:{faction}";

        return new Requirement { HasRequirement = f, LogMessage = s };
    }

    private Requirement CreateFamily(ReadOnlySpan<char> requirement)
    {
        // 'Family:_ID_' - CreatureFamily, mostly useful to separate beast subtypes.
        int sep = requirement.IndexOf(SEP1);
        int family = int.Parse(requirement[(sep + 1)..].Trim());

        Cursor c = cursor;

        bool f() => c.Creature.Family == family;
        string s() => $"Family:{family}";

        return new Requirement { HasRequirement = f, LogMessage = s };
    }
}
