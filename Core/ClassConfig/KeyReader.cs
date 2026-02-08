using Core.Database;

using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Core;

public static class KeyReader
{
    /// <summary>
    /// Static reference to ActionBarTextureReader for slot detection from textures.
    /// Set during initialization.
    /// </summary>
    public static ActionBarTextureReader? TextureReader { get; set; }

    /// <summary>
    /// Static reference to ActionBarMacroReader for macro slot detection by name hash.
    /// Set during initialization.
    /// </summary>
    public static ActionBarMacroReader? MacroReader { get; set; }

    /// <summary>
    /// Static reference to IconDB for spell name to texture lookup.
    /// Set during initialization.
    /// </summary>
    public static IconDB? IconDB { get; set; }

    /// <summary>
    /// Static reference to SpellBookReader for checking if spells are known.
    /// Set during initialization.
    /// </summary>
    public static SpellBookReader? SpellBookReader { get; set; }

    /// <summary>
    /// Static reference to ItemDB for item alias (Food/Drink) resolution.
    /// Set during initialization.
    /// </summary>
    public static ItemDB? ItemDB { get; set; }

    /// <summary>
    /// Static reference to EquipmentReader for equipment-based item alias resolution (Trinket 1/2, Shoot).
    /// Set during initialization.
    /// </summary>
    public static EquipmentReader? EquipmentReader { get; set; }

    /// <summary>
    /// Default WoW keybindings mapping BindingID to ConsoleKey.
    /// These represent the expected in-game bindings for non-actionbar keys.
    /// </summary>
    public static Dictionary<BindingID, ConsoleKey> DefaultBindings { get; } = new()
    {
        // Movement
        { BindingID.MOVEFORWARD, ConsoleKey.W },
        { BindingID.MOVEBACKWARD, ConsoleKey.S },
        { BindingID.STRAFELEFT, ConsoleKey.Q },
        { BindingID.STRAFERIGHT, ConsoleKey.E },
        { BindingID.TURNLEFT, ConsoleKey.A },
        { BindingID.TURNRIGHT, ConsoleKey.D },
        { BindingID.JUMP, ConsoleKey.Spacebar },
        { BindingID.SITORSTAND, ConsoleKey.X },

        // Targeting
        { BindingID.TARGETNEARESTENEMY, ConsoleKey.Tab },
        { BindingID.TARGETLASTTARGET, ConsoleKey.G },
        { BindingID.ASSISTTARGET, ConsoleKey.F },
        { BindingID.TARGETPET, ConsoleKey.Multiply },
        // ALT-PAGEUP: TARGETFOCUS (TBC+) or TARGETPARTYMEMBER1 (Vanilla) - version dependent

        // Combat
        { BindingID.PETATTACK, ConsoleKey.Subtract },

        // Interaction (ALT-HOME and ALT-END - modifiers come from runtime)
        { BindingID.INTERACTTARGET, ConsoleKey.Home },
        { BindingID.INTERACTMOUSEOVER, ConsoleKey.End },

        // Follow
        { BindingID.FOLLOWTARGET, ConsoleKey.PageDown },

        // Custom Actions (secure buttons)
        // Using ALT-DELETE/ALT-INSERT - modifiers come from runtime game bindings
        { BindingID.CUSTOM_STOPATTACK, ConsoleKey.Delete },
        { BindingID.CUSTOM_CLEARTARGET, ConsoleKey.Insert },
        // Using SHIFT-PAGEUP/SHIFT-PAGEDOWN - modifiers come from runtime game bindings
        { BindingID.CUSTOM_CONFIG, ConsoleKey.PageUp },
        { BindingID.CUSTOM_FLUSH, ConsoleKey.PageDown },
    };

    /// <summary>
    /// Maps ConsoleKey to WoW key string for SetBinding Lua calls.
    /// Includes all keys (action bar keys + special keys like letters, arrows, etc.)
    /// </summary>
    public static FrozenDictionary<ConsoleKey, string> ConsoleKeyToWoWKey { get; } = BuildConsoleKeyToWoWKey();

    /// <summary>
    /// Additional key name mappings not covered by KeyBindingDefaults.
    /// For action bar keys, use KeyBindingDefaults.KeyNameToBindingID.
    /// </summary>
    private static readonly Dictionary<string, ConsoleKey> ExtraKeyMappings = new()
    {
        { "Space", ConsoleKey.Spacebar },
        { " ", ConsoleKey.Spacebar },
    };

    /// <summary>
    /// Known item aliases for consumables and equipment-based actions.
    /// </summary>
    private static readonly FrozenSet<string> ItemAliases = FrozenSet.ToFrozenSet(
    [
        "Food", "Drink", "Water", "Bandage", "Hearthstone",
        "Mount", "Trinket 1", "Trinket 2", "Shoot", "Auto Shot"
    ], StringComparer.OrdinalIgnoreCase);

