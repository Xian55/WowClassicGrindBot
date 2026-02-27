using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Core;

public ref struct ExpressionTokenizer
{
    private readonly ReadOnlySpan<char> source;
    private readonly Dictionary<string, Func<int>> intVariables;
    private readonly FrozenDictionary<string, Func<bool>> boolVariables;
    private readonly string[] sortedVariableNames;
    private readonly string[] parameterizedPrefixes;

    private int pos;
    private bool hasPeeked;
    private ExpressionToken peeked;

    public ExpressionTokenizer(
        ReadOnlySpan<char> source,
        Dictionary<string, Func<int>> intVariables,
        FrozenDictionary<string, Func<bool>> boolVariables,
        string[] sortedVariableNames,
        string[] parameterizedPrefixes)
    {
        this.source = source;
        this.intVariables = intVariables;
        this.boolVariables = boolVariables;
        this.sortedVariableNames = sortedVariableNames;
        this.parameterizedPrefixes = parameterizedPrefixes;
        pos = 0;
        hasPeeked = false;
        peeked = default;
    }

    public ExpressionToken Peek()
    {
        if (!hasPeeked)
        {
            peeked = ScanNext();
            hasPeeked = true;
        }
        return peeked;
    }

    public ExpressionToken Next()
    {
        if (hasPeeked)
        {
            hasPeeked = false;
            return peeked;
        }
        return ScanNext();
    }

    private ExpressionToken ScanNext()
    {
        // 1. Skip whitespace
        while (pos < source.Length && source[pos] == ' ')
            pos++;

        // 2. EOF
        if (pos >= source.Length)
            return new ExpressionToken(TokenKind.Eof);

        // 3. Multi-char operators: ==, !=, >=, <=, &&, ||
        if (pos + 1 < source.Length)
        {
            char c0 = source[pos];
            char c1 = source[pos + 1];

            if (c0 == '=' && c1 == '=') { pos += 2; return new ExpressionToken(TokenKind.EqualEqual, "=="); }
            if (c0 == '!' && c1 == '=') { pos += 2; return new ExpressionToken(TokenKind.NotEqual, "!="); }
            if (c0 == '>' && c1 == '=') { pos += 2; return new ExpressionToken(TokenKind.GreaterEqual, ">="); }
            if (c0 == '<' && c1 == '=') { pos += 2; return new ExpressionToken(TokenKind.LessEqual, "<="); }
            if (c0 == '&' && c1 == '&') { pos += 2; return new ExpressionToken(TokenKind.AmpAmp, "&&"); }
            if (c0 == '|' && c1 == '|') { pos += 2; return new ExpressionToken(TokenKind.PipePipe, "||"); }
        }

        // 4. Parentheses
        if (source[pos] == '(') { pos++; return new ExpressionToken(TokenKind.LeftParen, "("); }
        if (source[pos] == ')') { pos++; return new ExpressionToken(TokenKind.RightParen, ")"); }

        // 5. Parameterized requirement prefixes
        ReadOnlySpan<char> remaining = source[pos..];
        foreach (string prefix in parameterizedPrefixes)
        {
            if (remaining.StartsWith(prefix, StringComparison.Ordinal))
            {
                // Consume everything until we hit a logical operator, paren, or end
                int start = pos;
                pos += prefix.Length;

                // For parameterized tokens, consume until we hit &&, ||, ), or end
                while (pos < source.Length)
                {
                    if (pos + 1 < source.Length)
                    {
                        char c0 = source[pos];
                        char c1 = source[pos + 1];
                        if ((c0 == '&' && c1 == '&') || (c0 == '|' && c1 == '|'))
                            break;
                    }
                    if (source[pos] == ')')
                        break;

                    pos++;
                }

                string text = source[start..pos].Trim().ToString();
                return new ExpressionToken(TokenKind.Parameterized, text);
            }
        }

        // 7. Known variable names (sorted by length desc, greedy match)
        remaining = source[pos..];
        foreach (string varName in sortedVariableNames)
        {
            if (remaining.Length < varName.Length)
                continue;

            if (!remaining.StartsWith(varName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Verify next char is a delimiter (operator, space, paren, end)
            if (remaining.Length > varName.Length)
            {
                char next = remaining[varName.Length];
                if (!IsDelimiter(next, remaining, varName.Length))
                    continue;
            }

            pos += varName.Length;
            string text = varName;

            if (intVariables.ContainsKey(text))
                return new ExpressionToken(TokenKind.IntVariable, text);

            if (boolVariables.ContainsKey(text))
                return new ExpressionToken(TokenKind.BoolVariable, text);

            // Shouldn't happen, but fallback
            return new ExpressionToken(TokenKind.BoolVariable, text);
        }

        // 8. Integer literals
        if (char.IsAsciiDigit(source[pos]))
        {
            int start = pos;
            while (pos < source.Length && char.IsAsciiDigit(source[pos]))
                pos++;

            int value = int.Parse(source[start..pos]);
            return new ExpressionToken(TokenKind.IntLiteral, source[start..pos].ToString(), value);
        }

        // 9. Single-char operators
        char ch = source[pos];
        pos++;
        return ch switch
        {
            '+' => new ExpressionToken(TokenKind.Plus, "+"),
            '-' => new ExpressionToken(TokenKind.Minus, "-"),
            '*' => new ExpressionToken(TokenKind.Star, "*"),
            '/' => new ExpressionToken(TokenKind.Slash, "/"),
            '%' => new ExpressionToken(TokenKind.Percent, "%"),
            '>' => new ExpressionToken(TokenKind.Greater, ">"),
            '<' => new ExpressionToken(TokenKind.Less, "<"),
            '!' => new ExpressionToken(TokenKind.Bang, "!"),
            _ => throw new InvalidOperationException(
                $"Unexpected character '{ch}' at position {pos - 1} in expression: {source.ToString()}")
        };
    }

    private static bool IsDelimiter(char c, ReadOnlySpan<char> remaining, int offset)
    {
        // Space, parens, or end-of-input are always delimiters
        if (c is ' ' or '(' or ')')
            return true;

        // Multi-char operators starting at offset
        if (offset + 1 < remaining.Length)
        {
            char c1 = remaining[offset + 1];
            if ((c == '&' && c1 == '&') ||
                (c == '|' && c1 == '|') ||
                (c == '=' && c1 == '=') ||
                (c == '!' && c1 == '=') ||
                (c == '>' && c1 == '=') ||
                (c == '<' && c1 == '='))
                return true;
        }

        // Single-char operators
        return c is '+' or '-' or '*' or '/' or '%' or '>' or '<' or '!' or '=';
    }
}
