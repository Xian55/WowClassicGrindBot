using System;

namespace Core;

public sealed class Requirement
{
    public const string And = " and ";
    public const string Or = " or ";

    public const string SymbolNegate = "!";
    public const string SymbolAnd = "&&";
    public const string SymbolOr = "||";

    private static bool False() => false;
    private static string Default() => "Unknown requirement";

    public Func<bool> HasRequirement { get; set; } = False;
    public Func<string> LogMessage { get; set; } = Default;
    public bool VisibleIfHasRequirement { get; init; }
}