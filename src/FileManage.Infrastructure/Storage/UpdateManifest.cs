using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace FileManage.Infrastructure.Storage;

/// <summary>
/// 版本文件清单（publish/manifest.json，CI 发布时生成）：
/// 记录新版本全部文件的相对路径与 SHA256，用于更新时的跨版本残留清理与校验。
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("files")]
    public List<UpdateManifestEntry> Files { get; set; } = [];

    /// <summary>清单内全部文件的规范化相对路径（'/' 分隔，小写）集合。</summary>
    public HashSet<string> GetNormalizedPaths()
    {
        return Files
            .Select(f => NormalizePath(f.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>相对路径规范化：反斜杠转正斜杠。</summary>
    public static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}

/// <summary>清单中的单个文件条目。</summary>
public sealed class UpdateManifestEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>
/// 增量安装对比器：新版本清单 vs 本地安装目录 → 计算删除清单。
/// 删除清单 = 本地存在的程序文件中，新版本已不再包含的部分（跨版本残留清理）。
/// Data\（用户数据）、_update_backup\（更新备份）、manifest.json 自身始终排除。
/// </summary>
public static class UpdateManifestComparer
{
    public const string DataDirName = "Data";
    public const string UpdateBackupDirName = "_update_backup";
    public const string ManifestFileName = "manifest.json";

    /// <summary>应从对比中排除的目录（规范化相对前缀）。</summary>
    private static readonly string[] ExcludedPrefixes =
    [
        NormalizeDir(DataDirName),
        NormalizeDir(UpdateBackupDirName)
    ];

    /// <summary>
    /// 计算删除清单：本地存在但新版本清单中不再包含的程序文件（相对路径，'/' 分隔）。
    /// 返回的是相对 appRoot 的路径，枚举自真实文件系统，不存在路径逃逸风险。
    /// </summary>
    public static IReadOnlyList<string> ComputeDeleteList(UpdateManifest manifest, string appRoot)
    {
        var newPaths = manifest.GetNormalizedPaths();
        var deleteList = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appRoot, "*", SearchOption.AllDirectories))
        {
            var relative = UpdateManifest.NormalizePath(Path.GetRelativePath(appRoot, file));

            if (IsExcluded(relative))
            {
                continue;
            }

            if (!newPaths.Contains(relative))
            {
                deleteList.Add(relative);
            }
        }

        return deleteList;
    }

    /// <summary>本地文件是否匹配清单条目的 SHA256（文件缺失视为不匹配）。</summary>
    public static bool MatchesManifest(UpdateManifestEntry entry, string appRoot)
    {
        var localPath = Path.Combine(appRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(localPath))
        {
            return false;
        }

        return ComputeSha256(localPath)
            .Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeSha256(string filePath)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();
    }

    private static bool IsExcluded(string normalizedRelative)
    {
        // manifest 自身不参与删除清单（CI 清单枚举先于写入自身，files 可能不含它；豁免保证幂等）
        if (normalizedRelative.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var prefix in ExcludedPrefixes)
        {
            if (normalizedRelative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDir(string dirName)
    {
        return UpdateManifest.NormalizePath(dirName) + "/";
    }
}
