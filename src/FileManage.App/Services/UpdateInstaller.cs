using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace FileManage.App.Services;

/// <summary>
/// 更新安装器：下载新版本 → 备份当前 exe → 通过批处理脚本替换并重启。
/// 回滚机制：备份文件保留在临时目录，若新版本启动失败用户可手动恢复。
/// </summary>
public static class UpdateInstaller
{
    /// <summary>
    /// 下载更新包到临时文件，返回临时文件路径。
    /// progress 报告 0-100 的下载进度。
    /// </summary>
    public static async Task<string> DownloadAsync(
        string downloadUrl,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);

        using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var tempPath = Path.Combine(Path.GetTempPath(), $"FileManage_Update_{Guid.NewGuid():N}.exe");

        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            long bytesRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRead += read;

                if (totalBytes > 0)
                {
                    progress?.Report((int)(bytesRead * 100.0 / totalBytes));
                }
            }
        }

        return tempPath;
    }

    /// <summary>
    /// 安装更新：生成批处理脚本执行"备份当前 exe → 替换 → 重启新版本"。
    /// 当前进程退出后由批脚本完成替换；回滚：备份文件保留在 exe 同目录 .bak。
    /// </summary>
    /// <param name="downloadedExePath">已下载的新版本 exe 临时路径。</param>
    /// <param name="currentExePath">当前运行的 exe 路径（通过 Process.GetCurrentProcess().MainModule.FileName 获取）。</param>
    public static void InstallAndRestart(string downloadedExePath, string currentExePath)
    {
        var backupPath = currentExePath + ".bak";
        var tempPath = downloadedExePath;

        // 生成批处理脚本：等待当前进程退出 → 备份 → 替换 → 启动新版本 → 清理
        var batPath = Path.Combine(Path.GetTempPath(), $"FileManage_Update_{Guid.NewGuid():N}.bat");

        var script = $"""
@echo off
chcp 65001 >nul
:: 等待当前 FileManage 进程退出
:wait_exit
tasklist /fi "pid eq {Environment.ProcessId}" 2>nul | find "{Environment.ProcessId}" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait_exit
)
:: 备份当前版本（回滚机制：保留 .bak 文件）
if exist "{backupPath}" del "{backupPath}"
copy "{currentExePath}" "{backupPath}" >nul 2>&1
:: 替换为新版本
copy "{tempPath}" "{currentExePath}" >nul 2>&1
if errorlevel 1 (
    :: 替换失败，回滚
    copy "{backupPath}" "{currentExePath}" >nul 2>&1
    start "" "{currentExePath}"
    del "{tempPath}" >nul 2>&1
    del "{batPath}" >nul 2>&1
    exit /b 1
)
:: 启动新版本并清理临时文件
start "" "{currentExePath}"
del "{tempPath}" >nul 2>&1
:: 延迟删除批处理自身
( goto ) 2>nul & del "{batPath}"
""";

        File.WriteAllText(batPath, script);

        // 启动批处理脚本（隐藏窗口），然后退出当前进程
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Application.Current?.Shutdown();
    }

    /// <summary>
    /// 回滚到备份版本（手动调用，用于新版本启动异常时恢复）。
    /// </summary>
    public static void Rollback(string currentExePath)
    {
        var backupPath = currentExePath + ".bak";

        if (!File.Exists(backupPath))
        {
            return;
        }

        var batPath = Path.Combine(Path.GetTempPath(), $"FileManage_Rollback_{Guid.NewGuid():N}.bat");

        var script = $"""
@echo off
chcp 65001 >nul
:wait_exit
tasklist /fi "pid eq {Environment.ProcessId}" 2>nul | find "{Environment.ProcessId}" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait_exit
)
copy "{backupPath}" "{currentExePath}" >nul 2>&1
start "" "{currentExePath}"
del "{backupPath}" >nul 2>&1
( goto ) 2>nul & del "{batPath}"
""";

        File.WriteAllText(batPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Application.Current?.Shutdown();
    }
}
