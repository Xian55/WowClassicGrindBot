using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace Core;

public class BadZone
{
    public int ZoneId { get; init; } = -1;
    public Vector3 ExitZoneLocation { get; init; }
}

public enum Mode
{
    Grind = 0,
    CorpseRun = 1,
    AttendedGather = 2,
    AttendedGrind = 3,
    AssistFocus = 4,
    AutoGather = 5
}


public sealed partial class ClassConfiguration
{
    public string FileName { get; set; } = string.Empty;

    public bool Log { get; set; } = true;
    public bool LogBagChanges { get; set; } = true;
    public bool Loot { get; set; } = true;
    public bool Skin { get; set; }
    public bool Herb { get; set; }
    public bool Mine { get; set; }
    public bool Salvage { get; set; }
    public bool GatherCorpse => Skin || Herb || Mine || Salvage;

    public bool UseMount { get; set; } = true;
    public bool KeyboardOnly { get; set; }
    public bool AllowPvP { get; set; }
    public bool TargetNeutral { get; set; }
    public bool AutoPetAttack { get; set; } = true;
    public bool CrossZoneSearch { get; set; }

    // Keeping this for backward compatibility
    // The following properties are consolidated under PathSettings
    public string PathFilename { get; set; } = string.Empty;
    public string? OverridePathFilename { get; set; } = string.Empty;
    public bool PathThereAndBack { get; set; } = true;
    public bool PathReduceSteps { get; set; }
    public List<string> SideActivityRequirements = [];
    public PathSettings[] Paths { get; set; } = [];

    public Mode Mode { get; set; } = Mode.Grind;

    public bool GatheringMode => Mode is Mode.AttendedGather or Mode.AutoGather;

    public BadZone WrongZone { get; } = new BadZone();

    public int NPCMaxLevels_Above { get; set; } = 1;
    public int NPCMaxLevels_Below { get; set; } = 7;
    public UnitClassification TargetMask { get; set; } =
        UnitClassification.Normal |
        UnitClassification.Trivial |
        UnitClassification.Rare;

    public bool CheckTargetGivesExp { get; set; }
    public string[] Blacklist { get; init; } = [];

    public Dictionary<int, SchoolMask> NpcSchoolImmunity { get; } = [];

    [JsonConverter(typeof(IntOrIntArrayDictionaryConverter))]
    public Dictionary<string, int[]> IntVariables { get; } = [];

    public Dictionary<string, string> StringVariables { get; } = [];

    public KeyActions Pull { get; } = new();
    public KeyActions Flee { get; } = new();
    public KeyActions Combat { get; } = new();
    public KeyActions Adhoc { get; } = new();
    public KeyActions Parallel { get; } = new();
    public KeyActions NPC { get; } = new();
    public KeyActions AssistFocus { get; } = new();
    public WaitKeyActions Wait { get; } = new();
    public FormKeyActions Form { get; } = new();

    /// <summary>
    /// Whether mail functionality is enabled for this profile.
    /// </summary>
    public bool Mail { get; set; }

    /// <summary>
    /// External mail configuration filename (relative to Json/mail/).
    /// If empty, uses inline MailConfig settings.
    /// </summary>
    public string MailFilename { get; set; } = string.Empty;

    /// <summary>
    /// Mail configuration settings (inline or loaded from external file).
    /// </summary>
    public MailConfiguration MailConfig { get; set; } = new();

    /// <summary>
    /// Runtime mail configuration overrides (from UI/localStorage).
    /// Takes priority over MailConfig when set. Not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    public MailConfiguration? RuntimeMailConfig { get; set; }

    public KeyAction[] GatherFindKeyConfig { get; set; } = Array.Empty<KeyAction>();
    public string[] GatherFindKeys { get; init; } = Array.Empty<string>();

    public ConsoleKey ForwardKey { get; init; } = ConsoleKey.UpArrow;
    public ConsoleKey BackwardKey { get; init; } = ConsoleKey.DownArrow;
    public ConsoleKey TurnLeftKey { get; init; } = ConsoleKey.LeftArrow;
    public ConsoleKey TurnRightKey { get; init; } = ConsoleKey.RightArrow;

    // Cached macro KeyActions for efficient re-resolution on action bar changes
    private readonly List<KeyAction> macroActions = [];
    public IReadOnlyList<KeyAction> MacroActions => macroActions;