    private static FrozenDictionary<ConsoleKey, string> BuildConsoleKeyToWoWKey()
    {
        var dict = new Dictionary<ConsoleKey, string>
        {
            // Letters
            { ConsoleKey.A, "A" }, { ConsoleKey.B, "B" }, { ConsoleKey.C, "C" },
            { ConsoleKey.D, "D" }, { ConsoleKey.E, "E" }, { ConsoleKey.F, "F" },
            { ConsoleKey.G, "G" }, { ConsoleKey.H, "H" }, { ConsoleKey.I, "I" },
            { ConsoleKey.J, "J" }, { ConsoleKey.K, "K" }, { ConsoleKey.L, "L" },
            { ConsoleKey.M, "M" }, { ConsoleKey.N, "N" }, { ConsoleKey.O, "O" },
            { ConsoleKey.P, "P" }, { ConsoleKey.Q, "Q" }, { ConsoleKey.R, "R" },
            { ConsoleKey.S, "S" }, { ConsoleKey.T, "T" }, { ConsoleKey.U, "U" },
            { ConsoleKey.V, "V" }, { ConsoleKey.W, "W" }, { ConsoleKey.X, "X" },
            { ConsoleKey.Y, "Y" }, { ConsoleKey.Z, "Z" },

            // Special keys
            { ConsoleKey.Spacebar, "SPACE" },
            { ConsoleKey.Tab, "TAB" },
            { ConsoleKey.Enter, "ENTER" },
            { ConsoleKey.Escape, "ESCAPE" },
            { ConsoleKey.Backspace, "BACKSPACE" },
            { ConsoleKey.Delete, "DELETE" },
            { ConsoleKey.Insert, "INSERT" },
            { ConsoleKey.Home, "HOME" },
            { ConsoleKey.End, "END" },
            { ConsoleKey.PageUp, "PAGEUP" },
            { ConsoleKey.PageDown, "PAGEDOWN" },

            // Arrow keys
            { ConsoleKey.UpArrow, "UP" },
            { ConsoleKey.DownArrow, "DOWN" },
            { ConsoleKey.LeftArrow, "LEFT" },
            { ConsoleKey.RightArrow, "RIGHT" },

            // Numpad operators
            { ConsoleKey.Add, "NUMPADPLUS" },
            { ConsoleKey.Subtract, "NUMPADMINUS" },
            { ConsoleKey.Multiply, "NUMPADMULTIPLY" },
            { ConsoleKey.Divide, "NUMPADDIVIDE" },
            { ConsoleKey.Decimal, "NUMPADDECIMAL" },

            // Punctuation
            { ConsoleKey.OemMinus, "-" },
            { ConsoleKey.OemPlus, "=" },
            { ConsoleKey.OemComma, "," },
            { ConsoleKey.OemPeriod, "." },
            { ConsoleKey.Oem1, ";" },
            { ConsoleKey.Oem2, "/" },
            { ConsoleKey.Oem3, "`" },
            { ConsoleKey.Oem4, "[" },
            { ConsoleKey.Oem5, "\\" },
            { ConsoleKey.Oem6, "]" },
            { ConsoleKey.Oem7, "'" },
        };

        // Add all keys from centralized mapping
        foreach (var binding in KeyBindingDefaults.Bindings.Values)
        {
            dict.TryAdd(binding.ConsoleKey, binding.WoWKey);
        }

        return dict.ToFrozenDictionary();
    }

    public static bool ReadKey(ILogger logger, KeyAction key)
    {
        // Priority 1: Resolve from BindingID using in-game bindings
        if (key.BindingID != BindingID.None)
        {
            if (ResolveFromBindingID(key))
            {
                return true;
            }
            // BindingID specified but not resolved - will retry when GameBindings arrives
        }

        // Priority 2: Resolve from Key string (explicit override or fallback)
        if (!string.IsNullOrEmpty(key.Key))
        {
            if (ResolveFromKeyString(logger, key))
            {
                return true;
            }
        }

        // Priority 3: Resolve from Slot (action bar spells without explicit Key)
        if (key.Slot > 0)
        {
            if (ResolveFromSlot(key))
            {
                return true;
            }
        }

        // Priority 4: Resolve from macro Name via action bar macro hashes
        // Detects which slot the macro is in by matching name hash
        // Macros have lowercase names (e.g., "cancelform")
        // Note: Unlike spells, macros are detected even with BaseAction=true
        // because macros cannot be detected via texture matching
        if (!string.IsNullOrEmpty(key.Name) && char.IsLower(key.Name[0]))
        {
            if (ResolveFromMacroName(key))
            {
                return true;
            }
        }

        // Priority 5: Resolve from spell Name via action bar textures
        // Detects which slot the spell is in by matching texture IDs
        if (!string.IsNullOrEmpty(key.Name) && !key.BaseAction)
        {
            if (ResolveFromSpellName(key))
            {
                return true;
            }
        }

        // Neither BindingID, Key, Slot, macro Name, nor spell Name could be resolved
        return false;
    }

