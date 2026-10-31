namespace ATTM.Core.Syntax;

/// <summary>源码区间：起始字符偏移 + 长度。</summary>
public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => Start + Length;

    public static SourceSpan Empty => new(0, 0);

    public static SourceSpan FromBounds(int start, int end)
        => new(start, Math.Max(0, end - start));

    public override string ToString() => $"[{Start}+{Length}]";
}
