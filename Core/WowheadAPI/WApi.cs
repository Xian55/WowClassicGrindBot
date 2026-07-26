using SharedLib;

namespace Core;

public sealed class WApi
{
    private string BaseUrl { get; }

    private string BaseUIMapUrl { get; }

    public WApi(StartupClientVersion scv)
    {
        // Each case pairs the modern Classic client with its Legacy_* equivalent:
        // they are the same expansion's content, so listing only the modern one
        // silently sent every legacy client to the retail site.
        BaseUrl = scv.Version switch
        {
            ClientVersion.SoM or ClientVersion.Legacy_Vanilla => "https://classic.wowhead.com",
            ClientVersion.TBC or ClientVersion.Legacy_TBC => "https://tbc.wowhead.com",
            ClientVersion.Wrath or ClientVersion.Legacy_Wrath => "https://www.wowhead.com/wotlk",
            ClientVersion.Cata or ClientVersion.Legacy_Cata => "https://www.wowhead.com/cata",
            ClientVersion.Mop or ClientVersion.Legacy_Mop => "https://www.wowhead.com/mop-classic",
            _ => "https://www.wowhead.com",
        };

        BaseUIMapUrl = scv.Version switch
        {
            ClientVersion.SoM or ClientVersion.Legacy_Vanilla => "https://wow.zamimg.com/images/wow/classic/maps/enus/original/",
            ClientVersion.TBC or ClientVersion.Legacy_TBC => "https://wow.zamimg.com/images/wow/tbc/maps/enus/original/",
            ClientVersion.Wrath or ClientVersion.Legacy_Wrath => "https://wow.zamimg.com/images/wow/wrath/maps/enus/original/",
            ClientVersion.Cata or ClientVersion.Legacy_Cata => "https://wow.zamimg.com/images/wow/cata/maps/enus/original/",

            // Mists deliberately uses the unversioned (retail) branch: zamimg has
            // no mop/ or mop-classic/ directory, and the retail one does carry the
            // Pandaria maps. Measured - area 5785 (The Jade Forest), 5840 (Vale of
            // Eternal Blossoms) and 5841 (Kun-Lai Summit) all 200 there and 404
            // under cata/. Do not "fix" this into a /mop/ path.
            ClientVersion.Mop or ClientVersion.Legacy_Mop => "https://wow.zamimg.com/images/wow/maps/enus/original/",

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