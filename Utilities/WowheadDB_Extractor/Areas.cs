using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WowheadDB_Extractor
{
    public static class Areas
    {
        public static string GetUrl(string key)
        {
            if (classic.TryGetValue(key, out int cl))
                return "https://wow.zamimg.com/images/wow/classic/maps/enus/zoom/" + cl + ".jpg";

            if (tbc.TryGetValue(key, out int tbcl))
                return "https://wow.zamimg.com/images/wow/tbc/maps/enus/zoom/" + tbcl + ".jpg";

            return string.Empty;
        }

        private static Dictionary<string, int> classic = new Dictionary<string, int>
        {
        // EK
        { "Alterac Mountains", 36 },
        { "Arathi Highlands", 45 },
        { "Badlands", 3 },
        { "Blasted Lands", 4 },
        { "Burning Steppes", 46 },
        { "Deadwind Pass", 41 },
        { "Dun Morogh", 1 },
        { "Duskwood", 10 },
        { "Eastern Plaguelands", 139 },
        { "Elwynn Forest", 12 },
        { "Hillsbrad Foothills", 267 },
        { "Ironforge", 1537 },
        { "Loch Modan", 38 },
        { "Redridge Mountains", 44 },
        { "Searing Gorge", 51 },
        { "Silverpine Forest", 130 },
        { "Stormwind City", 1519 },
        { "Stranglethorn Vale", 33 },
        { "Swamp of Sorrows", 8 },
        { "The Hinterlands", 47 },
        { "Tirisfal Glades", 85 },
        { "Undercity", 1497 },
        { "Western Plaguelands", 28 },
        { "Westfall", 40 },
        { "Wetlands", 11 },

        // Kalimdor
        { "Ashenvale", 331 },
        { "Azshara", 16 },
        { "Darkshore", 148 },
        { "Darnassus", 1657 },
        { "Desolace", 405 },
        { "Durotar", 14 },
        { "Dustwallow Marsh", 15 },
        { "Felwood", 361 },
        { "Feralas", 357 },
        { "Moonglade", 493 },
        { "Mulgore", 215 },
        { "Orgrimmar", 1637 },
        { "Silithus", 1377 },
        { "Stonetalon Mountains", 406 },
        { "Tanaris", 440 },
        { "Teldrassil", 141 },
        { "The Barrens", 17 },
        { "Thousand Needles", 400 },
        { "Thunder Bluff", 1638 },
        { "Un'Goro Crater", 490 },
        { "Winterspring", 618 }
    };

        private static Dictionary<string, int> tbc = new Dictionary<string, int>
        {
            { "Azuremyst Isle", 3524 },
            { "Blade's Edge Mountains", 3522 },
            { "Bloodmyst Isle", 3525 },
            { "Eversong Woods", 3430 },
            { "Ghostlands", 3433 },
            { "Hellfire Peninsula", 3483 },
            { "Isle of Quel'Danas", 4080 },
            { "Nagrand", 3518 },
            { "Netherstorm", 3523 },
            { "Terokkar Forest", 3519 },
            { "Shadowmoon Valley", 3520 },
            { "Zangarmarsh", 3521 },
        };

        public static Dictionary<string, int> List = classic.Union(tbc).ToDictionary(k => k.Key, v => v.Value);

        public static bool IsClassic(int id)
        {
            return classic.ContainsValue(id);
        }

        public static bool IsTbc(int id)
        {
            return tbc.ContainsValue(id);
        }
    }
}