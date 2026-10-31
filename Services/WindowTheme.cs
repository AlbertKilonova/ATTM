using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ATTM.Services;

/// <summary>
/// 让窗口标题栏跟随深色 / 浅色主题（Windows 10 20H1 起支持）。
/// </summary>
internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var value = dark ? 1 : 0;
        var size = sizeof(int);
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, size) != 0)
        {
            // 旧版 Windows 用属性 19
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref value, size);
        }
    }
}
