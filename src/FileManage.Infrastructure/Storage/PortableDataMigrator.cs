using System.IO;

namespace FileManage.Infrastructure.Storage;

/// <summary>
/// 便携版一次性数据迁移：把旧版（单文件版）%AppData%\FileManage 下的
/// settings.json / rules.json 与 undo\ / backup\ 目录复制到便携 Data\ 根。
/// 目标已存在的项跳过（不覆盖）；旧数据保留不删，便于回退旧版本。
/// </summary>
public static class PortableDataMigrator
{
    /// <summary>需要迁移的顶层文件。</summary>
    private static readonly string[] Files = ["settings.json", "rules.json"];

    /// <summary>需要迁移的子目录。</summary>
    private static readonly string[] Directories = ["undo", "backup"];

    public static void Migrate(string legacyRoot, string dataRoot)
    {
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        Directory.CreateDirectory(dataRoot);

        foreach (var fileName in Files)
        {
            CopyFileIfMissing(
                Path.Combine(legacyRoot, fileName),
                Path.Combine(dataRoot, fileName));
        }

        foreach (var dirName in Directories)
        {
            CopyDirectoryIfMissing(
                Path.Combine(legacyRoot, dirName),
                Path.Combine(dataRoot, dirName));
        }
    }

    private static void CopyFileIfMissing(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target))
        {
            return;
        }

        File.Copy(source, target, overwrite: false);
    }

    private static void CopyDirectoryIfMissing(string source, string target)
    {
        if (!Directory.Exists(source) || Directory.Exists(target))
        {
            return;
        }

        CopyDirectory(source, target);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }
}
