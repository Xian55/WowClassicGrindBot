using Newtonsoft.Json;

using System;

namespace Core;

[Flags]
[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum SchoolMask
{
    None = 0,
    Physical = 1,
    Holy = 2,
    Fire = 4,
    Nature = 8,
    Frost = 16,
    Shadow = 32,
    Arcane = 64,
}

public static class SchoolMask_Extension
{
    public static bool HasValue(this SchoolMask value, SchoolMask flag)
    {
        return (value & flag) != 0;
    }
}