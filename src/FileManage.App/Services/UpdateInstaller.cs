using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using FileManage.Infrastructure.Storage;

namespace FileManage.App.Services;

/// <summary>
/// 更新安装器（便携版）：下载新版本 zip → 解压 → 清单对比（增量安装）→ 批处理替换并重启。
/// 增量/跨版本支持：
/// - robocopy 默认仅复制内容有变化的文件（天然增量安装）；
/// - 通过 manifest.json 哈希清单计算"新版本已移除的文件"删除清单，跨版本升级后无旧版残留；
/// - 数据目录 Data\ 在备份、覆盖、删除清单各环节均被排除，用户数据原样保留。
/// 回滚机制：替换前将当前程序文件备份到 exe 目录 _update_backup，失败自动恢复；
/// 更新成功后由新版本启动时清理（App.TryCleanupUpdateBackup）。
/// </summary>
public static class UpdateInstaller
{
    /// <summary>
    /// 下载更新包（zip）到临时文件，返回临时文件路径。
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
        var tempPath = Path.Combine(Path.GetTempPath(), $"FileManage_Update_{Guid.NewGuid():N}.zip");

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
    /// 安装更新（便携版全文件夹替换）：
    /// 解压新版本 zip → 批处理（等待当前进程退出 → 备份当前程序文件 → 覆盖 → 重启）。
    /// Data\ 数据目录在备份与覆盖时均被排除，更新不影响用户数据。
    /// </summary>
    /// <param name="downloadedZipPath">已下载的新版本 zip 临时路径。</param>
    /// <param name="currentExePath">当前运行的 exe 路径。</param>
    public static void InstallAndRestart(string downloadedZipPath, string currentExePath)
    {
        var appRoot = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = Path.Combine(appRoot, "Data");
        var backupDir = Path.Combine(appRoot, "_update_backup");
        var extractDir = Path.Combine(Path.GetTempPath(), $"FileManage_Update_{Guid.NewGuid():N}");

        // 解压新版本（zip 根即 FileManage.exe + 依赖 + manifest.json）
        ZipFile.ExtractToDirectory(downloadedZipPath, extractDir, overwriteFiles: true);

        // 增量安装：按新版本清单计算"已移除文件"删除清单（跨版本残留清理）
        var deleteLines = BuildDeleteList(extractDir, appRoot);

        var batPath = Path.Combine(Path.GetTempPath(), $"FileManage_Update_{Guid.NewGuid():N}.bat");

        // robocopy 退出码 < 8 视为成功（1=复制了文件，属正常）
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
:: 备份当前程序文件（排除 Data 数据目录与备份目录自身）
rd /s /q "{backupDir}" >nul 2>&1
robocopy "{appRoot}" "{backupDir}" /E /XD "{dataDir}" "{backupDir}" /NFL /NDL /NJH /NJS >nul
:: 覆盖新版本文件（XD 兜底排除 Data，用户数据不受影响）
robocopy "{extractDir}" "{appRoot}" /E /XD "{dataDir}" /NFL /NDL /NJH /NJS >nul
if errorlevel 8 (
    :: 覆盖失败，从备份回滚并重启当前版本
    robocopy "{backupDir}" "{appRoot}" /E /NFL /NDL /NJH /NJS >nul
    start "" "{currentExePath}"
    rd /s /q "{backupDir}" >nul 2>&1
    rd /s /q "{extractDir}" >nul 2>&1
    del "{downloadedZipPath}" >nul 2>&1
    del "{batPath}" >nul 2>&1
    exit /b 1
)
:: 清理新版本已移除的旧文件（跨版本残留）
{string.Join("\r\n", deleteLines)}
:: 启动新版本（_update_backup 留给新进程启动时清理）
start "" "{currentExePath}"
rd /s /q "{extractDir}" >nul 2>&1
del "{downloadedZipPath}" >nul 2>&1
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
    /// 读取新版本解压目录中的 manifest.json，与本地安装目录对比，生成批处理删除行。
    /// 清单缺失（旧版升级/损坏）时返回空清单，退化为全量覆盖安装（安全兜底）。
    /// 路径来自本地文件系统枚举，天然无 ".." 逃逸风险。
    /// </summary>
    private static List<string> BuildDeleteList(string extractDir, string appRoot)
    {
        var manifestPath = Path.Combine(extractDir, UpdateManifestComparer.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(
                File.ReadAllText(manifestPath));

            if (manifest is null)
            {
                return [];
            }

            return UpdateManifestComparer
                .ComputeDeleteList(manifest, appRoot)
                .Select(relative =>
                {
                    var local = Path.Combine(appRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                    return $"if exist \"{local}\" del /f /q \"{local}\"";
                })
                .ToList();
        }
        catch
        {
            // 清单解析失败不阻断更新，退化为全量覆盖
            return [];
        }
    }
}
