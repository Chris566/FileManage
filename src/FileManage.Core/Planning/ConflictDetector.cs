using FileManage.Core.Abstractions;
using FileManage.Core.Models;

namespace FileManage.Core.Planning;

/// <summary>冲突类型（设计文档 §4.4）。</summary>
public enum ConflictType
{
    /// <summary>无冲突。</summary>
    None,

    /// <summary>计划内多个文件映射到同一新名（已自动改号）。</summary>
    PlanDuplicate,

    /// <summary>目标名磁盘上已存在（执行时按覆盖策略处理）。</summary>
    TargetExists,

    /// <summary>新路径超长（阻断）。</summary>
    PathTooLong,

    /// <summary>新名含非法字符（阻断）。</summary>
    InvalidChars
}

/// <summary>重命名候选：源文件 + 期望新名。</summary>
public sealed record RenameCandidate(FileItem Item, string RequestedName);

/// <summary>单个候选的冲突判定结果。</summary>
public sealed record ConflictItem(
    FileItem Item,
    string RequestedName,
    ConflictType Type,
    string FinalName);

/// <summary>整批候选的冲突报告。</summary>
public sealed record ConflictReport(IReadOnlyList<ConflictItem> Items)
{
    /// <summary>是否存在阻断类冲突（超长/非法字符），存在时不应执行。</summary>
    public bool HasBlockingConflicts => Items.Any(i =>
        i.Type is ConflictType.PathTooLong or ConflictType.InvalidChars);

    /// <summary>非 None 冲突数量。</summary>
    public int ConflictCount => Items.Count(i => i.Type != ConflictType.None);
}

/// <summary>
/// 冲突检测器（设计文档 §4.4）：
/// - PlanDuplicate：计划内同名 → 自动追加 _2/_3 改号；
/// - TargetExists：与磁盘现有文件同名 → 标记，执行期按覆盖策略处理；
/// - PathTooLong / InvalidChars：阻断，保留原名并标记。
/// </summary>
public sealed class ConflictDetector(IFileSystemService fileSystem)
{
    /// <summary>路径长度上限（设计文档 §4.4）。</summary>
    public const int MaxPathLength = 240;

    private const int MaxRenameAttempts = 1000;

    public ConflictReport Detect(
        IEnumerable<RenameCandidate> candidates,
        string targetDirectory,
        CancellationToken ct = default)
    {
        var items = new List<ConflictItem>();
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var requested = candidate.RequestedName;
            var fullPath = Path.Combine(targetDirectory, requested);
            var isSelf = string.Equals(fullPath, candidate.Item.FullPath, StringComparison.OrdinalIgnoreCase);

            if (ContainsInvalidFileNameChars(requested))
            {
                items.Add(new ConflictItem(candidate.Item, requested, ConflictType.InvalidChars, requested));
                continue;
            }

            if (fullPath.Length > MaxPathLength)
            {
                items.Add(new ConflictItem(candidate.Item, requested, ConflictType.PathTooLong, requested));
                continue;
            }

            if (!isSelf && fileSystem.FileExists(fullPath))
            {
                items.Add(new ConflictItem(candidate.Item, requested, ConflictType.TargetExists, requested));
                occupied.Add(requested);
                continue;
            }

            if (!isSelf && occupied.Contains(requested))
            {
                var final = FindAvailableName(targetDirectory, requested, occupied, candidate.Item.FullPath);
                items.Add(new ConflictItem(candidate.Item, requested, ConflictType.PlanDuplicate, final));
                occupied.Add(final);
                continue;
            }

            items.Add(new ConflictItem(candidate.Item, requested, ConflictType.None, requested));
            occupied.Add(requested);
        }

        return new ConflictReport(items);
    }

    /// <summary>在 BaseName_N.Extension 上寻找计划内与磁盘均未占用的名字。</summary>
    private string FindAvailableName(
        string targetDirectory,
        string requested,
        HashSet<string> occupied,
        string selfPath)
    {
        var baseName = Path.GetFileNameWithoutExtension(requested);
        var extension = Path.GetExtension(requested);

        for (var n = 2; n < MaxRenameAttempts; n++)
        {
            var candidateName = $"{baseName}_{n}{extension}";
            var candidatePath = Path.Combine(targetDirectory, candidateName);
            var isSelf = string.Equals(candidatePath, selfPath, StringComparison.OrdinalIgnoreCase);

            if (!occupied.Contains(candidateName)
                && (isSelf || !fileSystem.FileExists(candidatePath)))
            {
                return candidateName;
            }
        }

        // 理论上达不到；退化为原名，交由执行期覆盖策略处理
        return requested;
    }

    private static bool ContainsInvalidFileNameChars(string name)
    {
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }
}