    public void Initialise(IServiceProvider sp, Dictionary<int, string> overridePathFile)
    {
        Approach.Key = Interact.Key;
        AutoAttack.Key = Interact.Key;

        ILogger logger = sp.GetRequiredService<ILogger>();
        PlayerReader playerReader = sp.GetRequiredService<PlayerReader>();

        RecordInt globalTime = sp.GetRequiredService<AddonReader>().GlobalTime;

        if (Paths == Array.Empty<PathSettings>() &&
            !string.IsNullOrEmpty(PathFilename))
        {
            overridePathFile.TryGetValue(0, out string? firstoverridePath);
            OverridePathFilename = firstoverridePath ?? string.Empty;

            if (!string.IsNullOrEmpty(OverridePathFilename))
            {
                PathFilename = OverridePathFilename;
            }

            Paths =
            [
                new PathSettings()
                {
                    PathFilename = PathFilename,
                    OverridePathFilename = OverridePathFilename,
                    PathThereAndBack = PathThereAndBack,
                    PathReduceSteps = PathReduceSteps,
                    SideActivityRequirements = SideActivityRequirements
                }
            ];
        }

        DataConfig dataConfig = sp.GetRequiredService<DataConfig>();

        // Load mail config from external file if MailFilename is specified
        if (!string.IsNullOrEmpty(MailFilename))
        {
            string mailPath = Path.Join(dataConfig.Mail, MailFilename);
            if (File.Exists(mailPath))
            {
                MailConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MailConfiguration>(
                    File.ReadAllText(mailPath)) ?? new MailConfiguration();
                logger.LogInformation("Loaded mail config from {MailPath}", mailPath);
            }
            else
            {
                logger.LogWarning("Mail config file not found: {MailPath}", mailPath);
            }
        }

        for (int i = 0; i < Paths.Length; i++)
        {
            PathSettings settings = Paths[i];

            if (overridePathFile.TryGetValue(i, out string? overridePath))
                settings.OverridePathFilename = overridePath;

            if (!string.IsNullOrEmpty(settings.OverridePathFilename))
            {
                settings.PathFilename = settings.OverridePathFilename;
            }

            if (!File.Exists(Path.Join(dataConfig.Path, settings.PathFilename)))
            {
                if (!string.IsNullOrEmpty(OverridePathFilename))
                    throw new Exception(
                        $"[{nameof(ClassConfiguration)}.{nameof(Paths)}[{i}]] " +
                        $"`{settings.OverridePathFilename}` file does not exists!");
                else
                    throw new Exception(
                        $"[{nameof(ClassConfiguration)}.{nameof(Paths)}[{i}]] " +
                        $"`{settings.PathFilename}` file does not exists!");
            }

            settings.Init(globalTime, playerReader, i);
        }

        if (Paths.Select(x => x.Id).Distinct().Count() != Paths.Length)
        {
            throw new ArgumentException("One ore more PathSettings share the same Id. Must be unique!");
        }

        RequirementFactory factory = new(sp, this);

        var baseActionKeys = GetByType<KeyAction>();
        foreach ((string _, KeyAction keyAction) in baseActionKeys)
        {
            keyAction.Init(logger, Log, playerReader, globalTime);
            factory.Init(keyAction);
        }

        SetBaseActions(Pull,
            Interact, Approach, AutoAttack, StopAttack, PetAttack);

        SetBaseActions(Combat,
            Interact, Approach, AutoAttack, StopAttack, PetAttack);

        var groups = GetByTypeAsList<KeyActions>();

        foreach ((string name, KeyActions keyActions) in groups)
        {
            if (keyActions.Sequence.Length > 0)
            {
                LogInitBind(logger, name);
            }

            keyActions.InitBinds(logger, factory);

            if (keyActions is WaitKeyActions wait &&
                wait.AutoGenerateWaitForFoodAndDrink)
                wait.AddWaitKeyActionsForFoodOrDrink(logger, groups);
        }

        foreach ((string name, KeyActions keyActions) in groups)
        {
            if (keyActions.Sequence.Length > 0)
            {
                LogInitKeyActions(logger, name);
            }

            keyActions.Init(logger, Log,
                playerReader, globalTime, factory);
        }

        // Cache macro KeyActions (lowercase names) for efficient action bar change handling
        macroActions.Clear();
        foreach ((string _, KeyActions keyActions) in groups)
        {
            foreach (KeyAction action in keyActions.Sequence)
            {
                if (!string.IsNullOrEmpty(action.Name) && char.IsLower(action.Name[0]))
                {
                    macroActions.Add(action);
                }
            }
        }

        GatherFindKeyConfig = new KeyAction[GatherFindKeys.Length];
        for (int i = 0; i < GatherFindKeys.Length; i++)
        {
            KeyAction newAction = new()
            {
                Key = GatherFindKeys[i],
                Name = $"Profession {i}"
            };

            newAction.Init(logger, Log, playerReader, globalTime);
            factory.Init(newAction);

            GatherFindKeyConfig[i] = newAction;
        }

        if (CheckTargetGivesExp)
        {
            logger.LogWarning("CheckTargetGivesExp is enabled. NPCMaxLevels_Above and NPCMaxLevels_Below ignored!");
        }
        if (KeyboardOnly)
        {
            logger.LogWarning("KeyboardOnly mode is enabled. Mouse based actions ignored.");

            if (GatherCorpse)
                logger.LogWarning("GatherCorpse limited to the last target. Rest going to be skipped!");
        }

        // Mail configuration validation
        // Only warn (don't throw) since BlazorServer users can set recipient at runtime via UI
        if (Mail && !HasMailRecipient())
        {
            logger.LogWarning(
                "[Mail] Enabled but no recipient configured yet. " +
                "Set via UI (BlazorServer), {EnvVar} env var, " +
                "or RecipientName in config.", MailConfiguration.RecipientEnvVar);
        }
    }

