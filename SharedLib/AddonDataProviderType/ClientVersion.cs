namespace SharedLib;

public enum ClientVersion
{
    None = 0,
    Retail = 1,
    SoM = 2,
    TBC = 5,
    Wrath = 11,
    Cata = 14,
    Mop = 19,
    //
    Legacy_Vanilla = 91,
    Legacy_TBC = 92,
    Legacy_Wrath = 93,
    Legacy_Cata = 94,
    Legacy_Mop = 95,
}

public static class ClientVersion_Extension
{
    public static string ToStringF(this ClientVersion value) => value switch
    {
        ClientVersion.None => nameof(ClientVersion.None),
        ClientVersion.Retail => nameof(ClientVersion.Retail),
        ClientVersion.SoM => nameof(ClientVersion.SoM),
        ClientVersion.TBC => nameof(ClientVersion.TBC),
        ClientVersion.Wrath => nameof(ClientVersion.Wrath),
        ClientVersion.Cata => nameof(ClientVersion.Cata),
        ClientVersion.Mop => nameof(ClientVersion.Mop),
        ClientVersion.Legacy_Vanilla => nameof(ClientVersion.Legacy_Vanilla),
        ClientVersion.Legacy_TBC => nameof(ClientVersion.Legacy_TBC),
        ClientVersion.Legacy_Wrath => nameof(ClientVersion.Legacy_Wrath),
        ClientVersion.Legacy_Cata => nameof(ClientVersion.Legacy_Cata),
        ClientVersion.Legacy_Mop => nameof(ClientVersion.Legacy_Mop),
        _ => nameof(ClientVersion.None)
    };
}
