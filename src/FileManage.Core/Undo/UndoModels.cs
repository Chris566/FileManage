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
}

/// <summary>撤销执行结果。</summary>
public sealed record UndoResult
{
    public required int Reverted { get; init; }

    public required int Skipped { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public bool Success => Errors.Count == 0;
}
