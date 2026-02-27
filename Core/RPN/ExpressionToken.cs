namespace Core;

public enum TokenKind
{
    IntLiteral,
    IntVariable,
    BoolVariable,
    Parameterized,

    // Arithmetic
    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    // Comparison
    EqualEqual,
    NotEqual,
    Greater,
    Less,
    GreaterEqual,
    LessEqual,

    // Logical
    AmpAmp,
    PipePipe,
    Bang,

    LeftParen,
    RightParen,

    Eof
}

public readonly struct ExpressionToken
{
    public TokenKind Kind { get; init; }
    public int IntValue { get; init; }
    public string Text { get; init; }

    public ExpressionToken(TokenKind kind, string text = "", int intValue = 0)
    {
        Kind = kind;
        Text = text;
        IntValue = intValue;
    }
}
