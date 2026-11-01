namespace ATTM.Core.Syntax;

/// <summary>ATTM 词法单元类型。</summary>
public enum TokenKind
{
    Whitespace,
    NewLine,
    Comment,

    Number,
    Dot,
    Plus,
    Minus,
    Sharp,
    Comma,
    LParen,
    RParen,
    Slash,
    Equals,
    Identifier,

    /// <summary>行首的 $（整行升八度）。</summary>
    LineOctaveUp,

    /// <summary>行首的 %（整行降八度）。</summary>
    LineOctaveDown,

    Unknown
}
