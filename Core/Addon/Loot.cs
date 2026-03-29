namespace Core;

public enum LootStatus
{
    CORPSE = 0,
    READY = 1,
    CLOSED = 2
}

public static class Loot
{
    public const int LOOTFRAME_AUTOLOOT_DELAY_MS = 300;

    public const int LOOTFRAME_OPEN_TIME_MS = 2000;

    public const int LOOT_PER_ITEM_TIME_MS = 1000;

    public const int RESET_UPDATE_COUNT = 5;
}