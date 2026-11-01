using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATTM.Plugins;
using ATTM.Services;

namespace ATTM.Views;

/// <summary>
/// 插件管理器：列出所有扫描到的插件，控制启用状态，打开插件目录。
/// 这里是唯一知道「插件有哪些」的界面；主程序其它地方只按能力取用。
/// </summary>
public partial class PluginManagerWindow : Window
{
    private readonly PluginHost _host;
    private readonly AppConfig _config;

    private readonly Action? _onReload;

    public PluginManagerWindow(PluginHost host, AppConfig config, Action? onReload = null)
    {
        InitializeComponent();

        _host = host;
        _config = config;
        _onReload = onReload;

        SourceInitialized += (_, _) => WindowTheme.Apply(this, ThemeService.IsDark);

        BuildList();
    }

    /// <summary>本次是否改动过启用状态（调用方据此提示重启）。</summary>
    public bool ToggledAny { get; private set; }

    private void BuildList()
    {
        PluginList.Items.Clear();

        var loaded = _host.Entries.Count(e => e.IsLoaded);
        var failed = _host.Entries.Count(e => e.State == PluginState.Failed);
        var disabled = _host.Entries.Count(e => e.State == PluginState.Disabled);
        var pending = _host.Entries.Count(e => e.State == PluginState.PendingApproval);

        SummaryText.Text =
            $"共 {_host.Entries.Count} 个：已启用 {loaded} · 已禁用 {disabled} · 待授权 {pending} · 失败 {failed}";

        if (_host.Entries.Count == 0)
        {
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;

        foreach (var entry in _host.Entries)
        {
            PluginList.Items.Add(BuildRow(entry));
        }
    }

    private FrameworkElement BuildRow(PluginEntry entry)
    {
        var grid = new Grid { Margin = new Thickness(14, 8, 14, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toggle = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            IsChecked = entry.State != PluginState.Disabled,
            ToolTip = "取消勾选后重启即不再装载",
            IsEnabled = entry.SourcePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && entry.State != PluginState.PendingApproval
        };

        toggle.Click += (_, _) =>
        {
            SetEnabled(entry, toggle.IsChecked == true);
            BuildList();
        };

        grid.Children.Add(toggle);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var title = entry.Plugin is null
            ? entry.DisplayName
            : $"{entry.Plugin.Name} v{entry.Plugin.Version}";

        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });

        if (entry.Plugin is not null && !string.IsNullOrWhiteSpace(entry.Plugin.Description))
        {
            text.Children.Add(new TextBlock
            {
                Text = entry.Plugin.Description,
                Foreground = (Brush)FindResource("Brush.TextSecondary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        var trust = entry.Trust == PluginTrust.Unrestricted ? "无沙箱" : "沙箱";
        var detail = entry.Error is null
            ? $"{entry.Source} · {trust} · {DescribeCapabilities(entry)}"
            : $"{entry.Source} · {trust} · {entry.Error}";

        text.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = (Brush)FindResource(entry.Error is null ? "Brush.TextSecondary" : "Brush.Error"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 11.5
        });

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var state = new TextBlock
        {
            Text = entry.State switch
            {
                PluginState.Loaded => "已启用",
                PluginState.Disabled => "已禁用",
                PluginState.PendingApproval => "待授权",
                _ => "失败"
            },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = (Brush)FindResource(entry.State switch
            {
                PluginState.Loaded => "Brush.Success",
                PluginState.Disabled => "Brush.TextDisabled",
                PluginState.PendingApproval => "Brush.Accent",
                _ => "Brush.Error"
            })
        };

        Grid.SetColumn(state, 2);
        grid.Children.Add(state);

        return new Border
        {
            BorderBrush = (Brush)FindResource("Brush.Border"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static string DescribeCapabilities(PluginEntry entry)
    {
        if (entry.Plugin is null) return "无";

        var parts = new List<string>(4);
        if (entry.Plugin is IImporterPlugin) parts.Add("导入");
        if (entry.Plugin is IExporterPlugin) parts.Add("导出");
        if (entry.Plugin is ICommandPlugin) parts.Add("命令");
        if (entry.Plugin is IUiPlugin) parts.Add("界面");

        return parts.Count == 0 ? "纯逻辑" : string.Join(" / ", parts);
    }

    private void SetEnabled(PluginEntry entry, bool enabled)
    {
        if (!entry.SourcePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;

        var list = _config.DisabledPlugins;

        if (enabled)
        {
            list.RemoveAll(name => string.Equals(name, entry.Source, StringComparison.OrdinalIgnoreCase));
        }
        else if (!list.Any(name => string.Equals(name, entry.Source, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(entry.Source);
        }

        _config.Save();
        ToggledAny = true;
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        _onReload?.Invoke();
        BuildList();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        foreach (var directory in _host.Directories)
        {
            try
            {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
            }
            catch
            {
                // 打不开就算了，不打扰用户
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
