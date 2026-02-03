using System;
using System.Collections.Generic;

namespace Core;

/// <summary>
/// Represents a default key binding.
/// </summary>
/// <param name="BindingID">WoW binding ID</param>
/// <param name="KeyName">Key name used in JSON configs ("Spacebar", "1", "N1", "F1")</param>
/// <param name="ConsoleKey">System ConsoleKey for input simulation</param>
/// <param name="WoWKey">WoW API key string ("SPACE", "1", "NUMPAD1", "F1")</param>
/// <param name="KeyId">Numeric ID for pixel encoding</param>
/// <param name="Slot">Action bar slot (1-72), null for non-action bar bindings</param>
public readonly record struct KeyBinding(
    BindingID BindingID,
    string KeyName,
    ConsoleKey ConsoleKey,
    string WoWKey,
    int KeyId,
    int? Slot = null);

/// <summary>
/// Centralized default key bindings. Single source of truth.
/// </summary>
public static class KeyBindingDefaults
{
    /// <summary>
    /// All default key bindings indexed by BindingID.
    /// </summary>
    public static readonly Dictionary<BindingID, KeyBinding> Bindings = new()
    {
        // ===== Movement =====
        { BindingID.MOVEFORWARD,  new(BindingID.MOVEFORWARD,  "W",         ConsoleKey.W,         "W",         87) },
        { BindingID.MOVEBACKWARD, new(BindingID.MOVEBACKWARD, "S",         ConsoleKey.S,         "S",         83) },
        { BindingID.STRAFELEFT,   new(BindingID.STRAFELEFT,   "Q",         ConsoleKey.Q,         "Q",         81) },
        { BindingID.STRAFERIGHT,  new(BindingID.STRAFERIGHT,  "E",         ConsoleKey.E,         "E",         69) },
        { BindingID.TURNLEFT,     new(BindingID.TURNLEFT,     "A",         ConsoleKey.A,         "A",         65) },
        { BindingID.TURNRIGHT,    new(BindingID.TURNRIGHT,    "D",         ConsoleKey.D,         "D",         68) },
        { BindingID.JUMP,         new(BindingID.JUMP,         "Spacebar",  ConsoleKey.Spacebar,  "SPACE",     32) },
        { BindingID.SITORSTAND,   new(BindingID.SITORSTAND,   "X",         ConsoleKey.X,         "X",         88) },

        // ===== Targeting =====
        { BindingID.TARGETNEARESTENEMY, new(BindingID.TARGETNEARESTENEMY, "Tab",      ConsoleKey.Tab,      "TAB",            9) },
        { BindingID.TARGETLASTTARGET,   new(BindingID.TARGETLASTTARGET,   "G",        ConsoleKey.G,        "G",             71) },
        { BindingID.ASSISTTARGET,       new(BindingID.ASSISTTARGET,       "F",        ConsoleKey.F,        "F",             70) },
        { BindingID.TARGETPET,          new(BindingID.TARGETPET,          "Multiply", ConsoleKey.Multiply, "NUMPADMULTIPLY", 106) },
        // ALT-PAGEUP: TARGETFOCUS (TBC+) or TARGETPARTYMEMBER1 (Vanilla) - version dependent, modifiers come from runtime

        // ===== Combat =====
        { BindingID.STARTATTACK, new(BindingID.STARTATTACK, "Add",      ConsoleKey.Add,      "NUMPADPLUS",  107) },
        { BindingID.PETATTACK,   new(BindingID.PETATTACK,   "Subtract", ConsoleKey.Subtract, "NUMPADMINUS", 109) },

        // ===== Interaction (ALT-HOME/ALT-END - modifiers come from runtime) =====
        { BindingID.INTERACTTARGET,    new(BindingID.INTERACTTARGET,    "Home",     ConsoleKey.Home,     "HOME",     36) },
        { BindingID.INTERACTMOUSEOVER, new(BindingID.INTERACTMOUSEOVER, "End",      ConsoleKey.End,      "END",      35) },

        // ===== Follow =====
        { BindingID.FOLLOWTARGET, new(BindingID.FOLLOWTARGET, "PageDown", ConsoleKey.PageDown, "PAGEDOWN", 34) },

        // ===== Custom Actions (secure buttons - no macro slots used) =====
        // These replace WoW's limited built-in commands with macro-powered versions
        // CUSTOM_STOPATTACK: /stopattack + /stopcasting + /petfollow (better than built-in STOPATTACK)
        // CUSTOM_CLEARTARGET: /cleartarget (no built-in binding exists)
        // Using ALT-DELETE/ALT-INSERT - modifiers come from runtime game bindings
        { BindingID.CUSTOM_STOPATTACK,   new(BindingID.CUSTOM_STOPATTACK,   "Delete",   ConsoleKey.Delete,   "DELETE",   46) },
        { BindingID.CUSTOM_CLEARTARGET,  new(BindingID.CUSTOM_CLEARTARGET,  "Insert",   ConsoleKey.Insert,   "INSERT",   45) },
        // CUSTOM_CONFIG: /dc (SHIFT-PAGEUP) - opens addon config
        // CUSTOM_FLUSH: /dcflush (SHIFT-PAGEDOWN) - flushes addon state
        { BindingID.CUSTOM_CONFIG,       new(BindingID.CUSTOM_CONFIG,       "PageUp",   ConsoleKey.PageUp,   "PAGEUP",   33) },
        { BindingID.CUSTOM_FLUSH,        new(BindingID.CUSTOM_FLUSH,        "PageDown", ConsoleKey.PageDown, "PAGEDOWN", 34) },

        // ===== Main Action Bar: slots 1-12 =====
        { BindingID.ACTIONBUTTON1,  new(BindingID.ACTIONBUTTON1,  "1", ConsoleKey.D1,       "1", 49,  Slot: 1) },
        { BindingID.ACTIONBUTTON2,  new(BindingID.ACTIONBUTTON2,  "2", ConsoleKey.D2,       "2", 50,  Slot: 2) },
        { BindingID.ACTIONBUTTON3,  new(BindingID.ACTIONBUTTON3,  "3", ConsoleKey.D3,       "3", 51,  Slot: 3) },
        { BindingID.ACTIONBUTTON4,  new(BindingID.ACTIONBUTTON4,  "4", ConsoleKey.D4,       "4", 52,  Slot: 4) },
        { BindingID.ACTIONBUTTON5,  new(BindingID.ACTIONBUTTON5,  "5", ConsoleKey.D5,       "5", 53,  Slot: 5) },
        { BindingID.ACTIONBUTTON6,  new(BindingID.ACTIONBUTTON6,  "6", ConsoleKey.D6,       "6", 54,  Slot: 6) },
        { BindingID.ACTIONBUTTON7,  new(BindingID.ACTIONBUTTON7,  "7", ConsoleKey.D7,       "7", 55,  Slot: 7) },
        { BindingID.ACTIONBUTTON8,  new(BindingID.ACTIONBUTTON8,  "8", ConsoleKey.D8,       "8", 56,  Slot: 8) },
        { BindingID.ACTIONBUTTON9,  new(BindingID.ACTIONBUTTON9,  "9", ConsoleKey.D9,       "9", 57,  Slot: 9) },
        { BindingID.ACTIONBUTTON10, new(BindingID.ACTIONBUTTON10, "0", ConsoleKey.D0,       "0", 48,  Slot: 10) },
        { BindingID.ACTIONBUTTON11, new(BindingID.ACTIONBUTTON11, "-", ConsoleKey.OemMinus, "-", 189, Slot: 11) },
        { BindingID.ACTIONBUTTON12, new(BindingID.ACTIONBUTTON12, "=", ConsoleKey.OemPlus,  "=", 187, Slot: 12) },

        // ===== Bottom Right Action Bar: slots 49-58 (NumPad 1-0) =====
        // Note: SetDefaultBindings() only binds buttons 1-10, not 11-12
        { BindingID.MULTIACTIONBAR2BUTTON1,  new(BindingID.MULTIACTIONBAR2BUTTON1,  "N1",     ConsoleKey.NumPad1, "NUMPAD1",      97,  Slot: 49) },
        { BindingID.MULTIACTIONBAR2BUTTON2,  new(BindingID.MULTIACTIONBAR2BUTTON2,  "N2",     ConsoleKey.NumPad2, "NUMPAD2",      98,  Slot: 50) },
        { BindingID.MULTIACTIONBAR2BUTTON3,  new(BindingID.MULTIACTIONBAR2BUTTON3,  "N3",     ConsoleKey.NumPad3, "NUMPAD3",      99,  Slot: 51) },
        { BindingID.MULTIACTIONBAR2BUTTON4,  new(BindingID.MULTIACTIONBAR2BUTTON4,  "N4",     ConsoleKey.NumPad4, "NUMPAD4",      100, Slot: 52) },
        { BindingID.MULTIACTIONBAR2BUTTON5,  new(BindingID.MULTIACTIONBAR2BUTTON5,  "N5",     ConsoleKey.NumPad5, "NUMPAD5",      101, Slot: 53) },
        { BindingID.MULTIACTIONBAR2BUTTON6,  new(BindingID.MULTIACTIONBAR2BUTTON6,  "N6",     ConsoleKey.NumPad6, "NUMPAD6",      102, Slot: 54) },
        { BindingID.MULTIACTIONBAR2BUTTON7,  new(BindingID.MULTIACTIONBAR2BUTTON7,  "N7",     ConsoleKey.NumPad7, "NUMPAD7",      103, Slot: 55) },
        { BindingID.MULTIACTIONBAR2BUTTON8,  new(BindingID.MULTIACTIONBAR2BUTTON8,  "N8",     ConsoleKey.NumPad8, "NUMPAD8",      104, Slot: 56) },
        { BindingID.MULTIACTIONBAR2BUTTON9,  new(BindingID.MULTIACTIONBAR2BUTTON9,  "N9",     ConsoleKey.NumPad9, "NUMPAD9",      105, Slot: 57) },
        { BindingID.MULTIACTIONBAR2BUTTON10, new(BindingID.MULTIACTIONBAR2BUTTON10, "N0",     ConsoleKey.NumPad0, "NUMPAD0",      96,  Slot: 58) },

        // ===== Bottom Left Action Bar: slots 61-72 (Function keys) =====
        { BindingID.MULTIACTIONBAR1BUTTON1,  new(BindingID.MULTIACTIONBAR1BUTTON1,  "F1",  ConsoleKey.F1,  "F1",  112, Slot: 61) },
        { BindingID.MULTIACTIONBAR1BUTTON2,  new(BindingID.MULTIACTIONBAR1BUTTON2,  "F2",  ConsoleKey.F2,  "F2",  113, Slot: 62) },
        { BindingID.MULTIACTIONBAR1BUTTON3,  new(BindingID.MULTIACTIONBAR1BUTTON3,  "F3",  ConsoleKey.F3,  "F3",  114, Slot: 63) },
        { BindingID.MULTIACTIONBAR1BUTTON4,  new(BindingID.MULTIACTIONBAR1BUTTON4,  "F4",  ConsoleKey.F4,  "F4",  115, Slot: 64) },
        { BindingID.MULTIACTIONBAR1BUTTON5,  new(BindingID.MULTIACTIONBAR1BUTTON5,  "F5",  ConsoleKey.F5,  "F5",  116, Slot: 65) },
        { BindingID.MULTIACTIONBAR1BUTTON6,  new(BindingID.MULTIACTIONBAR1BUTTON6,  "F6",  ConsoleKey.F6,  "F6",  117, Slot: 66) },
        { BindingID.MULTIACTIONBAR1BUTTON7,  new(BindingID.MULTIACTIONBAR1BUTTON7,  "F7",  ConsoleKey.F7,  "F7",  118, Slot: 67) },
        { BindingID.MULTIACTIONBAR1BUTTON8,  new(BindingID.MULTIACTIONBAR1BUTTON8,  "F8",  ConsoleKey.F8,  "F8",  119, Slot: 68) },
        { BindingID.MULTIACTIONBAR1BUTTON9,  new(BindingID.MULTIACTIONBAR1BUTTON9,  "F9",  ConsoleKey.F9,  "F9",  120, Slot: 69) },
        { BindingID.MULTIACTIONBAR1BUTTON10, new(BindingID.MULTIACTIONBAR1BUTTON10, "F10", ConsoleKey.F10, "F10", 121, Slot: 70) },
        { BindingID.MULTIACTIONBAR1BUTTON11, new(BindingID.MULTIACTIONBAR1BUTTON11, "F11", ConsoleKey.F11, "F11", 122, Slot: 71) },
        { BindingID.MULTIACTIONBAR1BUTTON12, new(BindingID.MULTIACTIONBAR1BUTTON12, "F12", ConsoleKey.F12, "F12", 123, Slot: 72) },
    };

