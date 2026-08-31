namespace FileManage.Core.Reporting;

/// <summary>
/// 分类整理报表行：单个参与分类整理的文件的完整记录。
/// 列定义（需求）：原文件名 / 原文件完整路径 / 新文件名 / 新文件完整路径 /
/// 分类 / 操作 / 冲突 / 文件类型（规则名称）。
/// </summary>
public sealed record ClassificationReportRow
{
    /// <summary>原文件名。</summary>
    public required string OriginalName { get; init; }

    /// <summary>原文件完整路径。</summary>
    public required string OriginalPath { get; init; }

    /// <summary>新文件名（未改名/未转移时与原名一致）。</summary>
    public required string NewName { get; init; }

    /// <summary>新文件完整路径（未转移/未改名时与原路径一致）。</summary>
    public required string NewPath { get; init; }

    /// <summary>分类目录名称（目标根下的子目录，未命中规则时为空）。</summary>
    public required string Category { get; init; }

    /// <summary>执行的操作，如"移动"、"复制"、"重命名"（组合用"+"连接）。</summary>
    public required string Operation { get; init; }

    /// <summary>冲突标记，如"无冲突"、"文件名冲突"、"文件已存在"。</summary>
    public required string Conflict { get; init; }

    /// <summary>命中的规则名称（"规则管理"中的规则名，未命中为空）。</summary>
    public required string RuleName { get; init; }
}
