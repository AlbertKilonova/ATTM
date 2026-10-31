using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ATTM.Core.Syntax;

namespace ATTM.Editing;

/// <summary>
/// 右侧缩略图：把整篇谱面按"桶"压缩成柔和的横条，再叠一个当前视野的圆角框。
/// 点一下或拖动可以快速跳转。
/// </summary>
public sealed class EditorMinimap : FrameworkElement
{
    /// <summary>面板宽度。</summary>
    public const double PanelWidth = 80;

    /// <summary>每个桶的像素高度：桶之间留空隙，行数再多也不会糊成一片。</summary>
    private const double BucketHeight = 4.0;

    private const double BarInset = 9;
    private const double MinBarWidth = 6;

    private TextBuffer? _buffer;
    private EditorPalette _palette = EditorPalette.CreateFallback();
    private HashSet<int> _errorLines = new();

    private double _lineHeight = 25;
    private double _topPadding = 8;
    private double _verticalOffset;
    private double _viewportHeight;
    private int _caretLine = -1;

    /// <summary>内容层缓存：只有文档、尺寸、配色变化时才重建。</summary>
    private readonly DrawingGroup _content = new();
    private bool _contentDirty = true;
    private int _bufferVersion = -1;
    private double _builtWidth;
    private double _builtHeight;

    private Brush _noteBrush = Brushes.Gray;
    private Brush _commentBrush = Brushes.Gray;
    private Brush _directiveBrush = Brushes.Gray;
    private Brush _errorBrush = Brushes.Red;
    private Brush _viewportFill = Brushes.Transparent;
    private Brush _hoverFill = Brushes.Transparent;
    private Pen _viewportPen = new(Brushes.Gray, 1);
    private Brush _caretBrush = Brushes.OrangeRed;

    private bool _dragging;
    private double _hoverY = -1;

