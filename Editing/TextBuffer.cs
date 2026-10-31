namespace ATTM.Editing;

/// <summary>
/// 文本缓冲：单个字符串 + 行索引缓存。
/// 语法文件不会很大（几千行），每次编辑重建索引足够快，逻辑也更不容易出错。
/// </summary>
public sealed class TextBuffer
{
    private string _text = string.Empty;
    private readonly List<int> _lineStarts = new() { 0 };
    private readonly List<string> _lines = new() { string.Empty };

    public TextBuffer(string? text = null) => Reset(text ?? string.Empty);

    public string Text => _text;

    public int Length => _text.Length;

    public int LineCount => _lines.Count;

    public int MaxLineLength { get; private set; }

    public void Reset(string text)
    {
        _text = Normalize(text);
        Rebuild();
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    public string GetLine(int index)
        => _lines[Math.Clamp(index, 0, _lines.Count - 1)];

    public int GetLineStart(int index)
        => _lineStarts[Math.Clamp(index, 0, _lineStarts.Count - 1)];

    public int GetLineLength(int index) => GetLine(index).Length;

    public void Replace(int offset, int removeLength, string insertText)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        removeLength = Math.Clamp(removeLength, 0, _text.Length - offset);
        if (removeLength == 0 && insertText.Length == 0) return;

        _text = string.Concat(_text.AsSpan(0, offset), insertText, _text.AsSpan(offset + removeLength));
        Rebuild();
    }

    public string GetText(int offset, int length)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        length = Math.Clamp(length, 0, _text.Length - offset);
        return _text.Substring(offset, length);
    }

    /// <summary>字符偏移换算成 1 起的行列。</summary>
    public (int Line, int Column) GetLineColumn(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);

        var index = 0;
        var low = 0;
        var high = _lineStarts.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_lineStarts[mid] <= offset) { index = mid; low = mid + 1; }
            else high = mid - 1;
        }

        return (index + 1, offset - _lineStarts[index] + 1);
    }

    /// <summary>0 起的行 + 0 起的列换算成字符偏移。</summary>
    public int GetOffset(int line, int column)
    {
        line = Math.Clamp(line, 0, _lines.Count - 1);
        column = Math.Clamp(column, 0, _lines[line].Length);
        return _lineStarts[line] + column;
    }

    private void Rebuild()
    {
        _lineStarts.Clear();
        _lines.Clear();
        _lineStarts.Add(0);

        var start = 0;
        var maxLength = 0;

        for (var i = 0; i < _text.Length; i++)
        {
            if (_text[i] != '\n') continue;

            var line = _text.Substring(start, i - start);
            _lines.Add(line);
            maxLength = Math.Max(maxLength, line.Length);
            start = i + 1;
            _lineStarts.Add(start);
        }

        var last = _text[start..];
        _lines.Add(last);
        MaxLineLength = Math.Max(maxLength, last.Length);
    }
}
