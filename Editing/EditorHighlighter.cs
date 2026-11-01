using ATTM.Core.Syntax;

namespace ATTM.Editing;

public enum EditorTokenClass
{
    Note,
    Rest,
    Octave,
    OctaveDown,
    Sharp,
    Punctuation,
    Comment,
    Directive,
    Value,
    Unknown
}

public readonly record struct HighlightRun(int Start, int Length, EditorTokenClass Class);

/// <summary>把词法 token 映射成每行的配色区间（供自绘编辑器使用）。</summary>
public static class EditorHighlighter
{
    public static List<HighlightRun>[] Build(ParseResult result)
    {
        var lineCount = result.LineStarts.Count;
        var runs = new List<HighlightRun>[lineCount];
        for (var i = 0; i < lineCount; i++) runs[i] = new List<HighlightRun>(8);

        // 含等号的行按"指令行"配色（注释/内容行里不会出现等号）
        var directiveLines = new bool[lineCount];
        var line = 0;
        foreach (var token in result.Tokens)
        {
            if (token.Kind == TokenKind.NewLine)
            {
                line++;
                continue;
            }

            if (token.Kind == TokenKind.Equals && line < lineCount)
                directiveLines[line] = true;
        }

        line = 0;
        foreach (var token in result.Tokens)
        {
            if (token.Kind == TokenKind.NewLine)
            {
                line++;
                continue;
            }

            if (line >= lineCount) break;

            if (token.Kind == TokenKind.Whitespace) continue;
            if (token.Kind == TokenKind.Comment)
            {
                Add(runs[line], token, result.LineStarts[line], EditorTokenClass.Comment);
                continue;
            }

            Add(runs[line], token, result.LineStarts[line], Classify(token, directiveLines[line]));
        }

        return runs;
    }

    private static void Add(List<HighlightRun> target, Token token, int lineStart, EditorTokenClass tokenClass)
    {
        var start = Math.Max(0, token.Span.Start - lineStart);
        var length = Math.Min(token.Span.Length, Math.Max(0, token.Span.Length));
        if (length <= 0) return;
        target.Add(new HighlightRun(start, length, tokenClass));
    }

    private static EditorTokenClass Classify(Token token, bool directiveLine)
    {
        return token.Kind switch
        {
            TokenKind.Comment => EditorTokenClass.Comment,
            TokenKind.Identifier => directiveLine ? EditorTokenClass.Directive : EditorTokenClass.Unknown,
            TokenKind.Number => directiveLine
                ? EditorTokenClass.Value
                : token.Text == "0" ? EditorTokenClass.Rest : EditorTokenClass.Note,
            TokenKind.Dot => EditorTokenClass.Value,
            TokenKind.Plus => EditorTokenClass.Octave,
            TokenKind.Minus => EditorTokenClass.OctaveDown,
            TokenKind.Sharp => EditorTokenClass.Sharp,
            TokenKind.Comma or TokenKind.LParen or TokenKind.RParen or TokenKind.Slash or TokenKind.Equals
                => EditorTokenClass.Punctuation,
            _ => EditorTokenClass.Unknown
        };
    }
}