    public EditorMinimap()
    {
        Width = PanelWidth;
        Focusable = false;
        Cursor = Cursors.Hand;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    /// <summary>请求把编辑器滚到某个纵向偏移。</summary>
    public Action<double>? ScrollRequested { get; set; }

    public void Update(
        TextBuffer? buffer,
        EditorPalette palette,
        double lineHeight,
        double topPadding,
        double verticalOffset,
        double viewportHeight,
        int caretLine,
        HashSet<int>? errorLines)
    {
        // TextBuffer 是原地修改的，引用不变不代表内容不变，必须看版本号
        var version = buffer?.Version ?? -1;

        if (!ReferenceEquals(_palette, palette))
        {
            _palette = palette;
            // 低透明度 + 降饱和：只是"淡淡的文字影子"，不抢正文的注意力
            _noteBrush = Soft(palette.Foreground, 0x34, 0.25);
            _commentBrush = Soft(palette.Comment, 0x20, 0.20);
            _directiveBrush = Soft(palette.Directive, 0x3A, 0.35);
            _errorBrush = Soft(palette.Error, 0x7C, 0.25);
            _viewportFill = WithAlpha(palette.Accent, 0x16);
            _hoverFill = WithAlpha(palette.Border, 0x55);
            _viewportPen = new Pen(WithAlpha(palette.Border, 0xB0), 1);
            _caretBrush = palette.Caret;
            _contentDirty = true;
        }

        if (!ReferenceEquals(_buffer, buffer)
            || version != _bufferVersion
            || Math.Abs(_lineHeight - lineHeight) > 0.01
            || Math.Abs(_topPadding - topPadding) > 0.01)
        {
            _contentDirty = true;
        }

        _bufferVersion = version;

        _buffer = buffer;
        _lineHeight = Math.Max(1, lineHeight);
        _topPadding = topPadding;
        _verticalOffset = Math.Max(0, verticalOffset);
        _viewportHeight = Math.Max(0, viewportHeight);
        _caretLine = caretLine;

        if (errorLines is not null && !errorLines.SetEquals(_errorLines))
        {
            _errorLines = errorLines;
            _contentDirty = true;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        dc.DrawRectangle(_palette.GutterBackground, null, new Rect(0, 0, width, height));
        dc.DrawLine(new Pen(_palette.Border, 1), new Point(0.5, 0), new Point(0.5, height));

        if (_buffer is null || _buffer.LineCount == 0) return;

        if (_contentDirty
            || Math.Abs(_builtWidth - width) > 0.5
            || Math.Abs(_builtHeight - height) > 0.5)
        {
            RebuildContent(width, height);
            _contentDirty = false;
            _builtWidth = width;
            _builtHeight = height;
        }

        dc.DrawDrawing(_content);

        // 悬停提示条
        if (_hoverY >= 0 && !_dragging)
            dc.DrawRectangle(_hoverFill, null, new Rect(0, _hoverY - 1, width, 2));

        // 当前视野
        var scale = ScaleFor(height);
        if (_viewportHeight > 0 && scale > 0)
        {
            var viewY = _verticalOffset * scale;
            var viewH = Math.Max(12, _viewportHeight * scale);
            viewH = Math.Min(viewH, Math.Max(0, height - viewY));

            if (viewH > 0)
            {
                var rect = new Rect(1.5, viewY, width - 3, viewH);
                dc.DrawRectangle(_viewportFill, _viewportPen, rect);
            }
        }

        // 光标行：右侧一小段主题色
        if (_caretLine >= 0 && _caretLine < _buffer.LineCount && scale > 0)
        {
            var y = _topPadding * scale + _caretLine * _lineHeight * scale;
            var pillHeight = Math.Max(3, _lineHeight * scale * 0.72);
            dc.DrawRectangle(_caretBrush, null, new Rect(width - 3, y + 0.5, 2, pillHeight));
        }
    }

    /// <summary>把整篇文档压成若干个桶，每个桶画一条柔和的横条，空白行留空。</summary>
    private void RebuildContent(double width, double height)
    {
        using var dc = _content.Open();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var buffer = _buffer;
        if (buffer is null || buffer.LineCount == 0) return;

        var bucketCount = Math.Max(1, (int)Math.Floor(height / BucketHeight));
        var linesPerBucket = Math.Max(1, (int)Math.Ceiling(buffer.LineCount / (double)bucketCount));
        var usableWidth = Math.Max(MinBarWidth, width - BarInset - 9);
        var maxLength = Math.Max(8, buffer.MaxLineLength);

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var start = bucket * linesPerBucket;
            if (start >= buffer.LineCount) break;

            var end = Math.Min(buffer.LineCount, start + linesPerBucket);

            var nonEmpty = 0;
            var totalLength = 0;
            var hasError = false;
            var hasDirective = false;
            var commentOnly = true;

            for (var line = start; line < end; line++)
            {
                var text = buffer.GetLine(line).Trim();
                if (text.Length == 0) continue;

                nonEmpty++;
                totalLength += text.Length;

                if (_errorLines.Contains(line)) hasError = true;
                if (text[0] != '#') commentOnly = false;
                if (text.Contains('=', StringComparison.Ordinal)) hasDirective = true;
            }

            if (nonEmpty == 0) continue;

            var brush = hasError ? _errorBrush
                : hasDirective ? _directiveBrush
                : commentOnly ? _commentBrush
                : _noteBrush;

            var averageLength = totalLength / (double)nonEmpty;
            var barWidth = Math.Clamp(usableWidth * (averageLength / maxLength), MinBarWidth, usableWidth);

            var y = bucket * BucketHeight;
            var barHeight = Math.Max(1.4, BucketHeight - 2.2);

            dc.DrawRectangle(brush, null, new Rect(BarInset, y, barWidth, barHeight));
        }
    }

    private double ScaleFor(double height)
    {
        if (_buffer is null || _buffer.LineCount == 0) return 0;

        var contentHeight = _topPadding * 2 + _buffer.LineCount * _lineHeight;
        return contentHeight <= 0 ? 0 : height / contentHeight;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        ScrollToPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        _hoverY = e.GetPosition(this).Y;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var y = e.GetPosition(this).Y;

        if (_dragging)
        {
            ScrollToPoint(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (Math.Abs(y - _hoverY) > 0.5)
        {
            _hoverY = y;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverY < 0 && !_dragging) return;

        _hoverY = -1;
        InvalidateVisual();
    }

    private void ScrollToPoint(Point point)
    {
        if (_buffer is null || ActualHeight <= 0 || _buffer.LineCount == 0) return;

        var scale = ScaleFor(ActualHeight);
        if (scale <= 0) return;

        var contentY = point.Y / scale - _topPadding;
        var target = contentY - _viewportHeight / 2;

        ScrollRequested?.Invoke(Math.Max(0, target));
    }

    /// <summary>降低饱和度并压低透明度，得到柔和的横条颜色。</summary>
    private static Brush Soft(Brush source, byte alpha, double desaturate)
    {
        if (source is not SolidColorBrush solid) return source;

        var color = solid.Color;
        var gray = (byte)(color.R * 0.299 + color.G * 0.587 + color.B * 0.114);

        var r = (byte)(color.R + (gray - color.R) * desaturate);
        var g = (byte)(color.G + (gray - color.G) * desaturate);
        var b = (byte)(color.B + (gray - color.B) * desaturate);

        var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush WithAlpha(Brush brush, byte alpha)
    {
        if (brush is not SolidColorBrush solid) return brush;

        var copy = new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
        copy.Freeze();
        return copy;
    }
}
