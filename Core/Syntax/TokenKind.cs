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
    Unknown
}