    /// <summary>
    /// Slot to BindingID lookup for action bars.
    /// </summary>
    public static readonly Dictionary<int, BindingID> SlotToBindingID = new()
    {
        // Main Bar
        { 1,  BindingID.ACTIONBUTTON1 },
        { 2,  BindingID.ACTIONBUTTON2 },
        { 3,  BindingID.ACTIONBUTTON3 },
        { 4,  BindingID.ACTIONBUTTON4 },
        { 5,  BindingID.ACTIONBUTTON5 },
        { 6,  BindingID.ACTIONBUTTON6 },
        { 7,  BindingID.ACTIONBUTTON7 },
        { 8,  BindingID.ACTIONBUTTON8 },
        { 9,  BindingID.ACTIONBUTTON9 },
        { 10, BindingID.ACTIONBUTTON10 },
        { 11, BindingID.ACTIONBUTTON11 },
        { 12, BindingID.ACTIONBUTTON12 },

        // Bottom Right Bar
        { 49, BindingID.MULTIACTIONBAR2BUTTON1 },
        { 50, BindingID.MULTIACTIONBAR2BUTTON2 },
        { 51, BindingID.MULTIACTIONBAR2BUTTON3 },
        { 52, BindingID.MULTIACTIONBAR2BUTTON4 },
        { 53, BindingID.MULTIACTIONBAR2BUTTON5 },
        { 54, BindingID.MULTIACTIONBAR2BUTTON6 },
        { 55, BindingID.MULTIACTIONBAR2BUTTON7 },
        { 56, BindingID.MULTIACTIONBAR2BUTTON8 },
        { 57, BindingID.MULTIACTIONBAR2BUTTON9 },
        { 58, BindingID.MULTIACTIONBAR2BUTTON10 },
        { 59, BindingID.MULTIACTIONBAR2BUTTON11 },
        { 60, BindingID.MULTIACTIONBAR2BUTTON12 },

        // Bottom Left Bar
        { 61, BindingID.MULTIACTIONBAR1BUTTON1 },
        { 62, BindingID.MULTIACTIONBAR1BUTTON2 },
        { 63, BindingID.MULTIACTIONBAR1BUTTON3 },
        { 64, BindingID.MULTIACTIONBAR1BUTTON4 },
        { 65, BindingID.MULTIACTIONBAR1BUTTON5 },
        { 66, BindingID.MULTIACTIONBAR1BUTTON6 },
        { 67, BindingID.MULTIACTIONBAR1BUTTON7 },
        { 68, BindingID.MULTIACTIONBAR1BUTTON8 },
        { 69, BindingID.MULTIACTIONBAR1BUTTON9 },
        { 70, BindingID.MULTIACTIONBAR1BUTTON10 },
        { 71, BindingID.MULTIACTIONBAR1BUTTON11 },
        { 72, BindingID.MULTIACTIONBAR1BUTTON12 },
    };

