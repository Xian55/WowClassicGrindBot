using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;

namespace Core;

public sealed partial class ActionBarTextureReader : IReader
{
    private const int TEXTURE_SLOT = 107;

    private readonly ILogger<ActionBarTextureReader> logger;
    private readonly Dictionary<int, int> slotTextures = [];

    private bool initialized;

    public int Count => slotTextures.Count;
    public bool IsInitialized => initialized;

    public IReadOnlyDictionary<int, int> SlotTextures => slotTextures;

    /// <summary>
    /// Raised when a slot texture changes. Parameter is the action bar slot.
    /// Slot ranges: 1-12 (main), 49-60 (bottom right), 61-72 (bottom left),
    /// 73-84 (stance 1), 85-96 (stance 2), 97-108 (stance 3), 109-120 (stance 4).
    /// </summary>
    public event Action<int, int>? TextureChanged;

    public ActionBarTextureReader(ILogger<ActionBarTextureReader> logger)
    {
        this.logger = logger;

        // Set static reference for KeyReader slot detection
        KeyReader.TextureReader = this;
    }

    public void Update(IAddonDataProvider reader)
    {
        int encodedValue = reader.GetInt(TEXTURE_SLOT);
        if (encodedValue == 0)
        {
            // Queue exhausted, mark as initialized if we received any textures
            if (slotTextures.Count > 0 && !initialized)
            {
                initialized = true;
                LogTexturesInitialized(logger, slotTextures.Count);
            }
            return;
        }

        var decoded = DecodeTexture(encodedValue);
        if (decoded.HasValue)
        {
            int slot = decoded.Value.slot;
            int textureId = decoded.Value.textureId;

            bool changed = false;
            if (!slotTextures.TryGetValue(slot, out var existingTexture) || existingTexture != textureId)
            {
                changed = true;
                if (textureId > 0)
                {
                    slotTextures[slot] = textureId;
                    LogTextureReceived(logger, slot, textureId);
                }
                else
                {
                    slotTextures.Remove(slot);
                    LogTextureCleared(logger, slot);
                }
            }

            if (changed)
            {
                TextureChanged?.Invoke(slot, textureId);
            }
        }
    }

    public void Reset()
    {
        slotTextures.Clear();
        initialized = false;
    }

    /// <summary>
    /// Gets the texture ID for an action bar slot.
    /// </summary>
    public bool TryGetTexture(int slot, out int textureId)
    {
        return slotTextures.TryGetValue(slot, out textureId);
    }

    /// <summary>
    /// Checks if a slot has any action (texture > 0).
    /// </summary>
    public bool HasAction(int slot)
    {
        return slotTextures.TryGetValue(slot, out var textureId) && textureId > 0;
    }

    /// <summary>
    /// Finds all slots that have the specified texture ID.
    /// </summary>
    public List<int> FindSlotsByTexture(int textureId)
    {
        List<int> slots = [];
        foreach (var (slot, texture) in slotTextures)
        {
            if (texture == textureId)
                slots.Add(slot);
        }
        return slots;
    }

    /// <summary>
    /// Finds the best slot for a texture, preferring slots in the specified range.
    /// If no preferred slot found, returns any slot with that texture.
    /// Returns 0 if texture not found.
    /// </summary>
    public int FindSlotByTexture(int textureId, int preferredSlotMin = 0, int preferredSlotMax = 0)
    {
        int fallbackSlot = 0;

        foreach (var (slot, texture) in slotTextures)
        {
            if (texture != textureId)
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
    /// Finds a slot by texture, searching any of the provided texture IDs.
    /// Returns (slot, textureId) or (0, 0) if not found.
    /// </summary>
    public (int slot, int textureId) FindSlotByTextures(IEnumerable<int> textureIds, int preferredSlotMin = 0, int preferredSlotMax = 0)
    {
        foreach (int textureId in textureIds)
        {
            int slot = FindSlotByTexture(textureId, preferredSlotMin, preferredSlotMax);
            if (slot > 0)
                return (slot, textureId);
        }
        return (0, 0);
    }

    /// <summary>
    /// Decodes the encoded texture value.
    /// Format: index * 190000 + (textureId % 190000)
    /// Uses 190000 multiplier to fit in 24 bits (max: 84 * 190000 + 189999 = 16,149,999)
    /// Index 1-12 = Main bar slots 1-12
    /// Index 13-24 = Bottom Right bar slots 49-60
    /// Index 25-36 = Bottom Left bar slots 61-72
    /// Index 37-48 = Stance bar 1 slots 73-84
    /// Index 49-60 = Stance bar 2 slots 85-96
    /// Index 61-72 = Stance bar 3 slots 97-108
    /// Index 73-84 = Stance bar 4 slots 109-120
    /// </summary>
    private const int TEXTURE_MULTIPLIER = 190000;

    private static (int slot, int textureId)? DecodeTexture(int encoded)
    {
        if (encoded <= 0)
            return null;

        int index = encoded / TEXTURE_MULTIPLIER;
        int textureId = encoded % TEXTURE_MULTIPLIER;

        int slot = IndexToSlot(index);
        if (slot == 0)
            return null;

        return (slot, textureId);
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
        Message = "Action bar texture received: slot {slot} -> {textureId}")]
    static partial void LogTextureReceived(ILogger logger, int slot, int textureId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Action bar textures initialized with {count} slots")]
    static partial void LogTexturesInitialized(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Trace,
        Message = "Action bar texture cleared: slot {slot}")]
    static partial void LogTextureCleared(ILogger logger, int slot);

    #endregion
}
