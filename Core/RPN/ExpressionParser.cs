using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace Core;

public sealed class ExpressionParser
{
    private readonly Dictionary<string, Func<int>> intVariables;
    private readonly FrozenDictionary<string, Func<bool>> boolVariables;
    private readonly FrozenDictionary<string, Func<ReadOnlySpan<char>, Requirement>> requirementMap;
    private readonly ILogger logger;

    private readonly string[] parameterizedPrefixes;

    public ExpressionParser(
        Dictionary<string, Func<int>> intVariables,
        FrozenDictionary<string, Func<bool>> boolVariables,
        FrozenDictionary<string, Func<ReadOnlySpan<char>, Requirement>> requirementMap,
        ILogger logger)
    {
        this.intVariables = intVariables;
        this.boolVariables = boolVariables;
        this.requirementMap = requirementMap;
        this.logger = logger;

        // Build parameterized prefixes from requirementMap keys
        parameterizedPrefixes = [.. requirementMap.Keys.OrderByDescending(static k => k.Length)];
    }

    public Requirement Parse(ReadOnlySpan<char> expression)
    {
        // Build sorted variable names fresh each call so late-bound
        // entries (CD_*, Cost_*) added after construction are visible.
        string[] sortedVariableNames = intVariables.Keys
            .Concat(boolVariables.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static k => k.Length)
            .ToArray();

        ExpressionTokenizer tokenizer = new(
            expression, intVariables, boolVariables,
            sortedVariableNames, parameterizedPrefixes);

        ExprValue result = ParseExpression(ref tokenizer, 0);

        ExpressionToken eof = tokenizer.Next();
        if (eof.Kind != TokenKind.Eof)
        {
            throw new InvalidOperationException(
                $"Unexpected token '{eof.Text}' after expression: {expression.ToString()}");
        }

        // If top-level result is int, wrap as expr == 0 for backward compat
        if (result.IsInt)
        {
            Func<int> intFunc = result.IntFunc!;
            Func<string> logFunc = result.LogFunc;

            bool hasReq() => intFunc() == 0;
            string logMsg() => $"{logFunc()} == 0";

            return new Requirement
            {
                HasRequirement = hasReq,
                LogMessage = logMsg
            };
        }

        return new Requirement
        {
            HasRequirement = result.BoolFunc!,
            LogMessage = result.LogFunc
        };
    }

    private ExprValue ParseExpression(ref ExpressionTokenizer tokenizer, int minBp)
    {
        ExprValue left = Nud(ref tokenizer);

        while (true)
        {
            ExpressionToken op = tokenizer.Peek();

            int lbp = LeftBindingPower(op.Kind);
            if (lbp < minBp)
                break;

            tokenizer.Next(); // consume operator
            left = Led(ref tokenizer, left, op, lbp);
        }

        return left;
    }

    private ExprValue Nud(ref ExpressionTokenizer tokenizer)
    {
        ExpressionToken token = tokenizer.Next();

        switch (token.Kind)
        {
            case TokenKind.IntLiteral:
            {
                int val = token.IntValue;
                string text = token.Text;
                return ExprValue.Int(() => val, () => text);
            }

            case TokenKind.IntVariable:
            {
                Func<int> func = intVariables[token.Text];
                string name = token.Text;
                return ExprValue.Int(func, () => $"{name} {func()}");
            }

            case TokenKind.BoolVariable:
            {
                Func<bool> func = boolVariables[token.Text];
                string name = token.Text;
                return ExprValue.Bool(func, () => name);
            }

            case TokenKind.Parameterized:
            {
                return ParseParameterized(token.Text);
            }

            case TokenKind.Bang:
            {
                string prefix = token.Text;
                ExprValue operand = ParseExpression(ref tokenizer, PrefixBindingPower());

                if (operand.IsBool)
                {
                    Func<bool> inner = operand.BoolFunc!;
                    Func<string> log = operand.LogFunc;
                    return ExprValue.Bool(() => !inner(), () => $"{prefix}{log()}");
                }

                throw new InvalidOperationException(
                    $"Cannot negate non-bool operand: {prefix}{operand.LogFunc()}");
            }

            case TokenKind.Minus:
            {
                // Unary minus - check if next token is an int expression
                ExprValue operand = ParseExpression(ref tokenizer, PrefixBindingPower());

                if (operand.IsInt)
                {
                    Func<int> inner = operand.IntFunc!;
                    Func<string> log = operand.LogFunc;
                    return ExprValue.Int(() => -inner(), () => $"-{log()}");
                }

                throw new InvalidOperationException(
                    $"Cannot negate non-int operand: -{operand.LogFunc()}");
            }

            case TokenKind.LeftParen:
            {
                ExprValue inner = ParseExpression(ref tokenizer, 0);
                ExpressionToken close = tokenizer.Next();
                if (close.Kind != TokenKind.RightParen)
                {
                    throw new InvalidOperationException(
                        $"Expected ')' but got '{close.Text}'");
                }
                return inner;
            }

            default:
                throw new InvalidOperationException(
                    $"Unexpected token '{token.Text}' (kind: {token.Kind}) at start of expression");
        }
    }

