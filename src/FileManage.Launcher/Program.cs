using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileManage.Launcher;

/// <summary>
/// FileManage 便携版启动器（NativeAOT 原生 exe，发布后改名 FileManage.exe 放根目录）。
/// 职责：从 runtime\ 子目录加载 .NET 运行时并执行主程序（FileManage.dll），
/// 使根目录只保留 FileManage.exe + runtime\ + Data\ + manifest.json。
/// 走 hostfxr_initialize_for_dotnet_command_line + hostfxr_run_app（与 apphost 同一执行路径）：
/// 入口程序集、pack:// 资源、STA、deps 解析行为与标准自包含发布完全一致，托管代码零侵入。
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return Fail("无法定位启动器路径。", null);
        }

        var root = Path.GetDirectoryName(exePath)!;
        var runtimeDir = Path.Combine(root, "runtime");
        var hostfxrPath = Path.Combine(runtimeDir, "hostfxr.dll");
        var appDll = Path.Combine(runtimeDir, "FileManage.dll");
        var runtimeConfigPath = Path.Combine(runtimeDir, "FileManage.runtimeconfig.json");

        if (!File.Exists(hostfxrPath))
        {
            return Fail("找不到运行时组件 runtime\\hostfxr.dll。", root);
        }

        if (!File.Exists(appDll) || !File.Exists(runtimeConfigPath))
        {
            return Fail("找不到主程序 runtime\\FileManage.dll，安装可能不完整。", root);
        }

        nint hostfxr = 0;
        nint context = 0;
        var arg0 = Marshal.StringToHGlobalUni(appDll);
        var hostPath = Marshal.StringToHGlobalUni(exePath);
        var dotnetRoot = Marshal.StringToHGlobalUni(runtimeDir);

        try
        {
            hostfxr = NativeLibrary.Load(hostfxrPath);

            unsafe
            {
                var initialize = (delegate* unmanaged[Cdecl]<int, ushort**, HostfxrInitializeParameters*, nint*, int>)
                    NativeLibrary.GetExport(hostfxr, "hostfxr_initialize_for_dotnet_command_line");
                var runApp = (delegate* unmanaged[Cdecl]<nint, int>)
                    NativeLibrary.GetExport(hostfxr, "hostfxr_run_app");
                var close = (delegate* unmanaged[Cdecl]<nint, int>)
                    NativeLibrary.GetExport(hostfxr, "hostfxr_close");

                ushort** argv = (ushort**)&arg0;
                var parameters = new HostfxrInitializeParameters
                {
                    Size = sizeof(HostfxrInitializeParameters),
                    HostPath = (ushort*)hostPath,
                    DotnetRoot = (ushort*)dotnetRoot
                };

                int hr = initialize(1, argv, &parameters, &context);
                if (hr < 0)
                {
                    return Fail($"运行时初始化失败（0x{hr:X8}）。", runtimeConfigPath);
                }

                int exitCode = runApp(context);

                _ = close(context);
                return exitCode;
            }
        }
        catch (Exception ex)
        {
            return Fail($"启动器异常：{ex.Message}", root);
        }
        finally
        {
            if (hostfxr != 0)
            {
                NativeLibrary.Free(hostfxr);
            }

            Marshal.FreeHGlobal(arg0);
            Marshal.FreeHGlobal(hostPath);
            Marshal.FreeHGlobal(dotnetRoot);
        }
    }

    /// <summary>初始化失败提示（弹窗 + 非零退出码）。</summary>
    private static int Fail(string message, string? hintPath)
    {
        var detail = hintPath is null
            ? message
            : $"{message}\n\n位置：{hintPath}\n请确认程序文件夹完整（应包含 runtime\\ 子目录）。";

        _ = MessageBoxW(0, detail, "FileManage 启动失败", 0x10 /* MB_ICONERROR */);
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HostfxrInitializeParameters
    {
        public nint Size;
        public unsafe ushort* HostPath;
        public unsafe ushort* DotnetRoot;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);
}