    /// <summary>
    /// Gets the effective mail configuration (runtime overrides ?? persisted config).
    /// </summary>
    public MailConfiguration GetEffectiveMailConfig()
    {
        return RuntimeMailConfig ?? MailConfig;
    }

    /// <summary>
    /// Gets the effective recipient name from runtime config, env var, or persisted config.
    /// Priority: RuntimeMailConfig.RecipientName > MAIL_RECIPIENT env var > MailConfig.RecipientName
    /// </summary>
    public string GetEffectiveRecipientName()
    {
        // Priority 1: Runtime config recipient
        if (RuntimeMailConfig != null && !string.IsNullOrWhiteSpace(RuntimeMailConfig.RecipientName))
        {
            return RuntimeMailConfig.RecipientName;
        }

        // Priority 2: Environment variable
        string? envRecipient = Environment.GetEnvironmentVariable(MailConfiguration.RecipientEnvVar);
        if (!string.IsNullOrWhiteSpace(envRecipient))
        {
            return envRecipient;
        }

        // Priority 3: Persisted config
        return MailConfig.RecipientName;
    }

    /// <summary>
    /// Returns true if a valid recipient is configured (via runtime config, env var, or JSON).
    /// </summary>
    public bool HasMailRecipient()
    {
        return !string.IsNullOrWhiteSpace(GetEffectiveRecipientName());
    }

    /// <summary>
    /// Gets the effective list of excluded item IDs by merging persisted config with runtime exclusions.
    /// </summary>
    public int[] GetEffectiveExcludedItemIds()
    {
        if (RuntimeMailConfig == null || RuntimeMailConfig.ExcludedItemIds.Length == 0)
        {
            return MailConfig.ExcludedItemIds;
        }

        return [.. MailConfig.ExcludedItemIds.Union(RuntimeMailConfig.ExcludedItemIds)];
    }

    /// <summary>
    /// Gets a FrozenSet of effective excluded item IDs for efficient lookups.
    /// Merges persisted config with runtime exclusions.
    /// </summary>
    public FrozenSet<int> GetEffectiveExcludedItemIdSet()
    {
        if (RuntimeMailConfig == null || RuntimeMailConfig.ExcludedItemIds.Length == 0)
        {
            return MailConfig.ExcludedItemIdSet;
        }

        return MailConfig.ExcludedItemIds.Union(RuntimeMailConfig.ExcludedItemIds).ToFrozenSet();
    }

