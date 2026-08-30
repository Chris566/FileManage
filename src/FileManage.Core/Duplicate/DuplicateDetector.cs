using FileManage.Core.Abstractions;
using FileManage.Core.Models;

namespace FileManage.Core.Duplicate;

/// <summary>一组内容完全相同的文件（互为重复）。</summary>
public sealed record DuplicateGroup(string Sha256, long SizeBytes, IReadOnlyList<FileItem> Files);

/// <summary>重复检测结果。</summary>
public sealed record DuplicateScanResult(
    IReadOnlyList<DuplicateGroup> Groups,
    int ScannedCount,
    int DuplicateFileCount,
    long WastedBytes)
{
    /// <summary>被重复副本占用的字节 = 每组 (文件数-1) × 单文件大小 之和。</summary>
    public static long SumWasted(IEnumerable<DuplicateGroup> groups)
    {
        return groups.Sum(g => g.SizeBytes * (g.Files.Count - 1));
    }
}

/// <summary>
/// 重复文件检测器：两阶段——先按文件大小分组（O(n) 零 IO），
/// 仅对"同大小 ≥2 个"的组计算 SHA-256 全量哈希，内容一致才判定重复。
/// </summary>
public sealed class DuplicateDetector(IFileSystemService fileSystem)
{
    public DuplicateScanResult Detect(IReadOnlyList<FileItem> items, CancellationToken ct = default)
    {
        // 1. 按大小粗分组：大小唯一或为 0（空文件不算重复，避免全空目录误报）的跳过
        var sizeGroups = items
            .Where(f => f.SizeBytes > 0)
            .GroupBy(f => f.SizeBytes)
            .Where(g => g.Count() > 1)
            .ToArray();

        var groups = new List<DuplicateGroup>();
        var hashToFiles = new Dictionary<string, List<FileItem>>();

        foreach (var sizeGroup in sizeGroups)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var file in sizeGroup)
            {
                ct.ThrowIfCancellationRequested();

                string hash;

                try
                {
                    hash = fileSystem.ComputeSha256(file.FullPath);
                }
                catch (Exception)
                {
                    continue; // 文件被占用/消失，跳过
                }

                if (!hashToFiles.TryGetValue(hash, out var list))
                {
                    list = [];
                    hashToFiles[hash] = list;
                }

                list.Add(file);
            }
        }

        foreach (var (hash, files) in hashToFiles)
        {
            if (files.Count > 1)
            {
                groups.Add(new DuplicateGroup(hash, files[0].SizeBytes, files));
            }
        }

        groups.Sort(static (a, b) =>
        {
            var bySize = b.SizeBytes.CompareTo(a.SizeBytes); // 大文件在前
            return bySize != 0 ? bySize : string.CompareOrdinal(a.Sha256, b.Sha256);
        });

        return new DuplicateScanResult(
            groups,
            items.Count,
            groups.Sum(g => g.Files.Count),
            DuplicateScanResult.SumWasted(groups));
    }
}
