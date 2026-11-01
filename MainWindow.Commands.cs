using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ATTM.Core.Midi;
using ATTM.Plugins;
using ATTM.Core.Playback;
using ATTM.Core.Syntax;
using ATTM.Services;
using ATTM.Views;
using Microsoft.Win32;

namespace ATTM;

public partial class MainWindow
{
    // ------------------------------------------------------------ 工具栏

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_parse is not { CanPlay: true } parsed) return;

        switch (_player.State)
        {
            case PlayerState.Playing:
                _player.Pause();
                return;
            case PlayerState.Paused:
                _fadedMeasure = -1;
                _player.Play();
                return;
            default:
                _player.SetScore(parsed.Score);
                _fadedMeasure = -1;
                _player.Play();
                return;
        }
    }

    private void PlayFromCursorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_parse is not { CanPlay: true } parsed) return;

        _player.SetScore(parsed.Score);
        _fadedMeasure = -1;
        _player.Play(GetCaretPlaybackPosition());
    }

    /// <summary>光标所在小节对应的播放时刻。</summary>
    private TimeSpan GetCaretPlaybackPosition()
    {
        if (_parse?.Score is not { } score) return TimeSpan.Zero;

        var measure = _parse.GetMeasureIndex(Editor.CaretOffset);
        return score.GetMeasureStartTime(measure);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        ClearHighlight();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e) => PromptImportMidi();

    /// <summary>选一个文件导入（工具栏按钮与 Ctrl+I 共用）。文件类型由已装载的导入插件决定。</summary>
    private void PromptImportMidi()
    {
        var importers = _plugins.Importers;

        if (importers.Count == 0)
        {
            MessageBox.Show(this,
                "还没有可用的导入插件。\n\n把插件 dll 放到程序目录的 plugins\\ 子目录后重启即可。",
                "ATTM",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var filters = importers
            .Select(entry => ((IImporterPlugin)entry.Plugin!).FileFilter)
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .ToList();

        filters.Add("所有文件 (*.*)|*.*");

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入",
            Filter = string.Join("|", filters),
            CheckFileExists = true,
            Multiselect = false
        };

        try
        {
            dialog.InitialDirectory = AppConfig.ResolveSaveFolder(_config);
        }
        catch
        {
            // 目录不可用就让系统决定初始位置
        }

        if (dialog.ShowDialog(this) != true) return;

        var importer = FindImporter(dialog.FileName);

        if (importer is null)
        {
            MessageBox.Show(this,
                $"没有插件认得 {Path.GetExtension(dialog.FileName)} 这种文件。",
                "ATTM",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ConfirmDiscard()) return;

        ImportWith(importer, dialog.FileName);
    }

    private void NewButton_Click(object sender, RoutedEventArgs e) => NewDocument();

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenLibraryFolder();

    // 工具栏的保存键始终要求重新选位置；Ctrl+S 才在保存过一次后直接覆盖保存
    private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveDocument(forceDialog: true);

    private void UndoButton_Click(object sender, RoutedEventArgs e) => Editor.Undo();

    private void RedoButton_Click(object sender, RoutedEventArgs e) => Editor.Redo();

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var next = ThemeService.IsDark ? AppTheme.Light : AppTheme.Dark;
        _config.Theme = ThemeService.ToConfigString(next);
        _config.Save();

        ThemeService.SetMode(next);
    }

    private void PluginManagerButton_Click(object sender, RoutedEventArgs e) => ShowPluginManager();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_config, ApplyLiveSettings, _plugins.Entries) { Owner = this };
        dialog.ShowDialog();
    }
}
