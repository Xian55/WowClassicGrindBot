using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Collections.Generic;

namespace Core;

public sealed partial class KeyBindingsReader : IReader
{
    private const int BINDING_SLOT = 106;

    private readonly ILogger<KeyBindingsReader> logger;
    private readonly Dictionary<BindingID, (ConsoleKey Key, ModifierKey Modifier)> bindings = [];
    private readonly Dictionary<BindingID, (ConsoleKey Key, ModifierKey Modifier)> secondaryBindings = [];

    private bool initialized;

    public int Count => bindings.Count;
    public bool IsInitialized => initialized;

    public IReadOnlyDictionary<BindingID, (ConsoleKey Key, ModifierKey Modifier)> Bindings => bindings;
    public IReadOnlyDictionary<BindingID, (ConsoleKey Key, ModifierKey Modifier)> SecondaryBindings => secondaryBindings;

    /// <summary>
    /// Raised when a binding is added or changed.
    /// </summary>
    public event Action<BindingID>? BindingChanged;

    public KeyBindingsReader(ILogger<KeyBindingsReader> logger)
    {
        this.logger = logger;
    }

    public void Update(IAddonDataProvider reader)
    {
        int encodedValue = reader.GetInt(BINDING_SLOT);
        if (encodedValue == 0)
        {
            // Queue exhausted, mark as initialized if we received any bindings
            if (bindings.Count > 0 && !initialized)
            {
                initialized = true;
                LogBindingsInitialized(logger, bindings.Count);
            }
            return;
        }

        var decoded = KeyReader.DecodeBinding(encodedValue);
        if (decoded.HasValue)
        {
            bool changed = false;
            var bindingId = decoded.Value.bindingId;

            if (decoded.Value.key1 != ConsoleKey.NoName)
            {
                var newBinding = (decoded.Value.key1, decoded.Value.mod1);
                // Check if this is a new or changed binding
                if (!bindings.TryGetValue(bindingId, out var existingBinding) ||
                    existingBinding.Key != newBinding.key1 ||
                    existingBinding.Modifier != newBinding.mod1)
                {
                    bindings[bindingId] = newBinding;
                    // Sync to KeyReader.GameBindings for key resolution
                    KeyReader.GameBindings[bindingId] = newBinding;
                    changed = true;
                    if (logger.IsEnabled(LogLevel.Trace))
                        LogBindingReceived(logger, bindingId.ToStringF(), decoded.Value.mod1.ToPrefix(), decoded.Value.key1);
                }
            }
            else if (bindings.Remove(bindingId))
            {
                // Key was unbound
                KeyReader.GameBindings.Remove(bindingId);
                changed = true;
                if (logger.IsEnabled(LogLevel.Trace))
                    LogBindingRemoved(logger, bindingId.ToStringF());
            }

            if (decoded.Value.key2 != ConsoleKey.NoName)
            {
                var newBinding = (decoded.Value.key2, decoded.Value.mod2);
                if (!secondaryBindings.TryGetValue(bindingId, out var existingBinding) ||
                    existingBinding.Key != newBinding.key2 ||
                    existingBinding.Modifier != newBinding.mod2)
                {
                    secondaryBindings[bindingId] = newBinding;
                    // Sync to KeyReader.GameBindingsSecondary
                    KeyReader.GameBindingsSecondary[bindingId] = newBinding;
                    changed = true;
                    if (logger.IsEnabled(LogLevel.Trace))
                        LogSecondaryBindingReceived(logger, bindingId.ToStringF(), decoded.Value.mod2.ToPrefix(), decoded.Value.key2);
                }
            }
            else if (secondaryBindings.Remove(bindingId))
            {
                KeyReader.GameBindingsSecondary.Remove(bindingId);
                changed = true;
            }

            if (changed)
            {
                BindingChanged?.Invoke(bindingId);
            }
        }
    }

    public void Reset()
    {
        bindings.Clear();
        secondaryBindings.Clear();
        KeyReader.GameBindings.Clear();
        KeyReader.GameBindingsSecondary.Clear();
        initialized = false;
    }