    /// <summary>
    /// KeyName to BindingID lookup.
    /// </summary>
    public static readonly Dictionary<string, BindingID> KeyNameToBindingID = new()
    {
        // Movement
        { "W", BindingID.MOVEFORWARD },
        { "S", BindingID.MOVEBACKWARD },
        { "Q", BindingID.STRAFELEFT },
        { "E", BindingID.STRAFERIGHT },
        { "A", BindingID.TURNLEFT },
        { "D", BindingID.TURNRIGHT },
        { "Spacebar", BindingID.JUMP },
        { "X", BindingID.SITORSTAND },

        // Targeting
        { "Tab", BindingID.TARGETNEARESTENEMY },
        { "G", BindingID.TARGETLASTTARGET },
        { "F", BindingID.ASSISTTARGET },
        { "Multiply", BindingID.TARGETPET },
        // ALT-PAGEUP: TARGETFOCUS (TBC+) or TARGETPARTYMEMBER1 (Vanilla) - version dependent

        // Combat
        { "Add", BindingID.STARTATTACK },
        { "Subtract", BindingID.PETATTACK },

        // Interaction (ALT-HOME/ALT-END)
        { "Home", BindingID.INTERACTTARGET },
        { "End", BindingID.INTERACTMOUSEOVER },

        // Follow
        { "PageDown", BindingID.FOLLOWTARGET },

        // Custom Actions (ALT-DELETE/ALT-INSERT, SHIFT-PAGEUP/SHIFT-PAGEDOWN)
        { "Delete", BindingID.CUSTOM_STOPATTACK },
        { "Insert", BindingID.CUSTOM_CLEARTARGET },
        { "PageUp", BindingID.CUSTOM_CONFIG },
        // Note: PageDown maps to CUSTOM_FLUSH, but FOLLOWTARGET also uses PageDown (ALT-PAGEDOWN)
        // The KeyNameToBindingID lookup is ambiguous - use BindingID directly when needed

        // Main Bar
        { "1", BindingID.ACTIONBUTTON1 },
        { "2", BindingID.ACTIONBUTTON2 },
        { "3", BindingID.ACTIONBUTTON3 },
        { "4", BindingID.ACTIONBUTTON4 },
        { "5", BindingID.ACTIONBUTTON5 },
        { "6", BindingID.ACTIONBUTTON6 },
        { "7", BindingID.ACTIONBUTTON7 },
        { "8", BindingID.ACTIONBUTTON8 },
        { "9", BindingID.ACTIONBUTTON9 },
        { "0", BindingID.ACTIONBUTTON10 },
        { "-", BindingID.ACTIONBUTTON11 },
        { "=", BindingID.ACTIONBUTTON12 },

        // Bottom Right Bar (NumPad)
        { "N1",     BindingID.MULTIACTIONBAR2BUTTON1 },
        { "N2",     BindingID.MULTIACTIONBAR2BUTTON2 },
        { "N3",     BindingID.MULTIACTIONBAR2BUTTON3 },
        { "N4",     BindingID.MULTIACTIONBAR2BUTTON4 },
        { "N5",     BindingID.MULTIACTIONBAR2BUTTON5 },
        { "N6",     BindingID.MULTIACTIONBAR2BUTTON6 },
        { "N7",     BindingID.MULTIACTIONBAR2BUTTON7 },
        { "N8",     BindingID.MULTIACTIONBAR2BUTTON8 },
        { "N9",     BindingID.MULTIACTIONBAR2BUTTON9 },
        { "N0",     BindingID.MULTIACTIONBAR2BUTTON10 },
        // Note: MULTIACTIONBAR2BUTTON11-12 not bound by SetDefaultBindings()

        // Bottom Left Bar (Function keys)
        { "F1",  BindingID.MULTIACTIONBAR1BUTTON1 },
        { "F2",  BindingID.MULTIACTIONBAR1BUTTON2 },
        { "F3",  BindingID.MULTIACTIONBAR1BUTTON3 },
        { "F4",  BindingID.MULTIACTIONBAR1BUTTON4 },
        { "F5",  BindingID.MULTIACTIONBAR1BUTTON5 },
        { "F6",  BindingID.MULTIACTIONBAR1BUTTON6 },
        { "F7",  BindingID.MULTIACTIONBAR1BUTTON7 },
        { "F8",  BindingID.MULTIACTIONBAR1BUTTON8 },
        { "F9",  BindingID.MULTIACTIONBAR1BUTTON9 },
        { "F10", BindingID.MULTIACTIONBAR1BUTTON10 },
        { "F11", BindingID.MULTIACTIONBAR1BUTTON11 },
        { "F12", BindingID.MULTIACTIONBAR1BUTTON12 },
    };

    public static KeyBinding? GetBySlot(int slot)
    {
        if (SlotToBindingID.TryGetValue(slot, out var bindingId) &&
            Bindings.TryGetValue(bindingId, out var binding))
            return binding;
        return null;
    }

    public static KeyBinding? GetByKeyName(string keyName)
    {
        if (KeyNameToBindingID.TryGetValue(keyName, out var bindingId) &&
            Bindings.TryGetValue(bindingId, out var binding))
            return binding;
        return null;
    }

    public static KeyBinding? GetByBindingID(BindingID bindingId)
    {
        if (Bindings.TryGetValue(bindingId, out var binding))
            return binding;
        return null;
    }
}
