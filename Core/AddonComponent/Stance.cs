using SharedLib;

namespace Core;

public sealed class Stance : IReader
{
    private const int cell = 48;

    private int value;

    public Stance() { }

    public void Update(IAddonDataProvider reader)
    {
        value = reader.GetInt(cell);
    }

    public Form Get(UnitClass @class, bool stealth, ClientVersion version)
    => value == 0 ? Form.None : @class switch
    {
        UnitClass.Warrior => Form.Warrior_BattleStance + value - 1,
        UnitClass.Rogue => Form.Rogue_Stealth + value - 1,
        UnitClass.Priest => Form.Priest_Shadowform + value - 1,
        UnitClass.Druid =>
            version == ClientVersion.Wrath
            ? Form.Druid_Bear + value - 1
            : (stealth ? Form.Druid_Cat_Prowl : Form.Druid_Bear + value - 1),
        UnitClass.Paladin => Form.Paladin_Devotion_Aura + value - 1,
        UnitClass.Shaman => Form.Shaman_GhostWolf + value - 1,
        UnitClass.DeathKnight => Form.DeathKnight_Blood_Presence + value - 1,
        _ => Form.None
    };

    public static int ToSlot(KeyAction item, PlayerReader playerReader)
    {
        return item.Slot <= ActionBar.MAIN_ACTIONBAR_SLOT
            ? item.Slot + (int)FormToActionBar(playerReader.Class,
                item.HasForm
                ? item.FormValue
                : playerReader.Form)
            : item.Slot;
    }

    private static StanceActionBar FormToActionBar(UnitClass @class, Form form)
    {
        switch (@class)
        {
            case UnitClass.Druid:
                switch (form)
                {
                    case Form.Druid_Cat:
                    case Form.Druid_Cat_Prowl: // Prowl doesn't change action bar in Classic
                        return StanceActionBar.DruidCat;
                    case Form.Druid_Bear:
                        return StanceActionBar.DruidBear;
                    case Form.Druid_Moonkin:
                        return StanceActionBar.DruidMoonkin;
                }
                break;
            case UnitClass.Warrior:
                switch (form)
                {
                    case Form.Warrior_BattleStance:
                        return StanceActionBar.WarriorBattleStance;
                    case Form.Warrior_DefensiveStance:
                        return StanceActionBar.WarriorDefensiveStance;
                    case Form.Warrior_BerserkerStance:
                        return StanceActionBar.WarriorBerserkerStance;
                }
                break;
            case UnitClass.Rogue:
                if (form == Form.Rogue_Stealth)
                    return StanceActionBar.RogueStealth;
                break;
            case UnitClass.Priest:
                if (form == Form.Priest_Shadowform)
                    return StanceActionBar.PriestShadowform;
                break;
        }

        return StanceActionBar.None;
    }



}
