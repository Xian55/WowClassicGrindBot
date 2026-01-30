using Core.Database;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Core;

public readonly record struct SlotValidationResult(
    int Slot,
    string ExpectedSpell,
    int ActualTextureId,
    string[] PossibleSpells,
    SlotValidationStatus Status);

/// <summary>
/// Represents an action bar validation issue.
/// </summary>
/// <param name="KeyAction">The KeyAction with the issue</param>
/// <param name="Status">The validation status</param>
/// <param name="CanResolve">Whether this can be auto-resolved by placing the spell</param>
public readonly record struct ActionBarIssue(
    KeyAction KeyAction,
    SlotValidationStatus Status,
    bool CanResolve)
{
    public string SpellName => KeyAction.Name;
    public int Slot => KeyAction.Slot;
}

public enum SlotValidationStatus
{
    Valid,
    Mismatch,
    EmptySlot,
    UnknownTexture,
    NotOnActionBar
}

public sealed partial class ActionBarSlotValidator
{
    private readonly ILogger<ActionBarSlotValidator> logger;
    private readonly IconDB iconDB;
    private readonly ActionBarTextureReader textureReader;
    private readonly PlayerReader playerReader;
    private readonly SpellBookReader spellBookReader;

    // Maps spell names to their known Form (built from KeyActions that have Form specified)
    private Dictionary<string, Form> spellToForm = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Equipment-based actions whose icons change with equipped items.
    /// </summary>
    private static readonly FrozenSet<string> DynamicIconNames = FrozenSet.ToFrozenSet(
    [
        "Shoot", "Auto Shot", "Trinket 1", "Trinket 2"
    ], StringComparer.OrdinalIgnoreCase);

    public ActionBarSlotValidator(
        ILogger<ActionBarSlotValidator> logger,
        IconDB iconDB,
        ActionBarTextureReader textureReader,
        PlayerReader playerReader,
        SpellBookReader spellBookReader)
    {
        this.logger = logger;
        this.iconDB = iconDB;
        this.textureReader = textureReader;
        this.playerReader = playerReader;
        this.spellBookReader = spellBookReader;
    }

    /// <summary>
    /// Validates a single KeyAction against the action bar texture.
    /// Zero allocation in the valid case.
    /// </summary>
    public SlotValidationStatus Validate(KeyAction keyAction)
    {
        return Validate(keyAction, out _);
    }

    /// <summary>
    /// Validates a single KeyAction against the action bar texture.
    /// Returns the actual slot used for validation (may differ for form-specific actions).
    /// </summary>
    public SlotValidationStatus Validate(KeyAction keyAction, out int actualSlot)
    {
        // Skip actions without slots (base actions like Jump, etc.)
        if (keyAction.Slot == 0)
        {
            actualSlot = 0;
            return SlotValidationStatus.NotOnActionBar;
        }

        // Skip if no spell name to validate
        if (string.IsNullOrEmpty(keyAction.Name))
        {
            actualSlot = keyAction.Slot;
            return SlotValidationStatus.Valid;
        }

        // Skip macros (convention: macro names start with lowercase)
        if (char.IsLower(keyAction.Name[0]))
        {
            actualSlot = keyAction.Slot;
            return SlotValidationStatus.Valid;
        }

        // Skip item aliases (Food, Drink, etc. - these are consumables, not spells)
        if (KeyReader.IsItemAlias(keyAction.Name))
        {
            actualSlot = keyAction.Slot;
            return SlotValidationStatus.Valid;
        }

        // Skip form-changing spells when player is currently in that form
        // (the icon changes when the form is active, e.g., Cat Form shows different texture when in Cat Form)
        if (IsActiveFormSpell(keyAction.Name))
        {
            actualSlot = keyAction.Slot;
            return SlotValidationStatus.Valid;
        }

        // For form-specific actions, convert to the actual stance bar slot
        // If this action doesn't have Form set, check if the spell name is known to require a form
        actualSlot = GetActualSlot(keyAction);

        // Get texture from action bar
        if (!textureReader.TryGetTexture(actualSlot, out int textureId))
            return SlotValidationStatus.EmptySlot;

        if (textureId == 0)
            return SlotValidationStatus.EmptySlot;

        // Dynamic icon spells (Hunter Aspects, Paladin Auras) change texture when active
        // If slot has something, consider it valid - we can't reliably match the texture
        if (HasDynamicIcon(keyAction.Name))
            return SlotValidationStatus.Valid;

        // Check if spell icon DB has this texture
        ReadOnlySpan<int> spellIds = iconDB.GetSpellIds(textureId);
        if (spellIds.IsEmpty)
            return SlotValidationStatus.UnknownTexture;

        // Check if the expected spell uses this texture
        if (iconDB.SpellNameUsesTexture(keyAction.Name, textureId))
            return SlotValidationStatus.Valid;

        return SlotValidationStatus.Mismatch;
    }

