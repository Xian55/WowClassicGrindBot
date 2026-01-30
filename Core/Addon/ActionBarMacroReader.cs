using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;

namespace Core;

public sealed partial class ActionBarMacroReader : IReader
{
    private const int MACRO_SLOT = 108;

    private readonly ILogger<ActionBarMacroReader> logger;
    private readonly Dictionary<int, int> slotMacroHashes = [];

    private bool initialized;

    public int Count => slotMacroHashes.Count;
    public bool IsInitialized => initialized;

    public IReadOnlyDictionary<int, int> SlotMacroHashes => slotMacroHashes;

    /// <summary>
    /// Raised when a slot macro changes. Parameters are (slot, nameHash).
    /// </summary>
    public event Action<int, int>? MacroChanged;

    public ActionBarMacroReader(ILogger<ActionBarMacroReader> logger)
    {
        this.logger = logger;

        // Set static reference for KeyReader macro detection
        KeyReader.MacroReader = this;
    }

    public void Update(IAddonDataProvider reader)
    {
        int encodedValue = reader.GetInt(MACRO_SLOT);
        if (encodedValue == 0)
        {
            // Queue exhausted, mark as initialized if we received any macros
            if (slotMacroHashes.Count > 0 && !initialized)
            {
                initialized = true;
                LogMacrosInitialized(logger, slotMacroHashes.Count);
            }
            return;
        }

        var decoded = DecodeMacro(encodedValue);
        if (decoded.HasValue)
        {
            int slot = decoded.Value.slot;
            int nameHash = decoded.Value.nameHash;

            bool changed = false;
            if (!slotMacroHashes.TryGetValue(slot, out var existingHash) || existingHash != nameHash)
            {
                changed = true;
                if (nameHash > 0)
                {
                    slotMacroHashes[slot] = nameHash;
                    LogMacroReceived(logger, slot, nameHash);
                }
                else
                {
                    slotMacroHashes.Remove(slot);
                    LogMacroCleared(logger, slot);
                }
            }

            if (changed)
            {
                MacroChanged?.Invoke(slot, nameHash);
            }
        }
    }

    public void Reset()
    {
        slotMacroHashes.Clear();
        initialized = false;
    }

    /// <summary>
    /// Gets the macro name hash for an action bar slot.
    /// </summary>
    public bool TryGetMacroHash(int slot, out int nameHash)
    {
        return slotMacroHashes.TryGetValue(slot, out nameHash);
    }

    /// <summary>
    /// Checks if a slot has a macro.
    /// </summary>
    public bool HasMacro(int slot)
    {
        return slotMacroHashes.TryGetValue(slot, out var hash) && hash > 0;
    }

    /// <summary>
    /// Finds the best slot for a macro name, preferring slots in the specified range.
    /// Returns 0 if macro not found.
    /// </summary>
    public int FindSlotByMacroName(string macroName, int preferredSlotMin = 0, int preferredSlotMax = 0)
    {
        // Apply same modulo as encoding - stored hashes are truncated to fit in pixel encoding
        int targetHash = ComputeDJB2Hash24(macroName) % MACRO_MULTIPLIER;
        if (targetHash == 0)
            return 0;

        int fallbackSlot = 0;

        foreach (var (slot, hash) in slotMacroHashes)
        {
            if (hash != targetHash)
                continue;

            // Check if this slot is in the preferred range
            if (preferredSlotMin > 0 && preferredSlotMax > 0)
            {
                if (slot >= preferredSlotMin && slot <= preferredSlotMax)
                    return slot; // Found in preferred range
            }

            // Remember as fallback
            if (fallbackSlot == 0)
                fallbackSlot = slot;
        }

        return fallbackSlot;
    }

    /// <summary>
    /// DJB2 hash function (24-bit) - must match Lua implementation.
    /// Converts to lowercase before hashing for case-insensitive matching.
    /// Zero allocation implementation using ReadOnlySpan.
    /// </summary>
    public static int ComputeDJB2Hash24(ReadOnlySpan<char> str)
    {
        if (str.IsEmpty)
            return 0;

        uint hash = 5381;

        foreach (char c in str)
        {
            hash = ((hash * 33) + char.ToLowerInvariant(c)) % 16777216;
        }

        return (int)hash;
    }

    /// <summary>
    /// Decodes the encoded macro value.
    /// Format: index * 200000 + (nameHash % 200000)
    /// </summary>
    private const int MACRO_MULTIPLIER = 200000;

    private static (int slot, int nameHash)? DecodeMacro(int encoded)
    {
        if (encoded <= 0)
            return null;

        int index = encoded / MACRO_MULTIPLIER;
        int nameHash = encoded % MACRO_MULTIPLIER;

        int slot = IndexToSlot(index);
        if (slot == 0)
            return null;

        return (slot, nameHash);
    }

    private static int IndexToSlot(int index)
    {
        // Main bar: indices 1-12 -> slots 1-12
        if (index >= 1 && index <= 12)
            return index;

        // Bottom Right bar: indices 13-24 -> slots 49-60
        if (index >= 13 && index <= 24)
            return index - 13 + 49;

        // Bottom Left bar: indices 25-36 -> slots 61-72
        if (index >= 25 && index <= 36)
            return index - 25 + 61;

        // Stance bar 1: indices 37-48 -> slots 73-84
        if (index >= 37 && index <= 48)
            return index - 37 + 73;

        // Stance bar 2: indices 49-60 -> slots 85-96
        if (index >= 49 && index <= 60)
            return index - 49 + 85;

        // Stance bar 3: indices 61-72 -> slots 97-108
        if (index >= 61 && index <= 72)
            return index - 61 + 97;

        // Stance bar 4: indices 73-84 -> slots 109-120
        if (index >= 73 && index <= 84)
            return index - 73 + 109;

        return 0;
    }

    #region Logging

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Trace,
        Message = "Action bar macro received: slot {slot} -> hash {nameHash}")]
    static partial void LogMacroReceived(ILogger logger, int slot, int nameHash);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Action bar macros initialized with {count} slots")]
    static partial void LogMacrosInitialized(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Trace,
        Message = "Action bar macro cleared: slot {slot}")]
    static partial void LogMacroCleared(ILogger logger, int slot);

    #endregion
}
