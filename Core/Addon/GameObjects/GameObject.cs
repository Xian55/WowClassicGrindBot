using System;

namespace Core.Addon;

public static class GameObject
{
    public static bool IsHerb(int id) => Enum.IsDefined((Herb)id);

    public static bool IsMineral(int id) => Enum.IsDefined((Mineral)id);

    public static bool IsMailbox(int id) => id == (int)General.Mailbox;
}