    /// <summary>
    /// Resolves Slot and ConsoleKey by finding the macro on the action bar via name hash matching.
    /// Used when the macro Name is specified (lowercase names indicate macros).
    /// </summary>
    private static bool ResolveFromMacroName(KeyAction key)
    {
        if (MacroReader == null || !MacroReader.IsInitialized)
            return false;

        // Determine preferred slot range based on Form (if any)
        (int preferredMin, int preferredMax) = GetPreferredSlotRange(key);

        // Find the slot on the action bar by macro name hash
        int slot = MacroReader.FindSlotByMacroName(key.Name, preferredMin, preferredMax);
        if (slot == 0)
            return false;

        // Set the slot
        key.Slot = slot;

        // Now resolve the key from the slot
        return ResolveFromSlot(key);
    }

    /// <summary>
    /// Resolves Slot and ConsoleKey by finding the spell on the action bar via texture matching.
    /// Used when only the spell Name is specified in the config.
    /// </summary>
    private static bool ResolveFromSpellName(KeyAction key)
    {
        if (IconDB == null || TextureReader == null || !TextureReader.IsInitialized)
            return false;

        // Skip macros (lowercase names)
        if (char.IsLower(key.Name[0]))
            return false;

        // Try resolving item aliases (Food, Drink, etc.) via texture lookup
        if (IsItemAlias(key.Name))
            return ResolveFromItemAlias(key);

        // Guard rail: Skip if spell is not known by the player
        // This prevents trying to resolve spells the player hasn't learned yet
        if (SpellBookReader != null && SpellBookReader.Count > 0)
        {
            if (!SpellBookReader.KnowsSpell(key.Name))
                return false;
        }

        // Get texture IDs - use precomputed family textures for dynamic icon spells
        int[] textureIds = IconDB.HasDynamicIcon(key.Name)
            ? IconDB.GetFamilyTextures(key.Name)
            : IconDB.GetTexturesForSpellName(key.Name);

        if (textureIds.Length == 0)
            return false;

        // Determine preferred slot range based on Form (if any)
        // This ensures form-specific spells are found on the correct stance bar
        (int preferredMin, int preferredMax) = GetPreferredSlotRange(key);

        // Find the slot on the action bar
        var (slot, _) = TextureReader.FindSlotByTextures(textureIds, preferredMin, preferredMax);
        if (slot == 0)
            return false;

        // Set the slot
        key.Slot = slot;

        // Now resolve the key from the slot
        return ResolveFromSlot(key);
    }

    /// <summary>
    /// Checks if the name is a known item alias (Food, Drink, etc.)
    /// These are consumables or equipment-based actions placed on action bars.
    /// </summary>
    public static bool IsItemAlias(string name) => ItemAliases.Contains(name);

