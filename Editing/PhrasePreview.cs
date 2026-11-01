using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ATTM.Core.Syntax;

namespace ATTM.Editing;

/// <summary>
/// 侧边栏里的"乐句预览"：用超大字号显示当前播放到的那一小节（含并行声部的每一行），
/// 换小节时整组文字从行框外滑入并淡入，播放结束后淡出消失。
/// 配色、字体、语法高亮与编辑器完全一致。
/// </summary>
public sealed class PhrasePreview : FrameworkElement
{
    public const double PanelWidth = 460;

    private const double SidePadding = 24;
    private const double HeaderHeight = 38;
    private const double MaxFontSize = 64;
    private const double MinFontSize = 14;
    private const double InDurationMs = 340;
    private const double OutDurationMs = 200;
    private const double LineStaggerMs = 80;
    private const double SlideDistance = 64;

    private readonly List<PhraseLine> _current = new();
    private readonly List<ActiveRange> _active = new();
    private OutgoingPhrase? _outgoing;

    private long _inStartedAt;
    private EditorPalette _palette = EditorPalette.CreateFallback();
    private Brush _background = Brushes.Black;
    private Typeface _typeface;
    private DispatcherTimer? _timer;

    public PhrasePreview()
    {
        _typeface = CreateTypeface();
        ClipToBounds = true;
        SnapsToDevicePixels = true;

        // 固定宽度 + 右对齐：宿主用 0 -> 宽度的裁剪来"从右侧拉出来"，高度自适应
        Width = PanelWidth;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>当前乐句（按行）。</summary>
    public IReadOnlyList<PhraseLine> Lines => _current;

    public int MeasureIndex { get; private set; } = -1;

    /// <summary>总小节数，用于顶部计数显示。</summary>
    public int MeasureCount { get; private set; }

    /// <summary>当前正在发声的音（相对每个乐句行的列区间）。</summary>
    public IReadOnlyList<ActiveRange> ActiveRanges => _active;

    /// <summary>最近一次绘制用的字号（供测试/诊断）。</summary>
    public double LastFontSize { get; private set; }

    /// <summary>最近一次绘制的那一行文字排版宽度（供测试/诊断）。</summary>
    public double LastTextWidth { get; private set; }

    public bool HasContent => _current.Count > 0;

    public bool IsAnimating => _outgoing is not null || RevealProgress < 1;

    /// <summary>最后一行的揭示进度（0 = 还没出现，1 = 完全就位），供测试采样动画。</summary>
    public double RevealProgress
    {
        get
        {
            if (_current.Count == 0) return 0;
            return ProgressOf(_current.Count - 1, Now() - _inStartedAt);
        }
    }

    public string CurrentText => string.Join("\n", _current.Select(line => line.Text));

    public void SetPalette(EditorPalette palette)
    {
        _palette = palette;
        _typeface = CreateTypeface();
        _background = palette.GutterBackground;
        InvalidateVisual();
    }

    /// <summary>显示某一小节的乐句；与当前相同时不重复播放动画。</summary>
    public void Show(int measureIndex, IReadOnlyList<PhraseLine> lines, int measureCount = 0)
    {
        if (measureIndex == MeasureIndex && SameLines(lines)) return;

        if (_current.Count > 0)
            _outgoing = new OutgoingPhrase(new List<PhraseLine>(_current), AlphaOfCurrent(), Now());

        _current.Clear();
        _current.AddRange(lines);
        _active.Clear();
        MeasureIndex = measureIndex;
        MeasureCount = measureCount;
        _inStartedAt = Now();

        StartTimer();
        InvalidateVisual();
    }

    /// <summary>淡出并清空（停止播放或谱面变化时调用）。</summary>
    public void Hide()
    {
        if (_current.Count == 0 && _outgoing is null) return;

        if (_current.Count > 0)
            _outgoing = new OutgoingPhrase(new List<PhraseLine>(_current), AlphaOfCurrent(), Now());

        _current.Clear();
        _active.Clear();
        MeasureIndex = -1;
        StartTimer();
        InvalidateVisual();
    }

    /// <summary>更新"正在发声"的音，实时把当前音符框出来（不触发入场动画）。</summary>
    public void SetActiveSpans(IReadOnlyList<SourceSpan>? spans)
    {
        var next = new List<ActiveRange>();

        if (spans is not null && spans.Count > 0)
        {
            for (var i = 0; i < _current.Count; i++)
            {
                var line = _current[i];
                var lineStart = line.Offset;
                var lineEnd = lineStart + line.Text.Length;

                foreach (var span in spans)
                {
                    var start = Math.Max(span.Start, lineStart);
                    var end = Math.Min(span.Start + span.Length, lineEnd);
                    if (end > start) next.Add(new ActiveRange(i, start - lineStart, end - start));
                }
            }
        }

        if (SameActive(next)) return;

        _active.Clear();
        _active.AddRange(next);
        InvalidateVisual();
    }

    private bool SameActive(List<ActiveRange> next)
    {
        if (next.Count != _active.Count) return false;

        for (var i = 0; i < next.Count; i++)
        {
            if (next[i] != _active[i]) return false;
        }

        return true;
    }

    // ------------------------------------------------------------ 动画时钟

    private static long Now() => Environment.TickCount64;

    private void StartTimer()
    {
        if (_timer is not null) return;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += (_, _) =>
        {
            InvalidateVisual();
            if (IsAnimating) return;

            _timer?.Stop();
            _timer = null;
        };

        _timer.Start();
    }

    private static double ProgressOf(int index, double elapsedMs)
    {
        var local = (elapsedMs - index * LineStaggerMs) / InDurationMs;
        return Ease(Math.Clamp(local, 0.0, 1.0));
    }

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    private double AlphaOfCurrent()
    {
        if (_current.Count == 0) return 0;
        return ProgressOf(_current.Count - 1, Now() - _inStartedAt);
    }

    private bool SameLines(IReadOnlyList<PhraseLine> lines)
    {
        if (lines.Count != _current.Count) return false;

        for (var i = 0; i < lines.Count; i++)
        {
            if (!string.Equals(lines[i].Text, _current[i].Text, StringComparison.Ordinal)) return false;
        }

        return true;
    }

    // ------------------------------------------------------------ 绘制

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1) return;

