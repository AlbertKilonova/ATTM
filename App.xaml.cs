using System.IO;
using System.Windows;

namespace ATTM;

/// <summary>
/// 应用入口：支持 "ATTM.exe 谱面.attm" 这种命令行参数，
/// 这样在资源管理器里双击 .attm（安装包已注册关联）就能直接打开。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var file = FindFileArgument(e.Args);

        var window = new MainWindow(file);
        MainWindow = window;
        window.Show();
    }

    private static string? FindFileArgument(string[] args)
    {
        foreach (var argument in args)
        {
            if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-')) continue;

            var path = argument.Trim().Trim('"');
            if (File.Exists(path)) return path;
        }

        return null;
    }
}
