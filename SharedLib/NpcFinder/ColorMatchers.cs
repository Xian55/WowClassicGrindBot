using System.Runtime.CompilerServices;

using static SharedLib.NpcFinder.NpcNameColors;

namespace SharedLib.NpcFinder;

#region Simple matchers

public readonly struct SimpleEnemyMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        r > sE_R && g <= sE_G && b <= sE_B;
}

public readonly struct SimpleFriendlyMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        r == sF_R && g > sF_G && b == sF_B;
}

public readonly struct SimpleNeutralMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        r > sN_R && g > sN_G && b == sN_B;
}

public readonly struct SimpleCorpseMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        r == fC_RGB && g == fC_RGB && b == fC_RGB;
}

public readonly struct SimpleNamePlateMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        r is sNamePlate_N or sNamePlate_H_R &&
        g is sNamePlate_N or sNamePlate_H_G &&
        b is sNamePlate_N or sNamePlate_H_B;
}

public readonly struct SimpleEnemyNeutralMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        (r > sE_R && g <= sE_G && b <= sE_B) ||
        (r > sN_R && g > sN_G && b == sN_B);
}

public readonly struct SimpleFriendlyNeutralMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        (r == sF_R && g > sF_G && b == sF_B) ||
        (r > sN_R && g > sN_G && b == sN_B);
}

public readonly struct NoMatchMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) => false;
}

#endregion

#region Fuzzy matchers

file static class FuzzyHelper
{
    public const int colorFuzz = 40;
    public const int colorFuzzSqr = colorFuzz * colorFuzz;
    public const int fuzzCorpseSqr = fuzzCorpse * fuzzCorpse;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool FuzzyMatch(
        byte rr, byte gg, byte bb,
        byte r, byte g, byte b,
        int fuzzSqr)
    {
        unchecked
        {
            int dr = rr - r;
            int dg = gg - g;
            int db = bb - b;
            return (dr * dr) + (dg * dg) + (db * db) <= fuzzSqr;
        }
    }
}

public readonly struct FuzzyEnemyMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        FuzzyHelper.FuzzyMatch(fE_R, fE_G, fE_B, r, g, b, FuzzyHelper.colorFuzzSqr);
}

public readonly struct FuzzyFriendlyMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        FuzzyHelper.FuzzyMatch(fF_R, fF_G, fF_B, r, g, b, FuzzyHelper.colorFuzzSqr);
}

public readonly struct FuzzyNeutralMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        FuzzyHelper.FuzzyMatch(fN_R, fN_G, fN_B, r, g, b, FuzzyHelper.colorFuzzSqr);
}

public readonly struct FuzzyCorpseMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        FuzzyHelper.FuzzyMatch(fC_RGB, fC_RGB, fC_RGB, r, g, b, FuzzyHelper.fuzzCorpseSqr);
}

public readonly struct FuzzyNamePlateMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        FuzzyHelper.FuzzyMatch(sNamePlate_N, sNamePlate_N, sNamePlate_N, r, g, b, FuzzyHelper.fuzzCorpseSqr) ||
        FuzzyHelper.FuzzyMatch(sNamePlate_H_R, sNamePlate_H_G, sNamePlate_H_B, r, g, b, FuzzyHelper.fuzzCorpseSqr);
}

public readonly struct FuzzyEnemyNeutralMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        FuzzyHelper.FuzzyMatch(fE_R, fE_G, fE_B, r, g, b, FuzzyHelper.colorFuzzSqr) ||
        FuzzyHelper.FuzzyMatch(fN_R, fN_G, fN_B, r, g, b, FuzzyHelper.colorFuzzSqr);
}

public readonly struct FuzzyEnemyNeutralNamePlateMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        FuzzyHelper.FuzzyMatch(fE_R, fE_G, fE_B, r, g, b, FuzzyHelper.colorFuzzSqr) ||
        FuzzyHelper.FuzzyMatch(fN_R, fN_G, fN_B, r, g, b, FuzzyHelper.colorFuzzSqr) ||
        FuzzyHelper.FuzzyMatch(sNamePlate_N, sNamePlate_N, sNamePlate_N, r, g, b, FuzzyHelper.fuzzCorpseSqr) ||
        FuzzyHelper.FuzzyMatch(sNamePlate_H_R, sNamePlate_H_G, sNamePlate_H_B, r, g, b, FuzzyHelper.fuzzCorpseSqr);
}

public readonly struct FuzzyFriendlyNeutralMatcher : IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(byte r, byte g, byte b) =>
        FuzzyHelper.FuzzyMatch(fF_R, fF_G, fF_B, r, g, b, FuzzyHelper.colorFuzzSqr) ||
        FuzzyHelper.FuzzyMatch(fN_R, fN_G, fN_B, r, g, b, FuzzyHelper.colorFuzzSqr);
}

#endregion
