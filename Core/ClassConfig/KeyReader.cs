using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;

namespace Core;

public static class KeyReader
{
    // Bottom Right Action Bar
    public const string BR = "N";
    public const int BRIdx = 48; //49 - 1

    // Bottom Left Action Bar
    public const string BL = "F";
    public const int BLIdx = 60; //61 - 1

    public static Dictionary<string, ConsoleKey> KeyMapping { get; } = new()
    {
        { "1", ConsoleKey.D1 },
        { "2", ConsoleKey.D2 },
        { "3", ConsoleKey.D3 },
        { "4", ConsoleKey.D4 },
        { "5", ConsoleKey.D5 },
        { "6", ConsoleKey.D6 },
        { "7", ConsoleKey.D7 },
        { "8", ConsoleKey.D8 },
        { "9", ConsoleKey.D9 },
        { "0", ConsoleKey.D0 },

        { BR + "1", ConsoleKey.NumPad1 },
        { BR + "2", ConsoleKey.NumPad2 },
        { BR + "3", ConsoleKey.NumPad3 },
        { BR + "4", ConsoleKey.NumPad4 },
        { BR + "5", ConsoleKey.NumPad5 },
        { BR + "6", ConsoleKey.NumPad6 },
        { BR + "7", ConsoleKey.NumPad7 },
        { BR + "8", ConsoleKey.NumPad8 },
        { BR + "9", ConsoleKey.NumPad9 },
        { BR + "0", ConsoleKey.NumPad0 },

        { BL + "0", ConsoleKey.F10 },
        { BL + "1", ConsoleKey.F1 },
        { BL + "2", ConsoleKey.F2 },
        { BL + "3", ConsoleKey.F3 },
        { BL + "4", ConsoleKey.F4 },
        { BL + "5", ConsoleKey.F5 },
        { BL + "6", ConsoleKey.F6 },
        { BL + "7", ConsoleKey.F7 },
        { BL + "8", ConsoleKey.F8 },
        { BL + "9", ConsoleKey.F9 },
        { BL + "11", ConsoleKey.F11 },
        { BL + "12", ConsoleKey.F12 },

        { "Space", ConsoleKey.Spacebar },
        { " ", ConsoleKey.Spacebar },
        { "-", ConsoleKey.OemMinus },
        { "=", ConsoleKey.OemPlus },
    };

    public static Dictionary<string, int> ActionBarSlotMap { get; } = new()
    {
        // ActionBar page 1: slots 1 to 12
        { "1", 1 },
        { "2", 2 },
        { "3", 3 },
        { "4", 4 },
        { "5", 5 },
        { "6", 6 },
        { "7", 7 },
        { "8", 8 },
        { "9", 9 },
        { "0", 10 },
        { "-", 11 }, // English keyboard layout required
        { "=", 12 }, // English keyboard layout required

        // ActionBar page 2: slots 13 to 24
        // ActionBar page 3 (Right ActionBar): slots 25 to 36
        // ActionBar page 4 (Right ActionBar 2): slots 37 to 48
        
        // ActionBar page 5 (Bottom Right ActionBar): slots 49 to 60
        { BR + "1", 49 },
        { BR + "2", 50 },
        { BR + "3", 51 },
        { BR + "4", 52 },
        { BR + "5", 53 },
        { BR + "6", 54 },
        { BR + "7", 55 },
        { BR + "8", 56 },
        { BR + "9", 57 },
        { BR + "0", 58 },
        //11 - unused
        //12 - unused

        // ActionBar page 6 (Bottom Left ActionBar): slots 61 to 72
        { BL + "1", 61 },
        { BL + "2", 62 },
        { BL + "3", 63 },
        { BL + "4", 64 },
        { BL + "5", 65 },
        { BL + "6", 66 },
        { BL + "7", 67 },
        { BL + "8", 68 },
        { BL + "9", 69 },
        { BL + "10", 70 },
        { BL + "11", 71 },
        { BL + "12", 72 }
    };

    public static bool ReadKey(ILogger logger, KeyAction key)
    {
        if (string.IsNullOrEmpty(key.Key))
        {
            return false;
        }

        if (KeyMapping.TryGetValue(key.Key, out ConsoleKey consoleKey))
        {
            key.ConsoleKey = consoleKey;
        }
        else
        {
            if (!Enum.TryParse(key.Key, true, out consoleKey))
            {
                return false;
            }

            key.ConsoleKey = consoleKey;
        }

        if (ActionBarSlotMap.TryGetValue(key.Key, out int slot))
        {
            key.Slot = slot;
        }
        else if (!key.BaseAction)
        {
            logger.LogWarning($"[{key.Name}] Unable to assign Actionbar {nameof(KeyAction.Slot)}!");
        }

        return true;
    }
}