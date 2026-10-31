using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATTM.Core.Syntax;

namespace ATTM.Editing;

/// <summary>
/// 编辑器宿主：行号槽 + 可滚动的自绘文本区 + 最右缩略图。
/// 对外暴露文本、光标、撤销、解析结果等接口。
/// </summary>
public sealed class AttmEditor : Grid
{
    private readonly EditorMinimap _minimap = new();
    private readonly EditorTextView _view = new();
    private readonly EditorGutter _gutter = new();
    private readonly ScrollViewer _scroll = new();
    private EditorPalette _palette = EditorPalette.CreateFallback();

    public AttmEditor()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(EditorMinimap.PanelWidth) });

        // 导航交给右侧缩略图，正文区不再显示滚动条
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _scroll.CanContentScroll = false;
        _scroll.Focusable = false;
        _scroll.Content = _view;
        _scroll.ScrollChanged += (_, _) => SyncScroll();

        // 横向移动：Shift + 滚轮（竖向仍然直接滚轮）
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Shift) == 0 || (modifiers & ModifierKeys.Control) != 0) return;

            _scroll.ScrollToHorizontalOffset(Math.Max(0, _scroll.HorizontalOffset - e.Delta / 3.0));
            e.Handled = true;
        };

        Grid.SetColumn(_gutter, 0);
        Grid.SetColumn(_scroll, 1);
        Grid.SetColumn(_minimap, 2);

        _minimap.ScrollRequested = offset => _scroll.ScrollToVerticalOffset(Math.Max(0, offset));

        Children.Add(_minimap);
        Children.Add(_gutter);
        Children.Add(_scroll);

        _view.BufferChanged += (_, _) =>
        {
            UpdateGutter();
            SyncMinimap();
            BufferChanged?.Invoke(this, EventArgs.Empty);
        };

        _view.CaretChanged += (_, _) =>
        {
            UpdateGutter();
            CaretChanged?.Invoke(this, EventArgs.Empty);
        };

        _view.HistoryChanged += (_, _) => HistoryChanged?.Invoke(this, EventArgs.Empty);

        _view.LineMetricsChanged += (_, _) =>
        {
            SyncMetrics();
            SyncMinimap();
        };

        Loaded += (_, _) =>
        {
            ApplyTheme();
            SyncMetrics();
            SyncScroll();
            _view.FocusEditor();
        };
    }

    public event EventHandler? BufferChanged;
    public event EventHandler? CaretChanged;
    public event EventHandler? HistoryChanged;

    public TextBuffer Buffer => _view.Buffer;

    public string Text
    {
        get => _view.Text;
        set => _view.SetText(value);
    }

    public int CaretOffset => _view.CaretOffset;

    public (int Line, int Column) CaretPosition => _view.CaretPosition;

    public bool CanUndo => _view.CanUndo;

    public bool CanRedo => _view.CanRedo;

    public bool HasSelection => _view.SelectionLength > 0;

    public bool HasPlaybackHighlight => _view.HasPlaybackHighlight;

    public double EditorLineHeight => _view.LineHeight;

    /// <summary>播放跟随时把某一行的位置滚进视野。</summary>
    public void ScrollOffsetIntoView(int offset) => _view.ScrollOffsetIntoView(offset);

    public double EditorFontSize
    {
        get => _view.EditorFontSize;
        set
        {
            _view.SetFontSize(value);
            SyncMetrics();
        }
    }

    public void ApplyParse(ParseResult? result)
    {
        _view.SetParse(result);

        _gutter.ErrorLines = result is null
            ? new HashSet<int>()
            : result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Line - 1)
                .ToHashSet();

        _gutter.InvalidateVisual();
        SyncMinimap();
    }

    public void SetPlaybackSpans(IReadOnlyList<SourceSpan>? spans) => _view.SetPlaybackSpans(spans);

    public void SetCaret(int offset) => _view.SetSelection(offset, offset);

    public void InsertText(string text) => _view.InsertText(text);

    public void Undo() => _view.Undo();

    public void Redo() => _view.Redo();

    public void SelectAll() => _view.SelectAll();

    public void FocusEditor() => _view.FocusEditor();

    public void ApplyTheme()
    {
        var palette = EditorPalette.FromResources(this);
        _palette = palette;
        Background = palette.Background;
        _view.SetPalette(palette);
        _gutter.Palette = palette;
        _gutter.InvalidateVisual();
        SyncMinimap();
    }

    private void SyncMetrics()
    {
        _gutter.LineHeight = _view.LineHeight;
        _gutter.TopPadding = _view.TopPadding;
        _gutter.Typeface = _view.EditorTypeface;
        _gutter.FontSize = Math.Max(10, _view.EditorFontSize - 3);
        _gutter.LineCount = _view.Buffer.LineCount;
        _gutter.InvalidateVisual();
    }

    private void SyncScroll()
    {
        _gutter.VerticalOffset = _scroll.VerticalOffset;
        _gutter.InvalidateVisual();
        _view.InvalidateVisual();
        SyncMinimap();

        // 让文本区至少填满视口，点击空白处也能定位到最后一行
        var viewportHeight = _scroll.ViewportHeight;
        if (viewportHeight > 0 && Math.Abs(_view.MinHeight - viewportHeight) > 0.5)
            _view.MinHeight = viewportHeight;
    }

    private void UpdateGutter()
    {
        _gutter.ActiveLine = _view.CaretLine;
        _gutter.LineCount = _view.Buffer.LineCount;
        _gutter.InvalidateVisual();
        SyncMinimap();
    }

    private void SyncMinimap()
    {
        _minimap.Update(
            _view.Buffer,
            _palette,
            _view.LineHeight,
            _view.TopPadding,
            _scroll.VerticalOffset,
            _scroll.ViewportHeight,
            _view.CaretLine,
            _gutter.ErrorLines);
    }
}
