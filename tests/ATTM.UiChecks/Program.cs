using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATTM.Editing;
using ATTM.Services;
using ATTM.Views;

namespace ATTM.UiChecks;

/// <summary>
/// 进程内 UI 检查（本机截图不可靠，按交接文档走 VisualTree 采样 + 进程内渲染）。
/// 验证：主题资源、字体字形、编辑器输入 / 高亮 / 撤销 / 报错 / 绘制。
/// 退出码 0 = 全绿。
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var app = new App();
        app.InitializeComponent();

        var originalTheme = AppConfig.Load().Theme;

        var window = new MainWindow();
        window.Show();
        Pump(700);

        // 不受用户本地配置影响，测试固定从深色主题开始
        ThemeService.SetTheme(true);
        Pump(150);

        Check("窗口已渲染", () => window.ActualWidth > 400 && window.ActualHeight > 300,
            $"窗口 {window.ActualWidth:F0}x{window.ActualHeight:F0}");

        var background = (window.Background as SolidColorBrush)?.Color.ToString() ?? "(null)";
        Check("深色调色板已应用", () => background == "#FF141417", $"背景 {background}");

        var iconTexts = Descendants(window)
            .OfType<TextBlock>()
            .Where(t => t.FontFamily.Source.Contains("Material Symbols", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Check("图标字形已渲染", () => iconTexts.Count >= 5 && iconTexts.All(t => t.ActualWidth > 10),
            $"图标 TextBlock {iconTexts.Count} 个：" +
            string.Join(", ", iconTexts.Take(6).Select(t => $"{Describe(t.Text)} {t.ActualWidth:F0}x{t.ActualHeight:F0}")));

        Check("Lekton 字形真实存在（不只是名字）",
            () => GlyphExists("Font.Editor", 'M') && InkPixels("Font.Editor", "MMM", 20) > 30);

        Check("Material Symbols 图标字形真实存在",
            () => GlyphExists("Font.Icon", 0xE037)
                  && GlyphExists("Font.Icon", 0xE047)
                  && GlyphExists("Font.Icon", 0xE8B8)
                  && InkPixels("Font.Icon", "\uE037", 24) > 30);

        // ---------------------------------------------------------- 编辑器交互

        var editor = window.FindName("Editor") as AttmEditor;
        Check("编辑器控件已挂载", () => editor is not null && editor.ActualHeight > 100,
            editor is null ? "(null)" : $"{editor.ActualWidth:F0}x{editor.ActualHeight:F0}");

        if (editor is null) return Finish(window);

        Check("编辑器初始模板可解析", () => InfoTextOf(window).Contains("音符"), InfoTextOf(window));

        editor.SetCaret(editor.Text.Length);
        Pump(80);
        var beforeLength = editor.Text.Length;
        editor.InsertText("\n\n+1,+2,+3,+4");
        Pump(450);

        Check("输入后文本长度增加", () => editor.Text.Length == beforeLength + 13,
            $"{beforeLength} -> {editor.Text.Length}");

        Check("输入后解析出新小节（音符数增加）", () => InfoTextOf(window).Contains("11 音符"),
            InfoTextOf(window));

        Check("撤销恢复文本", () =>
        {
            var canUndo = editor.CanUndo;
            editor.Undo();
            Pump(450);
            return canUndo && editor.Text.Length == beforeLength && editor.CanRedo;
        });

        Check("重做恢复输入", () =>
        {
            editor.Redo();
            Pump(450);
            return editor.Text.Length == beforeLength + 13;
        });

        Check("插入非法内容后状态栏报错", () =>
        {
            editor.SelectAll();
            editor.InsertText("bpm = 120\n1 = C 4/4\n\n1,2,3");
            Pump(500);
            return ErrorTextOf(window).Contains("错误 1");
        });

        Check("编辑器绘制出真实文字像素", () =>
        {
            editor.SelectAll();
            editor.InsertText("bpm = 120\n1 = C 4/4\n\n1,2,3,4\n\n3,02,2,0");
            Pump(550);
            return NonBackgroundPixels(editor) > 150;
        }, NonBackgroundPixels(editor).ToString());

        Check("语法高亮真实渲染（注释/指令/值/音符）", () =>
        {
            var previousFontSize = editor.EditorFontSize;
            editor.EditorFontSize = 24;
            editor.SelectAll();
            editor.InsertText("""
# 测试注释
bpm = 120
1 = C 4/4

1,2,3,4

3,02,2,0
""");
            editor.SetCaret(0);
            Pump(650);

            var colors = new[] { "#6A6A78", "#7FB4FF", "#FFB86C", "#E8E8ED" };
            var results = colors.Select(hex => (hex, ok: ContainsColor(editor, hex, 42))).ToList();
            Console.WriteLine("      颜色采样：" + string.Join("、", results.Select(r => $"{r.hex}={(r.ok ? "有" : "无")}")));

            editor.EditorFontSize = previousFontSize;
            Pump(250);
            return results.All(r => r.ok);
        });

        Check("插入符颜色出现（主题色）", () =>
        {
            editor.FocusEditor();
            editor.SetCaret(editor.Text.Length);
            Pump(120);
            return ContainsColor(editor, "#FF4E6A", 28);
        });

        Check("默认字号够大且行距不松", () =>
        {
            var size = editor.EditorFontSize;
            var ratio = editor.EditorLineHeight / size;
            Console.WriteLine($"      字号 {size}，行高 {editor.EditorLineHeight}，比例 {ratio:F2}");
            return size >= 17 && ratio <= 1.45;
        });

        var gutter = Descendants(editor).OfType<EditorGutter>().FirstOrDefault();
        Check("行号槽已绘制", () => gutter is not null && gutter.ActualWidth > 40,
            gutter is null ? "(null)" : $"{gutter.ActualWidth:F0}");

        Check("缩略图已绘制在右侧", () =>
        {
            var minimap = Descendants(window).OfType<EditorMinimap>().FirstOrDefault();
            if (minimap is null || editor.ActualWidth <= 0) return false;

            var ratio = InkRatio(minimap);
            var leftEdge = minimap.TranslatePoint(new Point(0, 0), editor).X;
            var onRight = leftEdge > editor.ActualWidth * 0.6;

            Console.WriteLine($"      缩略图 {minimap.ActualWidth:F0}x{minimap.ActualHeight:F0}，x={leftEdge:F0}，墨点占比 {ratio:P1}");

            return minimap.ActualWidth >= 70 && minimap.ActualHeight > 100 && ratio > 0.01 && onRight;
        });

        Check("编辑器不显示滚动条（导航交给缩略图）", () =>
            Descendants(editor).OfType<ScrollBar>().All(bar => bar.Visibility != Visibility.Visible));

        Check("缩略图与内容对齐", () =>
        {
            const char lf = (char)10;

            var original = editor.Text;
            var builder = new StringBuilder("bpm = 120" + lf + "1 = C 4/4" + lf + lf);
            for (var i = 0; i < 120; i++)
                builder.Append("1,2,3,4" + lf + lf);

            editor.Text = builder.ToString();
            Pump(700);

            var minimap = Descendants(window).OfType<EditorMinimap>().First();
            var lineHeight = editor.EditorLineHeight;
            var ok = true;
            var details = new List<string>();

            foreach (var fraction in new[] { 0.0, 0.35, 1.0 })
            {
                editor.ScrollToVerticalOffset(fraction * Math.Max(0, editor.Buffer.LineCount * lineHeight - editor.ViewportHeight));
                Pump(200);

                // 视野框顶边应该等于"当前滚到的那一行"在缩略图里的 y
                var expected = minimap.YForLine(editor.VerticalOffset / lineHeight);
                var actual = minimap.ViewportTop;
                details.Add($"{fraction:P0}: 期望 {expected:F1} 实际 {actual:F1}");

                if (Math.Abs(expected - actual) > 1.5) ok = false;
            }

            editor.Text = original;
            editor.ScrollToVerticalOffset(0);
            Pump(400);

            Console.WriteLine("      " + string.Join(" | ", details));
            return ok;
        });

        Check("缩略图：长文档不会糊成一片", () =>
        {
            const char lf = (char)10;

            var original = editor.Text;
            var builder = new StringBuilder("bpm = 120" + lf + "1 = C 4/4" + lf + lf);
            for (var i = 0; i < 300; i++)
                builder.Append("1,2,3,4,5,6,7,+1" + lf + lf);

            editor.Text = builder.ToString();
            Pump(700);

            var minimap = Descendants(window).OfType<EditorMinimap>().First();
            var ratio = InkRatio(minimap);
            var deviation = MeanDeviation(minimap);

            editor.Text = original;
            Pump(400);

            Console.WriteLine($"      600 行文档，墨点占比 {ratio:P1}，平均亮度差 {deviation:F1}");
            return ratio > 0.05 && deviation is > 2 and < 55;
        });

        // ---------------------------------------------------------- 播放

        Check("播放按钮已启用", () => (window.FindName("PlayPauseButton") as Button)?.IsEnabled == true);

        Check("点击播放进入播放状态", () =>
        {
            (window.FindName("PlayPauseButton") as Button)?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(500);
            return PlaybackTextOf(window).Contains("播放中");
        }, PlaybackTextOf(window));

        Check("播放时编辑器有高亮区间", () => editor.HasPlaybackHighlight);

        Check("点击停止回到就绪", () =>
        {
            (window.FindName("StopButton") as Button)?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(250);
            Console.WriteLine($"      停止后：状态='{PlaybackTextOf(window)}' 高亮={editor.HasPlaybackHighlight}");
            return PlaybackTextOf(window).StartsWith("就绪") && !editor.HasPlaybackHighlight;
        }, PlaybackTextOf(window));

        Check("设置窗口能渲染并列出 MIDI 设备", () =>
        {
            var dialog = new SettingsWindow(AppConfig.Load(), _ => { }) { Owner = window };
            dialog.Show();
            Pump(450);

            var combo = dialog.FindName("DeviceCombo") as ComboBox;
            var count = combo?.Items.Count ?? 0;
            var ink = dialog.Content is FrameworkElement root ? NonBackgroundPixels(root) : 0;

            Console.WriteLine($"      设备项 {count}，窗口墨点 {ink}");
            dialog.Close();
            return count >= 1 && ink > 300;
        });

        Check("设置改动立即生效（没有保存按钮）", () =>
        {
            var config = AppConfig.Load();
            config.Theme = "Dark";
            var applied = 0;

            var dialog = new SettingsWindow(config, c =>
            {
                applied++;
                ThemeService.SetMode(c.Mode);
            }) { Owner = window };

            dialog.Show();
            Pump(350);

            var themeCombo = dialog.FindName("ThemeCombo") as ComboBox;
            var hasSaveButton = Descendants(dialog).OfType<Button>()
                .Any(b => (b.Content as string) == "保存");

            themeCombo!.SelectedIndex = 1; // 浅色
            Pump(300);
            var lightApplied = !ThemeService.IsDark;

            themeCombo.SelectedIndex = 2; // 跟随系统
            Pump(300);
            var systemApplied = ThemeService.Mode == AppTheme.System;

            themeCombo.SelectedIndex = 0; // 深色
            Pump(300);

            var themeItems = themeCombo.Items.Count;
            dialog.Close();

            Console.WriteLine($"      回调 {applied} 次，主题项 {themeItems} 个，有保存按钮={hasSaveButton}");
            return applied >= 3 && lightApplied && systemApplied && !hasSaveButton && themeItems == 3;
        });

        Check("工具栏有从光标处播放按钮", () =>
            Descendants(window).OfType<Button>()
                .Any(b => (b.ToolTip as string) == "从光标所在小节播放" && b.IsEnabled));

        Check("保存对话框预览双文件路径", () =>
        {
            var dialog = new SaveAsWindow(null, AppConfig.Load(), false) { Owner = window };
            dialog.Show();
            Pump(300);

            var nameBox = dialog.FindName("NameBox") as TextBox;
            nameBox!.Text = "测试谱";
            Pump(250);

            var texts = Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
            var attm = texts.FirstOrDefault(t => t.EndsWith(".attm", StringComparison.Ordinal));
            var midi = texts.FirstOrDefault(t => t.EndsWith(".mid", StringComparison.Ordinal));

            dialog.Close();

            Console.WriteLine($"      {attm} | {midi}");
            return attm is not null && attm.EndsWith("测试谱.attm", StringComparison.Ordinal)
                   && midi is not null && midi.EndsWith("测试谱.mid", StringComparison.Ordinal);
        });

        // ---------------------------------------------------------- 主题切换

        ThemeService.SetTheme(false);
        editor.ApplyTheme();
        Pump(400);

        var lightBackground = (window.Background as SolidColorBrush)?.Color.ToString() ?? "(null)";
        Check("浅色调色板切换成功", () => lightBackground == "#FFF7F7F9", $"背景 {lightBackground}");

        var lightAccent = (window.TryFindResource("Brush.Accent") as SolidColorBrush)?.Color.ToString() ?? "(null)";
        Check("主题色为 #FF4E6A", () => lightAccent == "#FFFF4E6A", $"Accent {lightAccent}");

        Check("浅色主题下仍能绘制文字", () => NonBackgroundPixels(editor) > 150,
            NonBackgroundPixels(editor).ToString());

        Check("大文件：两千行仍能流畅渲染", () =>
        {
            const char lf = (char)10;

            var original = editor.Text;
            var builder = new StringBuilder("bpm = 120" + lf + "1 = C 4/4" + lf + lf);
            for (var i = 0; i < 500; i++)
                builder.Append("1,2,3,4" + lf + lf);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            editor.Text = builder.ToString();
            Pump(700);
            var pixels = NonBackgroundPixels(editor);
            watch.Stop();

            editor.Text = original;
            Pump(400);

            Console.WriteLine($"      {watch.ElapsedMilliseconds} ms，墨点 {pixels}");
            return pixels > 100 && watch.ElapsedMilliseconds < 3000;
        });

        ThemeService.SetTheme(true);
        editor.ApplyTheme();
        Pump(300);

        // 还原用户原来的主题配置，别让测试改掉本机设置
        try
        {
            var restore = AppConfig.Load();
            restore.Theme = originalTheme;
            restore.Save();
            ThemeService.SetMode(restore.Mode);
        }
        catch { /* 忽略 */ }

        var dumpIndex = Array.IndexOf(args, "--dump");
        if (dumpIndex >= 0 && dumpIndex + 1 < args.Length)
            DumpWindow(window, args[dumpIndex + 1]);

        return Finish(window);
    }

    private static void DumpWindow(Window window, string path)
    {
        if (window.Content is not FrameworkElement root) return;

        var width = (int)Math.Max(1, root.ActualWidth);
        var height = (int)Math.Max(1, root.ActualHeight);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"已导出界面截图：{path}（{width}x{height}）");
    }

    private static int Finish(Window window)
    {
        Console.WriteLine();
        Console.WriteLine($"通过 {_passed}，失败 {_failed}");
        window.Close();
        return _failed == 0 ? 0 : 1;
    }

    private static string InfoTextOf(Window window)
        => (window.FindName("InfoText") as TextBlock)?.Text ?? string.Empty;

    private static string PlaybackTextOf(Window window)
        => (window.FindName("PlaybackStatusText") as TextBlock)?.Text ?? string.Empty;

    private static string ErrorTextOf(Window window)
        => (window.FindName("ErrorCountText") as TextBlock)?.Text ?? string.Empty;

    private static bool GlyphExists(string resourceKey, int codePoint)
    {
        if (Application.Current?.TryFindResource(resourceKey) is not FontFamily family) return false;
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        return typeface.TryGetGlyphTypeface(out var glyphTypeface)
               && glyphTypeface.CharacterToGlyphMap.ContainsKey(codePoint);
    }

    /// <summary>把一段文字画到位图上，数一数有多少个墨点像素（验证字体真的有字形）。</summary>
    private static int InkPixels(string resourceKey, string text, double fontSize)
    {
        if (Application.Current?.TryFindResource(resourceKey) is not FontFamily family) return 0;

        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.Black, 1.0);

        var width = Math.Max(8, (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 6);
        var height = Math.Max(8, (int)Math.Ceiling(formatted.Height) + 6);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawText(formatted, new Point(3, 3));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var ink = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 128) ink++;
        }

        return ink;
    }

    /// <summary>检查渲染结果里是否存在接近指定颜色的像素（验证语法高亮真的画出来了）。</summary>
    private static bool ContainsColor(FrameworkElement element, string hex, int tolerance)
    {
        var width = (int)Math.Max(1, element.ActualWidth);
        var height = (int)Math.Max(1, element.ActualHeight);
        if (width <= 1 || height <= 1) return false;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var color = (Color)ColorConverter.ConvertFromString(hex);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * stride + x * 4;
                if (Math.Abs(pixels[i] - color.B) <= tolerance
                    && Math.Abs(pixels[i + 1] - color.G) <= tolerance
                    && Math.Abs(pixels[i + 2] - color.R) <= tolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (Color Color, int Distance) ClosestColor(FrameworkElement element, string hex)
    {
        var width = (int)Math.Max(1, element.ActualWidth);
        var height = (int)Math.Max(1, element.ActualHeight);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var target = (Color)ColorConverter.ConvertFromString(hex);
        var best = Color.FromRgb(0, 0, 0);
        var bestDistance = int.MaxValue;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var distance = Math.Abs(b - target.B) + Math.Abs(g - target.G) + Math.Abs(r - target.R);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = Color.FromRgb(r, g, b);
        }

        return (best, bestDistance);
    }

    /// <summary>把一个元素画到位图上，统计文本区里"和背景不同"的像素数（深浅主题通用）。</summary>
    /// <summary>统计元素与背景色的平均亮度差，用来判断"是不是太抢眼"。</summary>
    private static double MeanDeviation(FrameworkElement element)
    {
        var width = (int)Math.Max(1, element.ActualWidth);
        var height = (int)Math.Max(1, element.ActualHeight);
        if (width <= 2 || height <= 2) return 0;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var histogram = new Dictionary<int, int>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var key = pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16);
            histogram[key] = histogram.GetValueOrDefault(key) + 1;
        }

        var background = histogram.OrderByDescending(pair => pair.Value).First().Key;
        int bgB = background & 0xFF, bgG = (background >> 8) & 0xFF, bgR = (background >> 16) & 0xFF;

        double sum = 0;
        var total = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            sum += (Math.Abs(pixels[i] - bgB) + Math.Abs(pixels[i + 1] - bgG) + Math.Abs(pixels[i + 2] - bgR)) / 3.0;
            total++;
        }

        return total == 0 ? 0 : sum / total;
    }

    /// <summary>统计元素的"墨点比例"（与出现最多的颜色不同的像素占比），适合衡量缩略图密不密。</summary>
    private static double InkRatio(FrameworkElement element)
    {
        var width = (int)Math.Max(1, element.ActualWidth);
        var height = (int)Math.Max(1, element.ActualHeight);
        if (width <= 2 || height <= 2) return 0;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        // 找出出现最多的颜色当作背景色
        var histogram = new Dictionary<int, int>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var key = pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16);
            histogram[key] = histogram.GetValueOrDefault(key) + 1;
        }

        var background = histogram.OrderByDescending(pair => pair.Value).First().Key;
        int bgB = background & 0xFF, bgG = (background >> 8) & 0xFF, bgR = (background >> 16) & 0xFF;

        var ink = 0;
        var total = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * stride + x * 4;
                total++;
                var distance = Math.Abs(pixels[i] - bgB) + Math.Abs(pixels[i + 1] - bgG) + Math.Abs(pixels[i + 2] - bgR);
                if (distance > 24) ink++;
            }
        }

        return total == 0 ? 0 : ink / (double)total;
    }
    private static int NonBackgroundPixels(FrameworkElement element)
    {
        var width = (int)Math.Max(1, element.ActualWidth);
        var height = (int)Math.Max(1, element.ActualHeight);
        if (width <= 1 || height <= 1) return 0;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        // 取右下角像素当作背景参考色
        var last = stride * height - 4;
        int bgB = pixels[last], bgG = pixels[last + 1], bgR = pixels[last + 2];

        var count = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 70; x < width; x++)
            {
                var i = y * stride + x * 4;
                var diff = Math.Abs(pixels[i] - bgB) + Math.Abs(pixels[i + 1] - bgG) + Math.Abs(pixels[i + 2] - bgR);
                if (diff > 60) count++;
            }
        }

        return count;
    }

    private static string Describe(string text)
    {
        if (string.IsNullOrEmpty(text)) return "(空)";
        return $"U+{(int)text[0]:X4}";
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Check(string name, Func<bool> test, string detail = "")
    {
        try
        {
            if (test())
            {
                _passed++;
                Console.WriteLine($"  ok   {name}" + (detail.Length > 0 ? $"  [{detail}]" : ""));
            }
            else
            {
                _failed++;
                Console.WriteLine($"  FAIL {name}" + (detail.Length > 0 ? $"  [{detail}]" : ""));
            }
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL {name}：{ex.GetType().Name} {ex.Message}");
        }
    }
}
