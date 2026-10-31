using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ATTM.Editing;

/// <summary>行号槽：只画可见行，和文本区共享行高与滚动偏移。</summary>
public sealed class EditorGutter : FrameworkElement
{
    public double LineHeight { get; set; } = 24;
    public double TopPadding { get; set; } = 8;
    public double VerticalOffset { get; set; }
    public int LineCount { get; set; }
    public int ActiveLine { get; set; } = -1;
    public HashSet<int> ErrorLines { get; set; } = new();
    public EditorPalette Palette { get; set; } = EditorPalette.CreateFallback();
    public Typeface Typeface { get; set; } = new("Consolas");
    public double FontSize { get; set; } = 12;
    public double GutterWidth { get; set; } = 54;

    public EditorGutter()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize) => new(GutterWidth, 0);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Palette.GutterBackground, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var borderPen = new Pen(Palette.Border, 1);
        dc.DrawLine(borderPen, new Point(ActualWidth - 0.5, 0), new Point(ActualWidth - 0.5, ActualHeight));

        if (LineCount <= 0) return;

        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var first = Math.Max(0, (int)Math.Floor((VerticalOffset - TopPadding) / LineHeight));
        var last = Math.Min(LineCount - 1,
            (int)Math.Floor((VerticalOffset + ActualHeight - TopPadding) / LineHeight) + 1);

        for (var i = first; i <= last; i++)
        {
            var isActive = i == ActiveLine;
            var brush = isActive ? Palette.GutterTextActive : Palette.GutterText;

            var number = new FormattedText(
                (i + 1).ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Typeface,
                FontSize,
                brush,
                ppd);

            var x = ActualWidth - 12 - number.WidthIncludingTrailingWhitespace;
            var y = TopPadding + i * LineHeight - VerticalOffset + (LineHeight - number.Height) / 2;

            dc.DrawText(number, new Point(x, y));

            if (ErrorLines.Contains(i))
            {
                dc.DrawEllipse(Palette.Error, null,
                    new Point(11, y + number.Height / 2), 2.6, 2.6);
            }
        }
    }
}
