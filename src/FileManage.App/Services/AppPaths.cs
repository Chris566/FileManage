using System.IO;

namespace FileManage.App.Services;

/// <summary>
/// 便携版路径布局：全部应用数据存放在 exe 同目录的 Data\ 子文件夹（随文件夹走，免安装可携带）。
/// 目录无写权限（如 Program Files）时回退 %AppData%\FileManage\Portable，避免启动崩溃。
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// exe 所在目录。用 ProcessPath（启动器 exe 真实路径）而非 BaseDirectory：
    /// 便携版经 runtime\ 子目录承载运行时，BaseDirectory 指向 runtime\，数据必须在根目录。
    /// </summary>
    public static string Root { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>旧版数据位置（单文件版），用于一次性自动迁移。</summary>
    public static string LegacyAppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileManage");

    private static string? _dataRoot;

    /// <summary>数据根目录：&lt;exe目录&gt;\Data（无写权限时回退 %AppData%\FileManage\Portable）。</summary>
    public static string DataRoot => _dataRoot ??= ResolveDataRoot();

    public static string SettingsPath => Path.Combine(DataRoot, "settings.json");

    public static string RulesPath => Path.Combine(DataRoot, "rules.json");

    public static string UndoDir => Path.Combine(DataRoot, "undo");

    public static string BackupDir => Path.Combine(DataRoot, "backup");

    /// <summary>更新完成后遗留的备份目录（新版本启动时清理）。</summary>
    public static string UpdateBackupDir => Path.Combine(Root, "_update_backup");

    /// <summary>测试注入：强制指定数据根目录（null = 自动解析）。</summary>
    internal static void SetDataRootForTests(string? root) => _dataRoot = root;

    private static string ResolveDataRoot()
    {
        var preferred = Path.Combine(Root, "Data");

        try
        {
            Directory.CreateDirectory(preferred);

            // 写探测：目录存在不代表可写（Program Files 场景）
            var probe = Path.Combine(preferred, ".write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            return preferred;
        }
        catch (Exception)
        {
            var fallback = Path.Combine(LegacyAppDataRoot, "Portable");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
