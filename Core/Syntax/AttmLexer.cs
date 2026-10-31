namespace ATTM.Core.Syntax;

/// <summary>
/// ATTM 的词法分析：把源文本切成 token，供解析器和语法高亮共用。
/// 每个数字字符单独成为一个 token（"11" 是两个音符，不是十一）。
/// </summary>
public static class AttmLexer
{
    public static List<Token> Lex(string text)
    {
        text ??= string.Empty;

        var tokens = new List<Token>(text.Length / 2 + 8);
        var index = 0;
        var line = 1;
        var column = 1;

        while (index < text.Length)
        {
            var ch = text[index];
            var start = index;
            var startLine = line;
            var startColumn = column;

            // 换行
            if (ch is '\r' or '\n')
            {
                if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;

                index++;
                tokens.Add(new Token(TokenKind.NewLine, "\n", startLine, startColumn,
                    SourceSpan.FromBounds(start, index)));

                line++;
                column = 1;
                continue;
            }

            // 空白
            if (ch is ' ' or '\t' or '\u3000')
            {
                while (index < text.Length && text[index] is ' ' or '\t' or '\u3000')
                {
                    index++;
                    column++;
                }

                tokens.Add(new Token(TokenKind.Whitespace, text[start..index], startLine, startColumn,
                    SourceSpan.FromBounds(start, index)));
                continue;
            }

            // '#' 后面紧跟数字是升号；紧跟 A-G 字母时，只有在调式声明里（1 = #C）才算升号，
            // 其余情况一律当注释，保证 "# 注释" 和 "#注释" 都能用
            if (ch == '#')
            {
                if ((index + 1 < text.Length && char.IsAsciiDigit(text[index + 1]))
                    || IsKeySharpMarker(tokens, text, index))
                {
                    index++;
                    column++;
                    tokens.Add(new Token(TokenKind.Sharp, "#", startLine, startColumn,
                        SourceSpan.FromBounds(start, index)));
                }
                else
                {
                    while (index < text.Length && text[index] is not '\r' and not '\n')
                    {
                        index++;
                        column++;
                    }

                    tokens.Add(new Token(TokenKind.Comment, text[start..index], startLine, startColumn,
                        SourceSpan.FromBounds(start, index)));
                }

                continue;
            }

            // 单个数字字符
            if (char.IsAsciiDigit(ch))
            {
                index++;
                column++;
                tokens.Add(new Token(TokenKind.Number, ch.ToString(), startLine, startColumn,
                    SourceSpan.FromBounds(start, index)));
                continue;
            }

            // 单字符符号
            var symbolKind = ch switch
            {
                '.' => TokenKind.Dot,
                '+' => TokenKind.Plus,
                '-' => TokenKind.Minus,
                ',' => TokenKind.Comma,
                '(' => TokenKind.LParen,
                ')' => TokenKind.RParen,
                '/' => TokenKind.Slash,
                '=' => TokenKind.Equals,
                _ => (TokenKind?)null
            };

            if (symbolKind is { } kind)
            {
                index++;
                column++;
                tokens.Add(new Token(kind, ch.ToString(), startLine, startColumn,
                    SourceSpan.FromBounds(start, index)));
                continue;
            }

            // 指令名 / 调式字母
            if (char.IsAsciiLetter(ch) || ch == '_')
            {
                while (index < text.Length && (char.IsAsciiLetter(text[index]) || text[index] == '_'))
                {
                    index++;
                    column++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..index], startLine, startColumn,
                    SourceSpan.FromBounds(start, index)));
                continue;
            }

            // 其它字符（含中文）不算合法语法
            index++;
            column++;
            tokens.Add(new Token(TokenKind.Unknown, ch.ToString(), startLine, startColumn,
                SourceSpan.FromBounds(start, index)));
        }

        return tokens;
    }

    /// <summary>
    /// 判断这个 '#' 是不是调式声明里的升号：
    ///   前置 1 = #C 4/4   -> '#' 前是等号，后面紧跟 A-G
    ///   后置 1 = C# 4/4   -> '#' 前是单个 A-G 字母，字母前是等号
    /// 其它情况（"# 注释"、"1,2 # 说明"）都按注释处理。
    /// </summary>
    private static bool IsKeySharpMarker(List<Token> tokens, string text, int index)
    {
        var previous = LastMeaningful(tokens);
        if (previous < 0) return false;

        if (tokens[previous].Kind == TokenKind.Equals)
        {
            return index + 1 < text.Length
                   && char.IsAsciiLetter(text[index + 1])
                   && char.ToUpperInvariant(text[index + 1]) <= 'G';
        }

        if (tokens[previous].Kind != TokenKind.Identifier) return false;

        var name = tokens[previous].Text;
        if (name.Length != 1) return false;

        var upper = char.ToUpperInvariant(name[0]);
        if (upper is < 'A' or > 'G') return false;

        var beforeName = LastMeaningful(tokens, previous - 1);
        return beforeName >= 0 && tokens[beforeName].Kind == TokenKind.Equals;
    }

    private static int LastMeaningful(List<Token> tokens, int from = -1)
    {
        if (from < 0) from = tokens.Count - 1;

        for (var i = from; i >= 0; i--)
        {
            var kind = tokens[i].Kind;
            if (kind == TokenKind.Whitespace) continue;
            if (kind == TokenKind.NewLine) return -1;
            return i;
        }

        return -1;
    }
}
