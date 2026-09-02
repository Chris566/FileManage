namespace FileManage.Core.Reporting;

/// <summary>
/// 回覆条目：单个文件从分类目标位置覆盖回原始位置的映射记录。
/// 从分类整理报表 Excel 解析获得。
/// </summary>
public sealed record RestoreEntry
{
    /// <summary>原文件名。</summary>
    public required string OriginalName { get; init; }

    /// <summary>原文件完整路径（回覆目标）。</summary>
    public required string OriginalPath { get; init; }

    /// <summary>分类后新文件名。</summary>
    public required string NewName { get; init; }

    /// <summary>分类后新文件完整路径（回覆源）。</summary>
    public required string NewPath { get; init; }

    /// <summary>分类目录名称。</summary>
    public required string Category { get; init; }

    /// <summary>命中的规则名称。</summary>
    public required string RuleName { get; init; }
}

/// <summary>
/// 回覆执行结果。
/// </summary>
public sealed record RestoreResult
{
    public required int Total { get; init; }
    public required int Restored { get; init; }
    public required int Skipped { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }

    public bool Success => Errors.Count == 0;
}
