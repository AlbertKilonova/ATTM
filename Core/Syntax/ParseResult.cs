namespace ATTM.Core.Syntax;

public sealed class ParseResult
{
    public ParseResult(
        Score? score,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<Token> tokens,
        IReadOnlyList<int> lineStarts,
        IReadOnlyList<int>? measureStartLines = null)
    {
        Score = score;
        Diagnostics = diagnostics;
        Tokens = tokens;
        LineStarts = lineStarts;
        MeasureStartLines = measureStartLines ?? Array.Empty<int>();
    }

    /// <summary>存在 bpm / 调式 / 拍号时才会生成（允许带着其它语法错误生成部分乐谱）。</summary>
    public Score? Score { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public IReadOnlyList<Token> Tokens { get; }

    /// <summary>每一行第一个字符的偏移（0 起），用于偏移与行列互转。</summary>
    public IReadOnlyList<int> LineStarts { get; }

    /// <summary>每个小节第一行的行号（0 起），用于"从光标处播放"的定位。</summary>
    public IReadOnlyList<int> MeasureStartLines { get; }

    /// <summary>把字符偏移换算成小节序号（0 起）。</summary>
    public int GetMeasureIndex(int offset)
    {
        if (MeasureStartLines.Count == 0) return 0;

        var (line, _) = GetLineColumn(offset);
        var lineIndex = line - 1;

        var index = 0;
        for (var i = 0; i < MeasureStartLines.Count; i++)
        {
            if (MeasureStartLines[i] <= lineIndex) index = i;
            else break;
        }

        return index;
    }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    public bool CanPlay => Score is not null && !HasErrors;

    /// <summary>把字符偏移换算成 1 起的行列。</summary>
    public (int Line, int Column) GetLineColumn(int offset)
    {
        if (LineStarts.Count == 0) return (1, 1);

        var index = 0;
        var low = 0;
        var high = LineStarts.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (LineStarts[mid] <= offset) { index = mid; low = mid + 1; }
            else high = mid - 1;
        }

        return (index + 1, offset - LineStarts[index] + 1);
    }
}
