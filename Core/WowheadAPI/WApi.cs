using SharedLib;

namespace Core;

public sealed class WApi
{
    private string BaseUrl { get; }

    private string BaseUIMapUrl { get; }

    public WApi(StartupClientVersion scv)
    {
        BaseUrl = scv.Version switch
        {
            ClientVersion.SoM => "https://classic.wowhead.com",
            ClientVersion.TBC => "https://tbc.wowhead.com",
            ClientVersion.Wrath => "https://www.wowhead.com/wotlk",
            ClientVersion.Cata => "https://www.wowhead.com/cata",
            _ => "https://www.wowhead.com",
        };

        BaseUIMapUrl = scv.Version switch
        {
            ClientVersion.SoM => "https://wow.zamimg.com/images/wow/classic/maps/enus/original/",
            ClientVersion.TBC => "https://wow.zamimg.com/images/wow/tbc/maps/enus/original/",
            ClientVersion.Wrath => "https://wow.zamimg.com/images/wow/wrath/maps/enus/original/",
            ClientVersion.Cata => "https://wow.zamimg.com/images/wow/cata/maps/enus/original/",
            _ => "https://wow.zamimg.com/images/wow/maps/enus/original/",
        };

    }

    public string NpcId => $"{BaseUrl}/npc=";
    public string ItemId => $"{BaseUrl}/item=";
    public string SpellId => $"{BaseUrl}/spell=";

    public string GetMapImage(int areaId)
    {
        return $"{BaseUIMapUrl}{areaId}.jpg";
    }
}