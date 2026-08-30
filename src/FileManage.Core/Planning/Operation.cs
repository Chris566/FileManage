using FileManage.Core.Models;
using FileManage.Core.Rules;

namespace FileManage.Core.Planning;

/// <summary>文件操作基类（设计文档 §4.5）。SourcePath 为计划中的原始源路径。</summary>
public abstract record Operation
{
    /// <summary>计划中的原始源路径（执行时经动态路径映射解析为实际位置）。</summary>
    public abstract string SourcePath { get; init; }
}

/// <summary>源目录内重命名。</summary>
public sealed record RenameOp(string SourcePath, string NewName, string NewPath) : Operation;

/// <summary>复制到目标目录（源文件保留，对齐旧版分类复制）。</summary>
public sealed record CopyOp(string SourcePath, string TargetDir, string TargetName) : Operation;

/// <summary>移动到目标目录（源文件不保留）。</summary>
public sealed record MoveOp(string SourcePath, string TargetDir, string TargetName) : Operation;

/// <summary>
/// 计划条目：单个文件的完整计划（预览用）。
/// </summary>
public sealed record PlanEntry
{
    /// <summary>源文件。</summary>
    public required FileItem Item { get; init; }

    /// <summary>命名引擎直接输出（未做冲突处理）。</summary>
    public required string RequestedName { get; init; }

    /// <summary>源目录内重命名的最终名（冲突改号后；不重命名时 = 原名）。</summary>
    public required string FinalName { get; init; }

    /// <summary>重命名冲突标记。</summary>
    public required ConflictType ConflictType { get; init; }

    /// <summary>命中的分类（null = 未命中或未启用分类）。</summary>
    public ClassificationResult? Classification { get; init; }

    /// <summary>分类目标内最终文件名（冲突改号后）。</summary>
    public string? CopyFinalName { get; init; }

    /// <summary>分类目标内冲突标记。</summary>
    public ConflictType CopyConflictType { get; init; } = ConflictType.None;

    /// <summary>重命名操作（null = 无需重命名或被阻断）。</summary>
    public RenameOp? Rename { get; init; }

    /// <summary>复制/移动操作（null = 未命中分类或被阻断）。</summary>
    public Operation? Transfer { get; init; }
}

/// <summary>
/// 操作计划（设计文档 §4.5）：预览、执行、撤销共用同一份数据。
/// </summary>
public sealed record OperationPlan(Guid Id, DateTime CreatedAt, IReadOnlyList<PlanEntry> Entries)
{
    /// <summary>扁平操作序列（执行器按此顺序执行）。</summary>
    public IReadOnlyList<Operation> Operations =>
        Entries
            .SelectMany(e => new Operation?[] { e.Rename, e.Transfer })
            .Where(op => op is not null)
            .Cast<Operation>()
            .ToArray();
}
