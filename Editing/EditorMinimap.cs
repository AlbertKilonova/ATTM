using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ATTM.Core.Syntax;

namespace ATTM.Editing;

/// <summary>
/// 右侧缩略图：把整篇谱面压成一根根小横条，外面再画一个"当前视野"的方框。
/// 点一下或拖动可以快速跳转。
/// </summary>
public sealed class EditorMinimap : FrameworkElement
{
    /// <summary>行数再多也只画这么多条，保证每帧开销可控。</summary>
    private const int MaxBars = 320;

    private const double MinBarHeight = 1.2;
    private const double HorizontalPadding = 6;

    private TextBuffer? _buffer;
    private EditorPalette _palette = EditorPalette.CreateFallback();
    private HashSet<int> _errorLines = new();

    private double _lineHeight = 25;
    private double _topPadding = 8;
    private double _verticalOffset;
    private double _viewportHeight;
    private int _caretLine = -1;

    private Brush _viewportFill = Brushes.Transparent;
    private Pen _viewportPen = new(Brushes.Gray, 1);
    private Brush _caretBrush = Brushes.OrangeRed;

    private bool _dragging;

    public EditorMinimap()
    {
        Width = 76;
        Focusable = false;
        Cursor = Cursors.Hand;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        IsHitTestVisible = true;
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
        if (!ReferenceEquals(_palette, palette))
        {
            _palette = palette;
            _viewportFill = WithAlpha(palette.Border, 0x3A);
            _viewportPen = new Pen(palette.Border, 1);
            _caretBrush = palette.Caret;
        }

        _buffer = buffer;
        _lineHeight = Math.Max(1, lineHeight);
        _topPadding = topPadding;
        _verticalOffset = Math.Max(0, verticalOffset);
        _viewportHeight = Math.Max(0, viewportHeight);
        _caretLine = caretLine;
        _errorLines = errorLines ?? _errorLines;

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        dc.DrawRectangle(_palette.GutterBackground, null, new Rect(0, 0, width, height));
        dc.DrawLine(new Pen(_palette.Border, 1), new Point(0.5, 0), new Point(0.5, height));

        var buffer = _buffer;
        if (buffer is null || buffer.LineCount == 0) return;

        var contentHeight = _topPadding * 2 + buffer.LineCount * _lineHeight;
        if (contentHeight <= 0) return;

        var scale = height / contentHeight;
        var step = Math.Max(1, (int)Math.Ceiling(buffer.LineCount / (double)MaxBars));
        var barHeight = Math.Max(MinBarHeight, _lineHeight * scale * step - 0.8);
        var usableWidth = Math.Max(4, width - HorizontalPadding * 2 - 2);
        var maxLength = Math.Max(8, buffer.MaxLineLength);

        var viewY = _verticalOffset * scale;
        var viewH = Math.Max(10, _viewportHeight * scale);
        var viewRect = new Rect(1, viewY, width - 2, Math.Min(viewH, Math.Max(0, height - viewY)));
        if (viewRect.Height > 0)
            dc.DrawRectangle(_viewportFill, _viewportPen, viewRect);

        for (var line = 0; line < buffer.LineCount; line += step)
        {
            var text = buffer.GetLine(line).Trim();
            if (text.Length == 0) continue;

            var brush = BrushFor(text, line);
            var barWidth = Math.Max(3.0, usableWidth * Math.Min(1.0, text.Length / (double)maxLength));
            var y = _topPadding * scale + line * _lineHeight * scale;

            dc.DrawRoundedRectangle(brush, null, new Rect(HorizontalPadding, y, barWidth, barHeight), 0.8, 0.8);
        }

        if (_caretLine >= 0 && _caretLine < buffer.LineCount)
        {
            var y = _topPadding * scale + _caretLine * _lineHeight * scale;
            dc.DrawRectangle(_caretBrush, null,
                new Rect(width - 2.5, y, 2.5, Math.Max(2, _lineHeight * scale * step)));
        }
    }

    private Brush BrushFor(string text, int line)
    {
        if (_errorLines.Contains(line)) return _palette.Error;

        if (text[0] == '#') return _palette.Comment;
        if (text.Contains('=', StringComparison.Ordinal)) return _palette.Directive;

        return _palette.Note;
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
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;

        ScrollToPoint(e.GetPosition(this));
        e.Handled = true;
    }

    private void ScrollToPoint(Point point)
    {
        if (_buffer is null || ActualHeight <= 0 || _buffer.LineCount == 0) return;

        var contentHeight = _topPadding * 2 + _buffer.LineCount * _lineHeight;
        var scale = ActualHeight / contentHeight;
        if (scale <= 0) return;

        var contentY = point.Y / scale - _topPadding;
        var target = contentY - _viewportHeight / 2;

        ScrollRequested?.Invoke(Math.Max(0, target));
    }

    private static Brush WithAlpha(Brush brush, byte alpha)
    {
        if (brush is not SolidColorBrush solid) return brush;

        var copy = new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
        copy.Freeze();
        return copy;
    }
}