    /// <summary>
    /// Validates a KeyAction and returns detailed result. Allocates for display.
    /// </summary>
    public SlotValidationResult ValidateWithDetails(KeyAction keyAction)
    {
        SlotValidationStatus status = Validate(keyAction, out int actualSlot);

        int textureId = 0;
        string[] possibleSpells = [];

        if (actualSlot > 0)
        {
            textureReader.TryGetTexture(actualSlot, out textureId);

            if (textureId > 0 && status != SlotValidationStatus.Valid)
            {
                possibleSpells = iconDB.GetSpellNamesForDisplay(textureId);
            }
        }

        return new SlotValidationResult(
            actualSlot,
            keyAction.Name,
            textureId,
            possibleSpells,
            status);
    }

    /// <summary>
    /// Validates all KeyActions and logs any issues.
    /// </summary>
    public void ValidateAndLog(IEnumerable<KeyAction> keyActions)
    {
        if (iconDB.IconToSpells.Count == 0)
        {
            LogValidationSkipped(logger);
            return;
        }

        if (!textureReader.IsInitialized)
        {
            LogTexturesNotReady(logger);
            return;
        }

        int validCount = 0;
        int issueCount = 0;

        foreach (KeyAction keyAction in keyActions)
        {
            // Check if spell is known in spellbook
            bool spellKnown = string.IsNullOrEmpty(keyAction.Name) ||
                              spellBookReader.Count == 0 ||
                              spellBookReader.KnowsSpell(keyAction.Name);

            SlotValidationStatus status = Validate(keyAction, out int actualSlot);

            switch (status)
            {
                case SlotValidationStatus.Valid:
                case SlotValidationStatus.NotOnActionBar:
                    validCount++;
                    break;

                case SlotValidationStatus.EmptySlot:
                    // Empty slot is always an issue, regardless of whether spell is known
                    issueCount++;
                    LogEmptySlot(logger, keyAction.Name, actualSlot);
                    break;

                case SlotValidationStatus.Mismatch:
                    if (spellKnown)
                    {
                        // Only report mismatch if we know the spell (can verify texture)
                        issueCount++;
                        LogMismatch(logger, keyAction.Name, actualSlot,
                            iconDB.GetSpellNamesForDisplay(
                                textureReader.SlotTextures[actualSlot]));
                    }
                    else
                    {
                        // Spell not in spellbook - slot has something, can't verify texture
                        validCount++;
                    }
                    break;

                case SlotValidationStatus.UnknownTexture:
                    // Not an error - texture not in our DB (could be item, macro, etc.)
                    validCount++;
                    break;
            }
        }

        if (issueCount > 0)
        {
            LogValidationComplete(logger, validCount, issueCount);
        }
    }

    /// <summary>
    /// Validates all KeyActions from a ClassConfiguration.
    /// </summary>
    public void ValidateClassConfig(ClassConfiguration classConfig)
    {
        List<KeyAction> allActions = [];

        // First pass: build spell-to-form mapping from actions that have Form specified
        spellToForm.Clear();
        foreach ((string _, KeyActions keyActions) in classConfig.GetByType<KeyActions>())
        {
            foreach (KeyAction action in keyActions.Sequence)
            {
                if (action.HasForm && !string.IsNullOrEmpty(action.Name))
                {
                    // Record that this spell name requires this form
                    spellToForm.TryAdd(action.Name, action.FormValue);
                }
            }
        }

        // Second pass: collect all KeyActions for validation
        foreach ((string _, KeyActions keyActions) in classConfig.GetByType<KeyActions>())
        {
            foreach (KeyAction action in keyActions.Sequence)
            {
                if (action.Slot > 0 && !string.IsNullOrEmpty(action.Name))
                {
                    allActions.Add(action);
                }
            }
        }

        if (allActions.Count > 0)
        {
            ValidateAndLog(allActions);
        }
    }

    /// <summary>
    /// Returns the count of validation issues for a ClassConfiguration.
    /// Includes both slot validation issues and unresolved spells.
    /// </summary>
    public int GetIssueCount(ClassConfiguration classConfig)
    {
        return GetIssues(classConfig).Count;
    }