    /// <summary>
    /// Resolves Slot and ConsoleKey by finding an item alias (Food/Drink) on the action bar via texture matching.
    /// Used when only the item alias Name is specified in the config without Key or Slot.
    /// </summary>
    private static bool ResolveFromItemAlias(KeyAction key)
    {
        if (ItemDB == null || TextureReader == null || !TextureReader.IsInitialized)
            return false;

        IEnumerable<int> textures = key.Name switch
        {
            var n when n.Equals("Drink", StringComparison.OrdinalIgnoreCase) => ItemDB.GetDrinkTextures(),
            var n when n.Equals("Water", StringComparison.OrdinalIgnoreCase) => ItemDB.GetDrinkTextures(),
            var n when n.Equals("Food", StringComparison.OrdinalIgnoreCase) => ItemDB.GetFoodTextures(),
            _ => []
        };

        var (slot, _) = TextureReader.FindSlotByTextures(textures);
        if (slot > 0)
        {
            key.Slot = slot;
            return ResolveFromSlot(key);
        }

        // Handle equipment-based items (Trinket 1/2, Shoot, Auto Shot)
        if (EquipmentReader != null)
        {
            int itemId = key.Name switch
            {
                var n when n.Equals("Trinket 1", StringComparison.OrdinalIgnoreCase)
                    => EquipmentReader.GetId((int)InventorySlotId.Trinket_1),
                var n when n.Equals("Trinket 2", StringComparison.OrdinalIgnoreCase)
                    => EquipmentReader.GetId((int)InventorySlotId.Trinket_2),
                var n when n.Equals("Shoot", StringComparison.OrdinalIgnoreCase)
                    => EquipmentReader.GetId((int)InventorySlotId.Ranged),
                var n when n.Equals("Auto Shot", StringComparison.OrdinalIgnoreCase)
                    => EquipmentReader.GetId((int)InventorySlotId.Ranged),
                _ => 0
            };

            if (itemId > 0 && ItemDB.TryGetTexture(itemId, out int textureId))
            {
                int equipSlot = TextureReader.FindSlotByTexture(textureId);
                if (equipSlot > 0)
                {
                    key.Slot = equipSlot;
                    return ResolveFromSlot(key);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the preferred slot range for a KeyAction based on its Form.
    /// Returns (0, 0) if no preference (search all bars).
    /// </summary>
    public static (int min, int max) GetPreferredSlotRange(KeyAction key)
    {
        if (!key.HasForm)
            return (0, 0);

        // Map Form to stance bar slot range
        return key.FormValue switch
        {
            // Druid forms (Prowl doesn't change action bar in Classic)
            Form.Druid_Cat or Form.Druid_Cat_Prowl => (73, 84), // Cat bar (stance 1)
            Form.Druid_Bear => (97, 108), // Bear bar (stance 3)
            Form.Druid_Moonkin => (109, 120), // Moonkin bar (stance 4)

            // Warrior stances
            Form.Warrior_BattleStance => (73, 84),
            Form.Warrior_DefensiveStance => (85, 96),
            Form.Warrior_BerserkerStance => (97, 108),

            // Rogue stealth
            Form.Rogue_Stealth => (73, 84),

            // Priest shadowform
            Form.Priest_Shadowform => (73, 84),

            _ => (0, 0)
        };
    }

    /// <summary>
    /// Resolves ConsoleKey, Modifier, and Slot from BindingID.
    /// First tries GameBindings (in-game), then falls back to KeyBindingDefaults.
    /// </summary>
    private static bool ResolveFromBindingID(KeyAction key)
    {
        // Try in-game bindings first (source of truth)
        if (GameBindings.TryGetValue(key.BindingID, out var gameBinding))
        {
            key.ConsoleKey = gameBinding.Key;
            key.Modifier = gameBinding.Modifier;

            // Get slot from defaults if this is an action bar binding
            var binding = KeyBindingDefaults.GetByBindingID(key.BindingID);
            if (binding.HasValue && binding.Value.Slot.HasValue)
            {
                key.Slot = binding.Value.Slot.Value;
            }
            return true;
        }

        // Fall back to KeyBindingDefaults (before GameBindings is populated)
        var defaultBinding = KeyBindingDefaults.GetByBindingID(key.BindingID);
        if (defaultBinding.HasValue)
        {
            key.ConsoleKey = defaultBinding.Value.ConsoleKey;
            key.Modifier = ModifierKey.None; // Defaults don't have modifiers
            if (defaultBinding.Value.Slot.HasValue)
                key.Slot = defaultBinding.Value.Slot.Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves ConsoleKey and Modifier from Slot number.
    /// Used for action bar spells when neither BindingID nor Key is specified.
    /// </summary>
    public static bool ResolveFromSlot(KeyAction key)
    {
        BindingID bindingId = SlotToBindingID(key.Slot);
        if (bindingId == BindingID.None)
            return false;

        // Try in-game bindings first
        if (GameBindings.TryGetValue(bindingId, out var gameBinding))
        {
            key.ConsoleKey = gameBinding.Key;
            key.Modifier = gameBinding.Modifier;
            return true;
        }

        // Fall back to KeyBindingDefaults
        var defaultBinding = KeyBindingDefaults.GetByBindingID(bindingId);
        if (defaultBinding.HasValue)
        {
            key.ConsoleKey = defaultBinding.Value.ConsoleKey;
            key.Modifier = ModifierKey.None; // Defaults don't have modifiers
            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts an action bar slot number to the corresponding BindingID.
    /// Slots 1-12: Main bar (ACTIONBUTTON1-12)
    /// Slots 49-60: Bottom Right bar (MULTIACTIONBAR2BUTTON1-12)
    /// Slots 61-72: Bottom Left bar (MULTIACTIONBAR1BUTTON1-12)
    /// Slots 73-120: Stance bars (use main bar keys ACTIONBUTTON1-12)
    /// </summary>
    public static BindingID SlotToBindingID(int slot)
    {
        // Main action bar: slots 1-12
        if (slot >= 1 && slot <= 12)
            return BindingID.ACTIONBUTTON1 + (slot - 1);

        // Bottom Right bar: slots 49-60
        if (slot >= 49 && slot <= 60)
            return BindingID.MULTIACTIONBAR2BUTTON1 + (slot - 49);

        // Bottom Left bar: slots 61-72
        if (slot >= 61 && slot <= 72)
            return BindingID.MULTIACTIONBAR1BUTTON1 + (slot - 61);

        // Stance bars: slots 73-120
        // These use the same keys as main action bar (ACTIONBUTTON1-12)
        // because when in a stance, the main bar shows stance bar content
        if (slot >= 73 && slot <= 120)
        {
            int positionInBar = ((slot - 73) % 12) + 1; // 1-12
            return BindingID.ACTIONBUTTON1 + (positionInBar - 1);
        }

        return BindingID.None;
    }

    /// <summary>
    /// Resolves ConsoleKey, Modifier, and Slot from Key string.
    /// Supports modifier prefixes like "Shift-F", "Ctrl-1", "Alt-Q".
    /// Used as fallback when BindingID is not set or not resolved.
    /// </summary>
    internal static bool ResolveFromKeyString(ILogger logger, KeyAction key)
    {
        // Parse modifier prefix first (e.g., "Shift-F" -> "F", Shift)
        var (baseKey, modifier) = ModifierKeyExtensions.ParseKeyString(key.Key);

        // Try KeyBindingDefaults first (centralized source of truth)
        var binding = KeyBindingDefaults.GetByKeyName(baseKey);
        if (binding.HasValue)
        {
            key.ConsoleKey = binding.Value.ConsoleKey;
            key.Modifier = modifier;
            if (binding.Value.Slot.HasValue)
                key.Slot = binding.Value.Slot.Value;
            return true;
        }

        // Try extra key mappings (Space, etc.)
        if (ExtraKeyMappings.TryGetValue(baseKey, out ConsoleKey consoleKey))
        {
            key.ConsoleKey = consoleKey;
            key.Modifier = modifier;
            // No slot for these keys
            if (!key.BaseAction)
            {
                logger.LogWarning("[{Name}] Unable to assign Actionbar Slot!", key.Name);
            }
            return true;
        }

        // Fallback: try parsing as ConsoleKey enum name
        if (Enum.TryParse(baseKey, true, out consoleKey))
        {
            key.ConsoleKey = consoleKey;
            key.Modifier = modifier;
            if (!key.BaseAction)
            {
                logger.LogWarning("[{Name}] Unable to assign Actionbar Slot!", key.Name);
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a KeyAction has a custom binding that differs from the default.
    /// </summary>
    public static bool HasCustomBinding(KeyAction keyAction)
    {
        if (keyAction.BindingID == BindingID.None)
            return false;

        if (!DefaultBindings.TryGetValue(keyAction.BindingID, out ConsoleKey defaultKey))
            return false;

        return keyAction.ConsoleKey != defaultKey;
    }

    /// <summary>
    /// Gets all KeyActions that have custom bindings differing from defaults.
    /// </summary>
    public static List<KeyAction> GetCustomBindings(IEnumerable<KeyAction> keyActions)
    {
        List<KeyAction> customBindings = [];

        foreach (KeyAction keyAction in keyActions)
        {
            if (HasCustomBinding(keyAction))
            {
                customBindings.Add(keyAction);
            }
        }

        return customBindings;
    }

    /// <summary>
    /// Generates a Lua SetBinding command for a KeyAction.
    /// Returns null if the binding cannot be generated.
    /// </summary>
    public static string? GenerateSetBindingLua(KeyAction keyAction)
    {
        if (keyAction.BindingID == BindingID.None)
            return null;

        if (!ConsoleKeyToWoWKey.TryGetValue(keyAction.ConsoleKey, out string? wowKey))
            return null;

        string bindingId = keyAction.BindingID.ToStringF();
        return $"SetBinding(\"{wowKey}\", \"{bindingId}\")";
    }

    /// <summary>
    /// Generates SetBinding Lua commands for bindings.
    /// Returns individual commands to respect WoW's ~255 char message limit.
    /// </summary>
    /// <param name="keyActions">KeyActions to generate bindings for</param>
    /// <param name="customOnly">If true, only generate for non-default bindings</param>
    public static List<string> GenerateSetBindingsLua(IEnumerable<KeyAction> keyActions, bool customOnly = true)
    {
        List<string> commands = [];

        IEnumerable<KeyAction> actions = customOnly
            ? GetCustomBindings(keyActions)
            : keyActions;

        foreach (KeyAction keyAction in actions)
        {
            string? lua = GenerateSetBindingLua(keyAction);
            if (lua != null)
            {
                commands.Add(lua);
            }
        }

        if (commands.Count > 0)
        {
            commands.Add("SaveBindings(2)"); // 2 = Character-specific bindings
        }

        return commands;
    }

    #region Binding Decoder (from Lua addon pixel encoding)

    /// <summary>
    /// Maps BindingIndex (from Lua) to BindingID enum.
    /// Must match BindingIndex table in SetupDefaultBindings.lua
    /// </summary>
    private static readonly Dictionary<int, BindingID> IndexToBindingID = new()
    {
        // Movement (1-8)
        { 1, BindingID.JUMP },
        { 2, BindingID.MOVEFORWARD },
        { 3, BindingID.MOVEBACKWARD },
        { 4, BindingID.STRAFELEFT },
        { 5, BindingID.STRAFERIGHT },
        { 6, BindingID.TURNLEFT },
        { 7, BindingID.TURNRIGHT },
        { 8, BindingID.SITORSTAND },

        // Targeting (9-14)
        { 9, BindingID.TARGETNEARESTENEMY },
        { 10, BindingID.TARGETLASTTARGET },
        { 11, BindingID.ASSISTTARGET },
        { 12, BindingID.TARGETPET },
        { 13, BindingID.TARGETFOCUS },

        // Combat (15-17)
        { 15, BindingID.STARTATTACK },
        { 16, BindingID.STOPATTACK },
        { 17, BindingID.PETATTACK },

        // Interaction (18-20)
        { 18, BindingID.INTERACTTARGET },
        { 19, BindingID.INTERACTMOUSEOVER },
        { 20, BindingID.FOLLOWTARGET },

        // Main Action Bar slots 1-12 (21-32)
        { 21, BindingID.ACTIONBUTTON1 },
        { 22, BindingID.ACTIONBUTTON2 },
        { 23, BindingID.ACTIONBUTTON3 },
        { 24, BindingID.ACTIONBUTTON4 },
        { 25, BindingID.ACTIONBUTTON5 },
        { 26, BindingID.ACTIONBUTTON6 },
        { 27, BindingID.ACTIONBUTTON7 },
        { 28, BindingID.ACTIONBUTTON8 },
        { 29, BindingID.ACTIONBUTTON9 },
        { 30, BindingID.ACTIONBUTTON10 },
        { 31, BindingID.ACTIONBUTTON11 },
        { 32, BindingID.ACTIONBUTTON12 },

        // Bottom Right Action Bar slots 49-60 (33-44)
        { 33, BindingID.MULTIACTIONBAR2BUTTON1 },
        { 34, BindingID.MULTIACTIONBAR2BUTTON2 },
        { 35, BindingID.MULTIACTIONBAR2BUTTON3 },
        { 36, BindingID.MULTIACTIONBAR2BUTTON4 },
        { 37, BindingID.MULTIACTIONBAR2BUTTON5 },
        { 38, BindingID.MULTIACTIONBAR2BUTTON6 },
        { 39, BindingID.MULTIACTIONBAR2BUTTON7 },
        { 40, BindingID.MULTIACTIONBAR2BUTTON8 },
        { 41, BindingID.MULTIACTIONBAR2BUTTON9 },
        { 42, BindingID.MULTIACTIONBAR2BUTTON10 },
        { 43, BindingID.MULTIACTIONBAR2BUTTON11 },
        { 44, BindingID.MULTIACTIONBAR2BUTTON12 },

        // Bottom Left Action Bar slots 61-72 (45-56)
        { 45, BindingID.MULTIACTIONBAR1BUTTON1 },
        { 46, BindingID.MULTIACTIONBAR1BUTTON2 },
        { 47, BindingID.MULTIACTIONBAR1BUTTON3 },
        { 48, BindingID.MULTIACTIONBAR1BUTTON4 },
        { 49, BindingID.MULTIACTIONBAR1BUTTON5 },
        { 50, BindingID.MULTIACTIONBAR1BUTTON6 },
        { 51, BindingID.MULTIACTIONBAR1BUTTON7 },
        { 52, BindingID.MULTIACTIONBAR1BUTTON8 },
        { 53, BindingID.MULTIACTIONBAR1BUTTON9 },
        { 54, BindingID.MULTIACTIONBAR1BUTTON10 },
        { 55, BindingID.MULTIACTIONBAR1BUTTON11 },
        { 56, BindingID.MULTIACTIONBAR1BUTTON12 },

        // Custom actions (secure buttons) (57-61)
        { 57, BindingID.CUSTOM_STOPATTACK },
        { 58, BindingID.CUSTOM_CLEARTARGET },
        { 59, BindingID.CUSTOM_CONFIG },
        { 61, BindingID.CUSTOM_FLUSH },

        // Vanilla-specific (60)
        { 60, BindingID.TARGETPARTYMEMBER1 },
    };

    /// <summary>
    /// Maps compact WoW key ID (from Lua pixel encoding) to ConsoleKey.
    /// Values match WoWKeyToId table in SetupDefaultBindings.lua.
    /// Uses compact IDs (1-90) to fit in 7 bits with modifier support.
    /// </summary>
    private static readonly FrozenDictionary<int, ConsoleKey> IdToConsoleKey = BuildIdToConsoleKey();

    private static FrozenDictionary<int, ConsoleKey> BuildIdToConsoleKey()
    {
        var dict = new Dictionary<int, ConsoleKey>
        {
            // Letters A-Z: 1-26
            { 1, ConsoleKey.A }, { 2, ConsoleKey.B }, { 3, ConsoleKey.C },
            { 4, ConsoleKey.D }, { 5, ConsoleKey.E }, { 6, ConsoleKey.F },
            { 7, ConsoleKey.G }, { 8, ConsoleKey.H }, { 9, ConsoleKey.I },
            { 10, ConsoleKey.J }, { 11, ConsoleKey.K }, { 12, ConsoleKey.L },
            { 13, ConsoleKey.M }, { 14, ConsoleKey.N }, { 15, ConsoleKey.O },
            { 16, ConsoleKey.P }, { 17, ConsoleKey.Q }, { 18, ConsoleKey.R },
            { 19, ConsoleKey.S }, { 20, ConsoleKey.T }, { 21, ConsoleKey.U },
            { 22, ConsoleKey.V }, { 23, ConsoleKey.W }, { 24, ConsoleKey.X },
            { 25, ConsoleKey.Y }, { 26, ConsoleKey.Z },

            // Numbers 0-9: 27-36
            { 27, ConsoleKey.D0 }, { 28, ConsoleKey.D1 }, { 29, ConsoleKey.D2 },
            { 30, ConsoleKey.D3 }, { 31, ConsoleKey.D4 }, { 32, ConsoleKey.D5 },
            { 33, ConsoleKey.D6 }, { 34, ConsoleKey.D7 }, { 35, ConsoleKey.D8 },
            { 36, ConsoleKey.D9 },

            // Numpad 0-9: 37-46
            { 37, ConsoleKey.NumPad0 }, { 38, ConsoleKey.NumPad1 }, { 39, ConsoleKey.NumPad2 },
            { 40, ConsoleKey.NumPad3 }, { 41, ConsoleKey.NumPad4 }, { 42, ConsoleKey.NumPad5 },
            { 43, ConsoleKey.NumPad6 }, { 44, ConsoleKey.NumPad7 }, { 45, ConsoleKey.NumPad8 },
            { 46, ConsoleKey.NumPad9 },

            // Numpad operators: 47-51
            { 47, ConsoleKey.Multiply }, { 48, ConsoleKey.Add }, { 49, ConsoleKey.Subtract },
            { 50, ConsoleKey.Decimal }, { 51, ConsoleKey.Divide },

            // Function keys F1-F12: 52-63
            { 52, ConsoleKey.F1 }, { 53, ConsoleKey.F2 }, { 54, ConsoleKey.F3 },
            { 55, ConsoleKey.F4 }, { 56, ConsoleKey.F5 }, { 57, ConsoleKey.F6 },
            { 58, ConsoleKey.F7 }, { 59, ConsoleKey.F8 }, { 60, ConsoleKey.F9 },
            { 61, ConsoleKey.F10 }, { 62, ConsoleKey.F11 }, { 63, ConsoleKey.F12 },

            // Special keys: 64-78
            { 64, ConsoleKey.Spacebar }, { 65, ConsoleKey.Tab }, { 66, ConsoleKey.Enter },
            { 67, ConsoleKey.Escape }, { 68, ConsoleKey.Backspace }, { 69, ConsoleKey.Delete },
            { 70, ConsoleKey.Insert }, { 71, ConsoleKey.Home }, { 72, ConsoleKey.End },
            { 73, ConsoleKey.PageUp }, { 74, ConsoleKey.PageDown },
            { 75, ConsoleKey.UpArrow }, { 76, ConsoleKey.DownArrow },
            { 77, ConsoleKey.LeftArrow }, { 78, ConsoleKey.RightArrow },

            // Punctuation: 79-89
            { 79, ConsoleKey.OemMinus }, { 80, ConsoleKey.OemPlus },
            { 81, ConsoleKey.OemComma }, { 82, ConsoleKey.OemPeriod },
            { 83, ConsoleKey.Oem1 }, { 84, ConsoleKey.Oem2 }, { 85, ConsoleKey.Oem3 },
            { 86, ConsoleKey.Oem4 }, { 87, ConsoleKey.Oem5 }, { 88, ConsoleKey.Oem6 },
            { 89, ConsoleKey.Oem7 },
        };

        return dict.ToFrozenDictionary();
    }

    /// <summary>
    /// Decodes an encoded binding value from the Lua addon.
    /// Format (24 bits) with modifier support:
    /// - Bits 22-23: key1 modifier (2 bits: 0=none, 1=Shift, 2=Ctrl, 3=Alt)
    /// - Bits 20-21: key2 modifier (2 bits: 0=none, 1=Shift, 2=Ctrl, 3=Alt)
    /// - Bits 14-19: index (6 bits, max 63)
    /// - Bits 7-13: key1Id (7 bits, max 127)
    /// - Bits 0-6: key2Id (7 bits, max 127)
    /// </summary>
    /// <param name="encodedValue">The pixel value from addon</param>
    /// <returns>Tuple of (BindingID, key1, mod1, key2, mod2) or null if invalid</returns>
    public static (BindingID bindingId, ConsoleKey key1, ModifierKey mod1, ConsoleKey key2, ModifierKey mod2)? DecodeBinding(int encodedValue)
    {
        if (encodedValue <= 0)
            return null;

        // Extract fields from new encoding format
        int mod1Value = (encodedValue >> 22) & 0x3;
        int mod2Value = (encodedValue >> 20) & 0x3;
        int index = (encodedValue >> 14) & 0x3F;
        int key1Id = (encodedValue >> 7) & 0x7F;
        int key2Id = encodedValue & 0x7F;

        if (!IndexToBindingID.TryGetValue(index, out BindingID bindingId))
            return null;

        // key1Id of 0 means no primary binding
        ConsoleKey key1 = ConsoleKey.NoName;
        if (key1Id > 0 && !IdToConsoleKey.TryGetValue(key1Id, out key1))
            key1 = ConsoleKey.NoName;

        // key2Id of 0 means no secondary binding
        ConsoleKey key2 = ConsoleKey.NoName;
        if (key2Id > 0 && !IdToConsoleKey.TryGetValue(key2Id, out key2))
            key2 = ConsoleKey.NoName;

        // At least one key must be bound
        if (key1 == ConsoleKey.NoName && key2 == ConsoleKey.NoName)
            return null;

        ModifierKey mod1 = ModifierKeyExtensions.FromEncodedValue(mod1Value);
        ModifierKey mod2 = ModifierKeyExtensions.FromEncodedValue(mod2Value);

        return (bindingId, key1, mod1, key2, mod2);
    }

    /// <summary>
    /// Storage for primary bindings received from the game client.
    /// </summary>
    public static Dictionary<BindingID, (ConsoleKey Key, ModifierKey Modifier)> GameBindings { get; } = [];

    /// <summary>
    /// Storage for secondary bindings received from the game client.
    /// </summary>
    public static Dictionary<BindingID, (ConsoleKey Key, ModifierKey Modifier)> GameBindingsSecondary { get; } = [];

    /// <summary>
    /// Processes an encoded binding value and stores it.
    /// Call this when receiving binding data from the addon.
    /// </summary>
    public static void ProcessBindingFromAddon(int encodedValue)
    {
        var decoded = DecodeBinding(encodedValue);
        if (decoded.HasValue)
        {
            if (decoded.Value.key1 != ConsoleKey.NoName)
                GameBindings[decoded.Value.bindingId] = (decoded.Value.key1, decoded.Value.mod1);

            if (decoded.Value.key2 != ConsoleKey.NoName)
                GameBindingsSecondary[decoded.Value.bindingId] = (decoded.Value.key2, decoded.Value.mod2);
        }
    }

    /// <summary>
    /// Compares expected bindings with actual game bindings.
    /// Returns list of mismatches (includes modifier comparison).
    /// </summary>
    public static List<(BindingID bindingId, ConsoleKey expectedKey, ModifierKey expectedMod, ConsoleKey actualKey, ModifierKey actualMod)> GetBindingMismatches(
        IEnumerable<KeyAction> keyActions)
    {
        List<(BindingID, ConsoleKey, ModifierKey, ConsoleKey, ModifierKey)> mismatches = [];

        foreach (KeyAction keyAction in keyActions)
        {
            if (keyAction.BindingID == BindingID.None)
                continue;

            if (GameBindings.TryGetValue(keyAction.BindingID, out var actualBinding))
            {
                if (keyAction.ConsoleKey != actualBinding.Key || keyAction.Modifier != actualBinding.Modifier)
                {
                    mismatches.Add((keyAction.BindingID, keyAction.ConsoleKey, keyAction.Modifier, actualBinding.Key, actualBinding.Modifier));
                }
            }
        }

        return mismatches;
    }

    #endregion
}
