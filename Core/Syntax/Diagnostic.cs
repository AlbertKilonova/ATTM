namespace ATTM.Core.Syntax;

public enum DiagnosticSeverity
{
    Warning,
    Error
}

/// <summary>一条语法诊断，位置供编辑器跳转与标红使用。</summary>
public sealed record Diagnostic(
    DiagnosticCode Code,
    DiagnosticSeverity Severity,
    string Message,
    int Line,
    int Column,
    SourceSpan Span)
{
    public int CodeNumber => (int)Code;
}