    /// <summary>
    /// Gets the primary ConsoleKey and Modifier bound to a BindingID in-game.
    /// </summary>
    public bool TryGetBinding(BindingID bindingId, out ConsoleKey key, out ModifierKey modifier)
    {
        if (bindings.TryGetValue(bindingId, out var binding))
        {
            key = binding.Key;
            modifier = binding.Modifier;
            return true;
        }
        key = ConsoleKey.NoName;
        modifier = ModifierKey.None;
        return false;
    }

    /// <summary>
    /// Gets the secondary ConsoleKey and Modifier bound to a BindingID in-game.
    /// </summary>
    public bool TryGetSecondaryBinding(BindingID bindingId, out ConsoleKey key, out ModifierKey modifier)
    {
        if (secondaryBindings.TryGetValue(bindingId, out var binding))
        {
            key = binding.Key;
            modifier = binding.Modifier;
            return true;
        }
        key = ConsoleKey.NoName;
        modifier = ModifierKey.None;
        return false;
    }

    /// <summary>
    /// Checks if the in-game binding matches the expected key and modifier.
    /// Checks both primary and secondary bindings.
    /// </summary>
    public bool BindingMatches(BindingID bindingId, ConsoleKey expectedKey, ModifierKey expectedModifier = ModifierKey.None)
    {
        // Check primary binding
        if (bindings.TryGetValue(bindingId, out var primaryBinding))
        {
            if (primaryBinding.Key == expectedKey && primaryBinding.Modifier == expectedModifier)
                return true;
        }

        // Check secondary binding
        if (secondaryBindings.TryGetValue(bindingId, out var secondaryBinding))
        {
            if (secondaryBinding.Key == expectedKey && secondaryBinding.Modifier == expectedModifier)
                return true;
        }

        // Binding not received from game or doesn't match
        return false;
    }

    /// <summary>
    /// Gets all bindings that don't match the expected KeyActions.
    /// A binding matches if either the primary or secondary key and modifier equal the expected values.
    /// </summary>
    public List<BindingMismatch> GetMismatches(IEnumerable<KeyAction> keyActions)
    {
        List<BindingMismatch> mismatches = [];

        foreach (KeyAction keyAction in keyActions)
        {
            if (keyAction.BindingID == BindingID.None)
                continue;

            // Check if either primary or secondary binding matches
            if (BindingMatches(keyAction.BindingID, keyAction.ConsoleKey, keyAction.Modifier))
                continue;

            // Get the actual binding for reporting (prefer primary)
            ConsoleKey actualKey = ConsoleKey.NoName;
            ModifierKey actualModifier = ModifierKey.None;
            if (bindings.TryGetValue(keyAction.BindingID, out var primaryBinding))
            {
                actualKey = primaryBinding.Key;
                actualModifier = primaryBinding.Modifier;
            }
            else if (secondaryBindings.TryGetValue(keyAction.BindingID, out var secondaryBinding))
            {
                actualKey = secondaryBinding.Key;
                actualModifier = secondaryBinding.Modifier;
            }

            mismatches.Add(new BindingMismatch(
                keyAction.BindingID,
                keyAction.ConsoleKey,
                keyAction.Modifier,
                actualKey,
                actualModifier));
        }

        return mismatches;
    }

    #region Logging

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Trace,
        Message = "Binding received: {bindingId} -> {modifierPrefix}{key}")]
    static partial void LogBindingReceived(ILogger logger, string bindingId, string modifierPrefix, ConsoleKey key);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Key bindings initialized with {count} bindings from game")]
    static partial void LogBindingsInitialized(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Trace,
        Message = "Secondary binding received: {bindingId} -> {modifierPrefix}{key}")]
    static partial void LogSecondaryBindingReceived(ILogger logger, string bindingId, string modifierPrefix, ConsoleKey key);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Trace,
        Message = "Binding removed: {bindingId}")]
    static partial void LogBindingRemoved(ILogger logger, string bindingId);

    #endregion
}

public readonly record struct BindingMismatch(
    BindingID BindingId,
    ConsoleKey ExpectedKey,
    ModifierKey ExpectedModifier,
    ConsoleKey ActualKey,
    ModifierKey ActualModifier);