    private ExprValue Led(ref ExpressionTokenizer tokenizer, ExprValue left, ExpressionToken op, int lbp)
    {
        // Right-associativity for logical ops would use lbp, left-assoc uses lbp+1
        int nextMinBp = lbp + 1;

        ExprValue right = ParseExpression(ref tokenizer, nextMinBp);

        switch (op.Kind)
        {
            // Arithmetic: int, int -> int
            case TokenKind.Plus:
                return IntBinOp(left, right, op.Text,
                    static (a, b) => a + b);
            case TokenKind.Minus:
                return IntBinOp(left, right, op.Text,
                    static (a, b) => a - b);
            case TokenKind.Star:
                return IntBinOp(left, right, op.Text,
                    static (a, b) => a * b);
            case TokenKind.Slash:
                return IntBinOp(left, right, op.Text,
                    static (a, b) => b == 0 ? 0 : a / b);
            case TokenKind.Percent:
                return IntBinOp(left, right, op.Text,
                    static (a, b) => b == 0 ? 0 : a % b);

            // Comparison: int, int -> bool
            case TokenKind.EqualEqual:
                return CmpOp(left, right, op.Text,
                    static (a, b) => a == b);
            case TokenKind.NotEqual:
                return CmpOp(left, right, op.Text,
                    static (a, b) => a != b);
            case TokenKind.Greater:
                return CmpOp(left, right, op.Text,
                    static (a, b) => a > b);
            case TokenKind.Less:
                return CmpOp(left, right, op.Text,
                    static (a, b) => a < b);
            case TokenKind.GreaterEqual:
                return CmpOp(left, right, op.Text,
                    static (a, b) => a >= b);
            case TokenKind.LessEqual:
                return CmpOp(left, right, op.Text,
                    static (a, b) => a <= b);

            // Logical: bool, bool -> bool
            case TokenKind.AmpAmp:
                return LogicalOp(left, right, Requirement.And,
                    static (a, b) => a() && b());
            case TokenKind.PipePipe:
                return LogicalOp(left, right, Requirement.Or,
                    static (a, b) => a() || b());

            default:
                throw new InvalidOperationException(
                    $"Unknown infix operator: {op.Text}");
        }
    }

    private static ExprValue IntBinOp(ExprValue left, ExprValue right, string opText,
        Func<int, int, int> op)
    {
        if (!left.IsInt || !right.IsInt)
            throw new InvalidOperationException(
                $"Arithmetic operator '{opText}' requires int operands, got: {left.LogFunc()} {opText} {right.LogFunc()}");

        Func<int> lf = left.IntFunc!;
        Func<int> rf = right.IntFunc!;
        Func<string> ll = left.LogFunc;
        Func<string> rl = right.LogFunc;

        return ExprValue.Int(
            () => op(lf(), rf()),
            () => $"{ll()} {opText} {rl()}");
    }

    private static ExprValue CmpOp(ExprValue left, ExprValue right, string opText,
        Func<int, int, bool> op)
    {
        if (!left.IsInt || !right.IsInt)
            throw new InvalidOperationException(
                $"Comparison operator '{opText}' requires int operands, got: {left.LogFunc()} {opText} {right.LogFunc()}");

        Func<int> lf = left.IntFunc!;
        Func<int> rf = right.IntFunc!;
        Func<string> ll = left.LogFunc;
        Func<string> rl = right.LogFunc;

        return ExprValue.Bool(
            () => op(lf(), rf()),
            () => $"{ll()} {opText} {rl()}");
    }

    private static ExprValue LogicalOp(ExprValue left, ExprValue right, string joinText,
        Func<Func<bool>, Func<bool>, bool> op)
    {
        // If either side is int, implicitly wrap as expr == 0
        Func<bool> lBool = left.IsBool
            ? left.BoolFunc!
            : WrapIntAsBool(left);

        Func<bool> rBool = right.IsBool
            ? right.BoolFunc!
            : WrapIntAsBool(right);

        Func<string> ll = left.IsBool ? left.LogFunc : WrapIntLog(left);
        Func<string> rl = right.IsBool ? right.LogFunc : WrapIntLog(right);

        return ExprValue.Bool(
            () => op(lBool, rBool),
            () => string.Join(joinText, ll(), rl()));
    }

    private static Func<bool> WrapIntAsBool(ExprValue expr)
    {
        Func<int> f = expr.IntFunc!;
        return () => f() == 0;
    }

    private static Func<string> WrapIntLog(ExprValue expr)
    {
        Func<string> log = expr.LogFunc;
        return () => $"{log()} == 0";
    }

    private ExprValue ParseParameterized(string text)
    {
        // Find which requirementMap key matches
        foreach (var kvp in requirementMap)
        {
            if (text.Contains(kvp.Key, StringComparison.Ordinal))
            {
                Requirement req = kvp.Value(text);
                return ExprValue.Bool(req.HasRequirement, req.LogMessage);
            }
        }

        // Shouldn't happen - tokenizer only emits Parameterized for known prefixes
        throw new InvalidOperationException(
            $"No handler for parameterized requirement: {text}");
    }

    private static int LeftBindingPower(TokenKind kind) => kind switch
    {
        TokenKind.PipePipe => 1,
        TokenKind.AmpAmp => 2,
        TokenKind.EqualEqual or TokenKind.NotEqual => 3,
        TokenKind.Greater or TokenKind.Less or
        TokenKind.GreaterEqual or TokenKind.LessEqual => 4,
        TokenKind.Plus or TokenKind.Minus => 5,
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 6,
        _ => -1 // Not an infix operator -> stop parsing
    };

    private static int PrefixBindingPower() => 7;
}

internal readonly struct ExprValue
{
    public Func<int>? IntFunc { get; }
    public Func<bool>? BoolFunc { get; }
    public Func<string> LogFunc { get; }

    public bool IsInt => IntFunc is not null;
    public bool IsBool => BoolFunc is not null;

    private ExprValue(Func<int>? intFunc, Func<bool>? boolFunc, Func<string> logFunc)
    {
        IntFunc = intFunc;
        BoolFunc = boolFunc;
        LogFunc = logFunc;
    }

    public static ExprValue Int(Func<int> intFunc, Func<string> logFunc)
        => new(intFunc, null, logFunc);

    public static ExprValue Bool(Func<bool> boolFunc, Func<string> logFunc)
        => new(null, boolFunc, logFunc);
}
