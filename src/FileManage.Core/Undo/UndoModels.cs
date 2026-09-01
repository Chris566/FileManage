using System.Text.Json.Serialization;

namespace FileManage.Core.Undo;

/// <summary>
/// 逆操作基类（设计文档 §4.5）。逆序执行即可恢复到执行前状态。
/// JSON 多态序列化（System.Text.Json，.NET 8 BCL）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(UndoRename), "rename")]
[JsonDerivedType(typeof(UndoCopyCreated), "copy")]
[JsonDerivedType(typeof(UndoMove), "move")]
[JsonDerivedType(typeof(UndoOverwrite), "overwrite")]
public abstract record UndoAction;

/// <summary>撤销重命名：把改名后的文件改回原名。</summary>
public sealed record UndoRename(string CurrentPath, string OriginalPath) : UndoAction;

/// <summary>撤销复制：删除复制出的文件。</summary>
public sealed record UndoCopyCreated(string CreatedPath) : UndoAction;

/// <summary>撤销移动：把文件移回原位置。</summary>
public sealed record UndoMove(string MovedPath, string OriginalPath) : UndoAction;

/// <summary>撤销覆盖：从备份恢复被覆盖的原文件。</summary>
public sealed record UndoOverwrite(string OverwrittenPath, string BackupPath) : UndoAction;

/// <summary>撤销批次：一次执行产生的全部逆操作。</summary>
public sealed record UndoBatch(Guid Id, DateTime Time, string Description, IReadOnlyList<UndoAction> Actions)
{
    public bool IsEmpty => Actions.Count == 0;

    /// <summary>
    /// 与本批次关联的分类整理报表文件完整路径（撤销时同步删除，保证原子性）。
    /// 旧批次 JSON 无此字段时反序列化为空列表，向后兼容。
    /// </summary>
    public IReadOnlyList<string> ReportPaths { get; init; } = [];
}

/// <summary>撤销执行结果。</summary>
public sealed record UndoResult
{
    public required int Reverted { get; init; }

    public required int Skipped { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>已同步删除的关联报表文件数。</summary>
    public int ReportsDeleted { get; init; }

    /// <summary>
    /// 因关联报表删除失败而中止撤销（文件未做任何修改）。
    /// 原子性保证：要么文件恢复与报表删除都执行，要么都不执行。
    /// </summary>
    public bool Aborted { get; init; }

    public bool Success => Errors.Count == 0;
}