    private static void SetBaseActions(
        KeyActions keyActions, params ReadOnlySpan<KeyAction> baseActions)
    {
        KeyAction @default = new();
        for (int i = 0; i < keyActions.Sequence.Length; i++)
        {
            KeyAction user = keyActions.Sequence[i];
            for (int d = 0; d < baseActions.Length; d++)
            {
                KeyAction baseAction = baseActions[d];

                if (user.Name != baseAction.Name)
                    continue;

                // Copy key-related properties from base action
                user.Key = baseAction.Key;
                user.ConsoleKey = baseAction.ConsoleKey;
                user.BindingID = baseAction.BindingID;

                if (!string.IsNullOrEmpty(baseAction.Requirement))
                    user.Requirement = string.IsNullOrEmpty(user.Requirement)
                        ? baseAction.Requirement
                        : $"{user.Requirement} && {baseAction.Requirement}";

                if (user.BeforeCastDelay == @default.BeforeCastDelay)
                    user.BeforeCastDelay = baseAction.BeforeCastDelay;

                if (user.BeforeCastMaxDelay == @default.BeforeCastMaxDelay)
                    user.BeforeCastMaxDelay = baseAction.BeforeCastMaxDelay;

                if (user.AfterCastDelay == @default.AfterCastDelay)
                    user.AfterCastDelay = baseAction.AfterCastDelay;

                if (user.AfterCastMaxDelay == @default.AfterCastMaxDelay)
                    user.AfterCastMaxDelay = baseAction.AfterCastMaxDelay;

                if (user.PressDuration == @default.PressDuration)
                    user.PressDuration = baseAction.PressDuration;

                if (user.Cooldown == @default.Cooldown)
                    user.Cooldown = baseAction.Cooldown;

                if (user.BaseAction == @default.BaseAction)
                    user.BaseAction = baseAction.BaseAction;

                if (user.Item == @default.Item)
                    user.Item = baseAction.Item;
            }
        }
    }

    public IEnumerable<(string name, T)> GetByType<T>()
    {
        return GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            .Where(OfType)
            .Select(pInfo =>
            {
                return (pInfo.Name, (T)pInfo.GetValue(this)!);
            });

        static bool OfType(PropertyInfo pInfo)
        {
            return typeof(T).IsAssignableFrom(pInfo.PropertyType);
        }
    }

    public List<(string name, T)> GetByTypeAsList<T>()
    {
        return GetByType<T>().ToList();
    }

    [LoggerMessage(
        EventId = 0010,
        Level = LogLevel.Information,
        Message = "[{prefix}] Init Binds(Cost, Cooldown)")]
    static partial void LogInitBind(ILogger logger, string prefix);

    [LoggerMessage(
        EventId = 0011,
        Level = LogLevel.Information,
        Message = "[{prefix}] Init KeyActions")]
    static partial void LogInitKeyActions(ILogger logger, string prefix);

}

/// <summary>
/// Deserializes IntVariables where each value can be either a scalar int or an int[].
/// Scalars are normalized to single-element arrays for uniform handling.
/// </summary>
public sealed class IntOrIntArrayDictionaryConverter : JsonConverter<Dictionary<string, int[]>>
{
    public override Dictionary<string, int[]>? ReadJson(
        JsonReader reader, Type objectType,
        Dictionary<string, int[]>? existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        Dictionary<string, int[]> result = existingValue ?? [];

        if (reader.TokenType == JsonToken.Null)
            return result;

        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException(
                $"Expected StartObject for IntVariables, got {reader.TokenType}.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.EndObject)
                return result;

            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    $"Expected PropertyName, got {reader.TokenType}.");

            string key = (string)reader.Value!;
            reader.Read();

            int[] values = reader.TokenType switch
            {
                JsonToken.Integer => [(int)(long)reader.Value!],
                JsonToken.StartArray => ReadIntArray(reader),
                _ => throw new JsonSerializationException(
                    $"IntVariables['{key}']: expected integer or array, got {reader.TokenType}.")
            };

            result[key] = values;
        }

        return result;
    }

    private static int[] ReadIntArray(JsonReader reader)
    {
        List<int> list = [];
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.EndArray)
                return [.. list];

            if (reader.TokenType != JsonToken.Integer)
                throw new JsonSerializationException(
                    $"IntVariables array element: expected integer, got {reader.TokenType}.");

            list.Add((int)(long)reader.Value!);
        }

        throw new JsonSerializationException("Unexpected end of JSON in IntVariables array.");
    }

    public override void WriteJson(
        JsonWriter writer, Dictionary<string, int[]>? value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        if (value != null)
        {
            foreach ((string key, int[] values) in value)
            {
                writer.WritePropertyName(key);
                if (values.Length == 1)
                {
                    writer.WriteValue(values[0]);
                }
                else
                {
                    writer.WriteStartArray();
                    for (int i = 0; i < values.Length; i++)
                        writer.WriteValue(values[i]);
                    writer.WriteEndArray();
                }
            }
        }
        writer.WriteEndObject();
    }
}