    /// <summary>
    /// Returns the list of validation issues for a ClassConfiguration.
    /// Includes both slot validation issues and unresolved spells.
    /// Filters out duplicates by spell name.
    /// </summary>
    public List<ActionBarIssue> GetIssues(ClassConfiguration classConfig)
    {
        List<ActionBarIssue> issues = [];
        HashSet<string> seenSpells = new(StringComparer.OrdinalIgnoreCase);

        // Build spell-to-form mapping
        spellToForm.Clear();
        foreach ((string _, KeyActions keyActions) in classConfig.GetByType<KeyActions>())
        {
            foreach (KeyAction action in keyActions.Sequence)
            {
                if (action.HasForm && !string.IsNullOrEmpty(action.Name))
                {
                    spellToForm.TryAdd(action.Name, action.FormValue);
                }
            }
        }

        foreach ((string _, KeyActions keyActions) in classConfig.GetByType<KeyActions>())
        {
            foreach (KeyAction action in keyActions.Sequence)
            {
                // Skip if no name
                if (string.IsNullOrEmpty(action.Name))
                    continue;

                // Skip item aliases
                if (KeyReader.IsItemAlias(action.Name))
                    continue;

                // Skip duplicates
                if (!seenSpells.Add(action.Name))
                    continue;

                // Check if this is a macro (convention: macro names start with lowercase)
                bool isMacro = char.IsLower(action.Name[0]);

                // For spells (not macros), check if known in spellbook
                if (!isMacro)
                {
                    bool spellKnown = spellBookReader.Count == 0 ||
                                      spellBookReader.KnowsSpell(action.Name);

                    // Skip spells the player doesn't know yet
                    if (!spellKnown)
                        continue;
                }

                if (action.Slot > 0)
                {
                    if (isMacro)
                    {
                        // Macro with slot - just check if slot is empty (can't validate texture)
                        if (!textureReader.TryGetTexture(action.Slot, out int textureId) || textureId == 0)
                        {
                            issues.Add(new ActionBarIssue(action, SlotValidationStatus.EmptySlot, CanResolve: true));
                        }
                    }
                    else
                    {
                        // Spell with slot - validate against action bar
                        SlotValidationStatus status = Validate(action);
                        if (status == SlotValidationStatus.EmptySlot)
                        {
                            // Empty slot is always an issue - can be auto-resolved
                            issues.Add(new ActionBarIssue(action, status, CanResolve: true));
                        }
                        else if (status == SlotValidationStatus.Mismatch)
                        {
                            // Mismatch - can be auto-resolved
                            issues.Add(new ActionBarIssue(action, status, CanResolve: true));
                        }
                    }
                }
                else if (!action.BaseAction && !isMacro)
                {
                    // No slot in config - check if spell is actually on action bar via texture
                    // (skip this check for macros - they need explicit slots)
                    if (!IsSpellOnActionBar(action.Name))
                    {
                        // Not on action bar and no slot - needs manual resolution (add slot to config)
                        issues.Add(new ActionBarIssue(action, SlotValidationStatus.NotOnActionBar, CanResolve: false));
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Gets the actual slot for validation, considering form inference.
    /// If the action doesn't have Form set but the spell name is known to require a form,
    /// uses the inferred form to calculate the stance bar slot.
    /// </summary>
    private int GetActualSlot(KeyAction keyAction)
    {
        // If action already has a form, use standard conversion
        if (keyAction.HasForm)
        {
            return Stance.ToSlot(keyAction, playerReader);
        }

        // Check if this spell name is known to require a specific form
        if (spellToForm.TryGetValue(keyAction.Name, out Form inferredForm) &&
            inferredForm != Form.None &&
            keyAction.Slot <= ActionBar.MAIN_ACTIONBAR_SLOT)
        {
            // Calculate stance bar slot using the inferred form
            int stanceOffset = (int)FormToStanceActionBar(playerReader.Class, inferredForm);
            return keyAction.Slot + stanceOffset;
        }

        // Warriors are always in a stance - main bar is always a stance bar
        // Use the player's current stance to determine the actual slot
        if (playerReader.Class == UnitClass.Warrior &&
            keyAction.Slot <= ActionBar.MAIN_ACTIONBAR_SLOT &&
            playerReader.Form != Form.None)
        {
            int stanceOffset = (int)FormToStanceActionBar(UnitClass.Warrior, playerReader.Form);
            return keyAction.Slot + stanceOffset;
        }

        // No form inference needed, use the slot as-is
        return keyAction.Slot;
    }

    /// <summary>
    /// Maps Form to StanceActionBar offset (same logic as Stance.FormToActionBar but accessible here).
    /// </summary>
    private static StanceActionBar FormToStanceActionBar(UnitClass @class, Form form)
    {
        return @class switch
        {
            UnitClass.Druid => form switch
            {
                Form.Druid_Cat or Form.Druid_Cat_Prowl => StanceActionBar.DruidCat, // Prowl doesn't change action bar
                Form.Druid_Bear => StanceActionBar.DruidBear,
                Form.Druid_Moonkin => StanceActionBar.DruidMoonkin,
                _ => StanceActionBar.None
            },
            UnitClass.Warrior => form switch
            {
                Form.Warrior_BattleStance => StanceActionBar.WarriorBattleStance,
                Form.Warrior_DefensiveStance => StanceActionBar.WarriorDefensiveStance,
                Form.Warrior_BerserkerStance => StanceActionBar.WarriorBerserkerStance,
                _ => StanceActionBar.None
            },
            UnitClass.Rogue => form == Form.Rogue_Stealth ? StanceActionBar.RogueStealth : StanceActionBar.None,
            UnitClass.Priest => form == Form.Priest_Shadowform ? StanceActionBar.PriestShadowform : StanceActionBar.None,
            _ => StanceActionBar.None
        };
    }

    /// <summary>
    /// Checks if a spell name is found on the action bar via texture matching.
    /// Used to detect spells that have been placed on the action bar but don't have a slot assigned in config.
    /// </summary>
    public bool IsSpellOnActionBar(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // Skip macros (lowercase names)
        if (char.IsLower(name[0]))
            return false;

        // Skip item aliases
        if (KeyReader.IsItemAlias(name))
            return false;

        if (!textureReader.IsInitialized)
            return false;

        // Get all texture IDs that could represent this spell
        int[] textureIds = iconDB.GetTexturesForSpellName(name);
        if (textureIds.Length == 0)
            return false;

        // Check if any of these textures are on the action bar
        var (slot, _) = textureReader.FindSlotByTextures(textureIds);
        return slot > 0;
    }

    /// <summary>
    /// Checks if the spell is a form-changing spell AND the player is currently in that form.
    /// Form spells change their icon when active (e.g., Cat Form shows a different texture when in Cat Form).
    /// </summary>
    private bool IsActiveFormSpell(string name)
    {
        Form currentForm = playerReader.Form;
        if (currentForm == Form.None)
            return false;

        // Check if this is a form-changing spell that matches the current form
        return name switch
        {
            // Druid forms
            "Cat Form" => currentForm == Form.Druid_Cat || currentForm == Form.Druid_Cat_Prowl,
            "Bear Form" or "Dire Bear Form" => currentForm == Form.Druid_Bear,
            "Moonkin Form" => currentForm == Form.Druid_Moonkin,
            "Travel Form" => currentForm == Form.Druid_Travel,
            "Aquatic Form" => currentForm == Form.Druid_Aquatic,
            "Flight Form" or "Swift Flight Form" => currentForm == Form.Druid_Flight,

            // Warrior stances
            "Battle Stance" => currentForm == Form.Warrior_BattleStance,
            "Defensive Stance" => currentForm == Form.Warrior_DefensiveStance,
            "Berserker Stance" => currentForm == Form.Warrior_BerserkerStance,

            // Rogue
            "Stealth" => currentForm == Form.Rogue_Stealth,

            // Priest
            "Shadowform" => currentForm == Form.Priest_Shadowform,

            _ => false
        };
    }

    /// <summary>
    /// Checks if the spell has a dynamic icon that changes when active.
    /// These spells cannot be reliably validated by texture matching.
    /// </summary>
    private static bool HasDynamicIcon(string name)
    {
        // Exact match equipment-based actions
        if (DynamicIconNames.Contains(name))
            return true;

        // Hunter Aspects (prefix pattern)
        if (name.StartsWith("Aspect of", StringComparison.OrdinalIgnoreCase))
            return true;

        // Paladin Auras (suffix pattern)
        if (name.EndsWith(" Aura", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    #region Logging

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Action bar validation skipped - spelliconmap.json not loaded")]
    static partial void LogValidationSkipped(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Action bar validation deferred - textures not yet received")]
    static partial void LogTexturesNotReady(ILogger logger);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Slot {slot} is empty but expected '{spellName}'")]
    static partial void LogEmptySlot(ILogger logger, string spellName, int slot);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Slot {slot} mismatch: expected '{expectedSpell}' but found [{actualSpells}]")]
    static partial void LogMismatch(ILogger logger, string expectedSpell, int slot, string[] actualSpells);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Action bar validation: {validCount} valid, {issueCount} issues")]
    static partial void LogValidationComplete(ILogger logger, int validCount, int issueCount);

    #endregion
}
