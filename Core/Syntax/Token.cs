namespace ATTM.Core.Syntax;

/// <summary>词法单元。Line / Column 从 1 开始。</summary>
public readonly record struct Token(
    TokenKind Kind,
    string Text,
    int Line,
    int Column,
    SourceSpan Span)
{
    public bool IsTrivia => Kind is TokenKind.Whitespace or TokenKind.NewLine or TokenKind.Comment;

    public override string ToString() => $"{Kind} '{Text}' L{Line}:{Column}";
}