        // 背景 + 左侧分隔线（与缩略图右边缘借位，视觉上是一条缝）
        dc.DrawRectangle(_background, null, new Rect(0, 0, width, height));
        dc.DrawRectangle(_palette.Border, null, new Rect(0, 0, 1, height));

        DrawHeader(dc, width);

        var body = new Rect(0, HeaderHeight, width, Math.Max(0, height - HeaderHeight));
        if (body.Height < 8) return;

        if (_outgoing is not null)
        {
            var elapsed = Now() - _outgoing.StartedAt;
            var progress = Math.Clamp(elapsed / OutDurationMs, 0.0, 1.0);
            if (progress >= 1) _outgoing = null;
            else DrawPhrase(dc, _outgoing.Lines, body, 1.0 - progress, _outgoing.StartAlpha * (1 - progress), true);
        }

        if (_current.Count == 0)
        {
            if (_outgoing is null) DrawEmptyHint(dc, body);
            return;
        }

        DrawPhrase(dc, _current, body, 1.0, 1.0, false);
    }

    private void DrawHeader(DrawingContext dc, double width)
    {
        var label = new FormattedText(
            "乐句预览",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            UiTypeface(),
            12,
            _palette.GutterText,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(label, new Point(SidePadding - 4, (HeaderHeight - label.Height) / 2));

        if (MeasureIndex >= 0 && MeasureCount > 0)
        {
            var counter = new FormattedText(
                $"第 {MeasureIndex + 1} / {MeasureCount} 小节",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                UiTypeface(),
                12,
                _palette.GutterText,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(counter, new Point(Math.Max(0, width - SidePadding - counter.Width + 4), (HeaderHeight - counter.Height) / 2));
        }
        dc.DrawRectangle(_palette.Border, null, new Rect(SidePadding - 4, HeaderHeight - 1, Math.Max(0, width - SidePadding * 2 + 8), 1));
    }

    private void DrawEmptyHint(DrawingContext dc, Rect body)
    {
        var hint = new FormattedText(
            "播放时在这里显示当前乐句",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            UiTypeface(),
            13,
            _palette.GutterText,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        hint.MaxTextWidth = Math.Max(40, body.Width - SidePadding * 2);
        hint.TextAlignment = TextAlignment.Center;
        dc.DrawText(hint, new Point(0, body.Top + (body.Height - hint.Height) / 2));
    }

    /// <param name="elapsedFactor">0 = 动画起点，1 = 完全就位。</param>
    /// <param name="batchAlpha">整组透明度。</param>
    /// <param name="leaving">true = 正在淡出（滑动方向相反）。</param>
    private void DrawPhrase(
        DrawingContext dc,
        IReadOnlyList<PhraseLine> lines,
        Rect body,
        double elapsedFactor,
        double batchAlpha,
        bool leaving)
    {
        if (lines.Count == 0 || batchAlpha <= 0.001) return;

        var available = Math.Max(60, body.Width - SidePadding * 2);
        var fontSize = FitFontSize(lines, available);
        var lineHeight = fontSize * 1.5;
        var totalHeight = lineHeight * lines.Count;
        var top = body.Top + Math.Max(12, (body.Height - totalHeight) / 2);

        for (var i = 0; i < lines.Count; i++)
        {
            // 逐行错峰：每行比上一行晚 80ms 出发
            var progress = leaving ? elapsedFactor : ProgressOf(i, Now() - _inStartedAt);
            if (progress <= 0.001) continue;

            // 偶数行从左边滑进来，奇数行从右边滑进来；出场沿原路滑回去
            var direction = i % 2 == 0 ? -1.0 : 1.0;
            var dx = direction * SlideDistance * (1 - progress);

            var lineRect = new Rect(0, top + i * lineHeight, body.Width, lineHeight);
            var alpha = leaving ? batchAlpha : batchAlpha * Math.Clamp(progress * 1.4, 0, 1);

            dc.PushOpacity(Math.Clamp(alpha, 0, 1));
            dc.PushClip(new RectangleGeometry(new Rect(2, lineRect.Top, Math.Max(0, body.Width - 4), lineRect.Height)));
            dc.PushTransform(new TranslateTransform(dx, 0));

            DrawLine(dc, lines[i], i, lineRect, fontSize);

            dc.Pop();
            dc.Pop();
            dc.Pop();
        }
    }

    private void DrawLine(DrawingContext dc, PhraseLine line, int lineIndex, Rect lineRect, double fontSize)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = line.Text;
        if (text.Length == 0) return;

        // 整行只排一次，再用 SetForegroundBrush 上色：字距、连字都由 WPF 统一处理，
        // 不会出现"分段各排一次"导致的字符重叠/间距错乱。
        var full = MakeText(text, fontSize, _palette.Foreground, pixelsPerDip);

        foreach (var run in line.Runs)
        {
            var start = Math.Clamp(run.Start, 0, text.Length);
            var length = Math.Clamp(run.Length, 0, text.Length - start);
            if (length <= 0) continue;

            full.SetForegroundBrush(_palette.BrushFor(run.Class), start, length);
        }

        var startX = lineRect.Left + Math.Max(0, (lineRect.Width - full.WidthIncludingTrailingWhitespace) / 2);
        var y = lineRect.Top + (lineRect.Height - full.Height) / 2;

        LastFontSize = fontSize;
        LastTextWidth = full.WidthIncludingTrailingWhitespace;

        DrawActiveMarks(dc, text, lineIndex, startX, lineRect, fontSize, pixelsPerDip);
        dc.DrawText(full, new Point(startX, y));
    }

    /// <summary>把正在发声的音在乐句里框出来（跟随文字一起滑动）。</summary>
    private void DrawActiveMarks(
        DrawingContext dc,
        string text,
        int lineIndex,
        double startX,
        Rect lineRect,
        double fontSize,
        double pixelsPerDip)
    {
        if (_active.Count == 0) return;

        foreach (var range in _active)
        {
            if (range.Line != lineIndex) continue;

            var start = Math.Clamp(range.Start, 0, text.Length);
            var length = Math.Clamp(range.Length, 0, text.Length - start);
            if (length <= 0) continue;

            var prefix = start > 0 ? MakeText(text[..start], fontSize, _palette.Foreground, pixelsPerDip) : null;
            var piece = MakeText(text.Substring(start, length), fontSize, _palette.Foreground, pixelsPerDip);

            var left = startX + (prefix?.WidthIncludingTrailingWhitespace ?? 0);
            var mark = new Rect(left - 2, lineRect.Top + 3, piece.WidthIncludingTrailingWhitespace + 4, lineRect.Height - 6);

            dc.DrawRectangle(_palette.PlayHighlight, null, mark);
            dc.DrawRectangle(_palette.Accent, null, new Rect(mark.Left, mark.Bottom - 2, mark.Width, 2));
        }
    }

    private double FitFontSize(IReadOnlyList<PhraseLine> lines, double available)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var widest = 1.0;

        foreach (var line in lines)
        {
            if (line.Text.Length == 0) continue;
            var text = MakeText(line.Text, MaxFontSize, _palette.Foreground, pixelsPerDip);
            widest = Math.Max(widest, text.WidthIncludingTrailingWhitespace);
        }

        var scale = available / widest;
        return Math.Clamp(MaxFontSize * scale, MinFontSize, MaxFontSize);
    }

    private FormattedText MakeText(string text, double size, Brush brush, double pixelsPerDip) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        _typeface,
        size,
        brush,
        pixelsPerDip);

    private Typeface UiTypeface()
    {
        var family = Application.Current?.TryFindResource("Font.UI") as FontFamily
                     ?? new FontFamily("Microsoft YaHei UI");

        return new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    private static Typeface CreateTypeface()
    {
        var family = Application.Current?.TryFindResource("Font.Editor") as FontFamily
                     ?? new FontFamily("Consolas");

        return new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    private sealed record OutgoingPhrase(IReadOnlyList<PhraseLine> Lines, double StartAlpha, long StartedAt);
}

/// <summary>乐句里的一行：原文 + 该行的语法高亮区间 + 在缓冲区里的绝对起点。</summary>
public sealed record PhraseLine(string Text, IReadOnlyList<HighlightRun> Runs, int Offset);

/// <summary>乐句里正在发声的一段（行号 + 行内列区间）。</summary>
public readonly record struct ActiveRange(int Line, int Start, int Length);
