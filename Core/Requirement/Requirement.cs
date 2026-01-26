using System;

namespace Core;

public static class RequirementExt
{
    public static void Or(this Requirement f1, Requirement f2)
    {
        Func<bool> HasRequirement = f1.HasRequirement;
        Func<string> LogMessage = f1.LogMessage;

        f1.HasRequirement = CombinedReq;
        f1.LogMessage = Message;

        bool CombinedReq() =>
            HasRequirement() || f2.HasRequirement();

        string Message() =>
            string.Join(Requirement.Or,
            LogMessage(), f2.LogMessage());
    }

    public static void And(this Requirement f1, Requirement f2)
    {
        Func<bool> HasRequirement = f1.HasRequirement;
        Func<string> LogMessage = f1.LogMessage;

        f1.HasRequirement = CombinedReq;
        f1.LogMessage = Message;

        bool CombinedReq()
            => HasRequirement() && f2.HasRequirement();

        string Message()
            => string.Join(Requirement.And,
            LogMessage(), f2.LogMessage());
    }

    public static void Negate(this Requirement f, ReadOnlySpan<char> keyword)
    {
        Func<bool> HasRequirement = f.HasRequirement;
        Func<string> LogMessage = f.LogMessage;

        string keywordStr = keyword.ToString();

        f.HasRequirement = Negated;
        f.LogMessage = Message;

        bool Negated() => !HasRequirement();
        string Message() => $"{keywordStr}{LogMessage()}";
    }
}

public sealed class Requirement
{
    public const string And = " and ";
    public const string Or = " or ";

    public const string SymbolNegate = "!";
    public const string SymbolAnd = "&&";
    public const string SymbolOr = "||";

    public const char SymbolAndChar = '&';
    public const char SymbolOrChar = '|';

    private static bool False() => false;
    private static string Default() => "Unknown requirement";

    public Func<bool> HasRequirement { get; set; } = False;
    public Func<string> LogMessage { get; set; } = Default;
    public bool VisibleIfHasRequirement { get; init; }
}