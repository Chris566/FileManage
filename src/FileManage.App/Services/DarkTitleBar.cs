using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileManage.App.Services;

/// <summary>
/// 深色标题栏（Windows 10 1809+ DWMWA_USE_IMMERSIVE_DARK_MODE）。
/// 调用失败静默忽略（旧系统保持浅色标题栏）。
/// </summary>
internal static class DarkTitleBar
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>按 UIStateService 当前主题设置窗口标题栏。</summary>
    public static void Apply(Window window)
    {
        Apply(window, UIStateService.Settings.Theme.Equals("dark", StringComparison.OrdinalIgnoreCase));
    }

    public static void Apply(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero)
            {
                return;
            }

            var value = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // 非 Win10 1809+ / DWM 不可用时忽略
        }
    }
}
