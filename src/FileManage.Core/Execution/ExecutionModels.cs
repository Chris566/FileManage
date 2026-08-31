using FileManage.Core.Planning;

namespace FileManage.Core.Execution;

/// <summary>覆盖策略（对齐旧版"询问/全部覆盖/全部跳过"）。</summary>
public enum OverwritePolicy
{
    /// <summary>逐个询问（弹窗）。</summary>
    Ask,

    /// <summary>全部覆盖。</summary>
    OverwriteAll,

    /// <summary>全部跳过。</summary>
    SkipAll
}

/// <summary>单次覆盖决策。</summary>
public enum OverwriteDecision
{
    /// <summary>覆盖此文件。</summary>
    Overwrite,

    /// <summary>覆盖此文件及后续所有冲突。</summary>
    OverwriteAll,

    /// <summary>跳过此文件。</summary>
    Skip,

    /// <summary>跳过此文件及后续所有冲突。</summary>
    SkipAll
}

/// <summary>
/// 覆盖询问回调（M3 由 UI 弹窗实现，测试用 stub）。
/// </summary>
public interface IOverwriteResolver
{
    /// <summary>目标文件已存在时询问用户。</summary>
    OverwriteDecision Resolve(string targetFile);
}

/// <summary>执行进度。</summary>
public sealed record ProgressInfo(int Total, int Completed, string CurrentDescription);

/// <summary>单条操作的执行结果（供整理报表等精确记录使用）。</summary>
public enum OperationOutcome
{
    /// <summary>执行成功。</summary>
    Succeeded,

    /// <summary>目标已存在且按策略跳过。</summary>
    Skipped,

    /// <summary>未执行（用户取消后剩余的操作）。</summary>
    NotExecuted
}

/// <summary>单条操作的执行记录。</summary>
public sealed record OperationResult(
    Operation Operation,
    string ResolvedSourcePath,
    string TargetPath,
    OperationOutcome Outcome);

/// <summary>执行报告。</summary>
public sealed record ExecutionReport
{
    /// <summary>批次 ID（与 UndoBatch 一致）。</summary>
    public required Guid BatchId { get; init; }

    public required int Total { get; init; }

    public required int Succeeded { get; init; }

    public required int Skipped { get; init; }

    public required int Failed { get; init; }

    /// <summary>错误信息（回滚原因等）。</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>是否发生了回滚。</summary>
    public required bool RolledBack { get; init; }

    /// <summary>是否因用户取消而中止（已完成部分保留）。</summary>
    public required bool Cancelled { get; init; }

    /// <summary>撤销批次文件路径（无可撤销内容时为 null）。</summary>
    public string? UndoFilePath { get; init; }

    /// <summary>
    /// 逐操作执行结果（按执行顺序，与计划操作一一对应；取消时短于计划）。
    /// 回滚时为空（回滚后不应生成整理报表）。
    /// </summary>
    public IReadOnlyList<OperationResult> Results { get; init; } = [];
}
