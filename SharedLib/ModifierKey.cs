using System;

namespace SharedLib;

/// <summary>
/// Modifier keys that can be combined with regular keys.
/// Values match the encoding used in Lua addon (0=none, 1=Shift, 2=Ctrl, 3=Alt).
/// </summary>
[Flags]
public enum ModifierKey : byte
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4
}

public static class ModifierKeyExtensions
{
    /// <summary>
    /// Parses a modifier from the 2-bit encoded value from Lua.
    /// 0=None, 1=Shift, 2=Ctrl, 3=Alt
    /// </summary>
    public static ModifierKey FromEncodedValue(int value)
    {
        return value switch
        {
            1 => ModifierKey.Shift,
            2 => ModifierKey.Ctrl,
            3 => ModifierKey.Alt,
            _ => ModifierKey.None
        };
    }

    /// <summary>
    /// Converts modifier to the 2-bit encoded value for Lua.
    /// </summary>
    public static int ToEncodedValue(this ModifierKey modifier)
    {
        return modifier switch
        {
            ModifierKey.Shift => 1,
            ModifierKey.Ctrl => 2,
            ModifierKey.Alt => 3,
            _ => 0
        };
    }

    /// <summary>
    /// Formats the modifier as a prefix string (e.g., "Shift-", "Ctrl-", "Alt-").
    /// </summary>
    public static string ToPrefix(this ModifierKey modifier)
    {
        return modifier switch
        {
            ModifierKey.Shift => "Shift-",
            ModifierKey.Ctrl => "Ctrl-",
            ModifierKey.Alt => "Alt-",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Parses a key string like "Shift-F" or "CTRL-1" and extracts the modifier.
    /// Returns the base key string without the modifier prefix.
    /// </summary>
    public static (string baseKey, ModifierKey modifier) ParseKeyString(string keyString)
    {
        if (string.IsNullOrEmpty(keyString))
            return (keyString, ModifierKey.None);

        // Check for modifier prefixes (case-insensitive)
        string upper = keyString.ToUpperInvariant();

        if (upper.StartsWith("SHIFT-"))
            return (keyString[6..], ModifierKey.Shift);

        if (upper.StartsWith("CTRL-"))
            return (keyString[5..], ModifierKey.Ctrl);

        if (upper.StartsWith("ALT-"))
            return (keyString[4..], ModifierKey.Alt);

        return (keyString, ModifierKey.None);
    }
}
