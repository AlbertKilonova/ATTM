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

            // '#' 后面紧跟数字是升号，否则是注释
            if (ch == '#')
            {
                if (index + 1 < text.Length && char.IsAsciiDigit(text[index + 1]))
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
}
