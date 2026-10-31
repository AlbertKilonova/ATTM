using System.IO;
using System.Windows;
using ATTM.Services;

namespace ATTM.Views;

/// <summary>
/// 保存对话框：文件名 + 目录 + 两个产物（.attm / .mid）的路径预览。
/// </summary>
public partial class SaveAsWindow : Window
{
    private static readonly char[] InvalidNameChars =
        Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }).ToArray();

    private string _folder;

    public SaveAsWindow(string? currentPath, AppConfig config, bool isSaveAs)
    {
        InitializeComponent();

        Title = isSaveAs ? "另存为" : "保存谱面";

        var name = string.IsNullOrEmpty(currentPath)
            ? "未命名"
            : Path.GetFileNameWithoutExtension(currentPath);

        _folder = !string.IsNullOrEmpty(currentPath)
            ? Path.GetDirectoryName(currentPath) ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(_folder))
            _folder = AppConfig.ResolveSaveFolder(config);

        NameBox.Text = name;
        FolderBox.Text = _folder;

        SourceInitialized += (_, _) => WindowTheme.Apply(this, ThemeService.IsDark);

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
            UpdatePreview();
        };

        UpdatePreview();
    }

    /// <summary>用户确认后的完整 .attm 路径。</summary>
    public string? ResultPath { get; private set; }

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePreview();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择保存目录",
            InitialDirectory = _folder
        };

        if (dialog.ShowDialog(this) != true) return;

        _folder = dialog.FolderName;
        FolderBox.Text = _folder;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var name = SanitizeName(NameBox.Text);

        if (name.Length == 0 || string.IsNullOrEmpty(_folder))
        {
            AttmPathText.Text = "—";
            MidiPathText.Text = "—";
            ErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        var basePath = Path.Combine(_folder, name);
        AttmPathText.Text = basePath + ".attm";
        MidiPathText.Text = basePath + ".mid";

        var error = string.Empty;
        if (!Directory.Exists(_folder))
            error = "保存目录不存在。";
        else if (NameBox.Text.Trim().Length == 0)
            error = "请填写文件名。";

        ErrorText.Text = error;
        ErrorText.Visibility = error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string SanitizeName(string raw)
    {
        var name = raw.Trim();

        // 用户手动打上扩展名也接受
        if (name.EndsWith(".attm", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (InvalidNameChars.Contains(ch) || char.IsControl(ch)) continue;
            builder.Append(ch);
        }

        return builder.ToString().Trim().TrimEnd('.');
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = SanitizeName(NameBox.Text);
        if (name.Length == 0)
        {
            ErrorText.Text = "请填写文件名。";
            ErrorText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        if (!Directory.Exists(_folder))
        {
            ErrorText.Text = "保存目录不存在，请重新选择。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var path = Path.Combine(_folder, name + ".attm");

        if (File.Exists(path) &&
            MessageBox.Show(this, $"\"{name}.attm\" 已经存在，要覆盖吗？", "ATTM",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ResultPath = path;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
