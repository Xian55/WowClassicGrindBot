
using System.Collections.Generic;

namespace WowheadDB_Extractor;

internal static class Continents
{
    public static readonly Dictionary<string, int> Map = new()
    {
        ["Eastern Kingdom"] = 0,
        ["Kalimdor"] = 1,
        ["Outland"] = 530,
        ["Northrend"] = 571,
        ["The Lost Isles"] = 648,
        ["Deepholm"] = 646,
    };
}
