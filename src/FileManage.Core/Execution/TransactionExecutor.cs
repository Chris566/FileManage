using FileManage.Core.Abstractions;
using FileManage.Core.Planning;
using FileManage.Core.Undo;

namespace FileManage.Core.Execution;

/// <summary>
/// 事务执行器（设计文档 §4.5）：
/// 阶段 A 备份（覆盖前按需快照）→ 阶段 B 逐条执行（进度/取消）→ 阶段 C 提交（生成撤销批次）/回滚（逆序恢复）。
/// 通过动态路径映射支持"重命名 → 复制改名后文件"等链式操作。
/// </summary>
public sealed class TransactionExecutor(
    IFileSystemService fileSystem,
    IFileBackupService backup,
    IUndoStore undoStore)
{
    /// <summary>单条已执行操作（用于回滚与撤销生成）。</summary>
    private sealed record ExecutedAction(
        Operation Kind,
        string ResolvedSourcePath,
        string ActualTargetPath,
        string? OverwrittenBackupPath);

    public Task<ExecutionReport> ExecuteAsync(
        OperationPlan plan,
        OverwritePolicy policy,
        IOverwriteResolver? resolver = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() => ExecuteCore(plan, policy, resolver, progress, ct), CancellationToken.None);
    }

    private ExecutionReport ExecuteCore(
        OperationPlan plan,
        OverwritePolicy policy,
        IOverwriteResolver? resolver,
        IProgress<ProgressInfo>? progress,
        CancellationToken ct)
    {
        var operations = plan.Operations;
        var currentPolicy = policy;
        var executed = new List<ExecutedAction>();
        var results = new List<OperationResult>(operations.Count);
        var currentPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<string>();
        var succeeded = 0;
        var skipped = 0;
        var rolledBack = false;
        var cancelled = false;

        try
        {
            for (var i = 0; i < operations.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var op = operations[i];
                progress?.Report(new ProgressInfo(operations.Count, i, Describe(op)));

                var sourcePath = ResolveSource(op, currentPaths);
                var (targetPath, targetDir) = ResolveTarget(op);

                // Copy/Move 目标目录可能不存在
                if (op is CopyOp or MoveOp)
                {
                    fileSystem.CreateDirectory(targetDir);
                }

                var overwrite = false;

                if (fileSystem.FileExists(targetPath))
                {
                    var decision = Decide(targetPath, resolver, ref currentPolicy);

                    if (decision is OverwriteDecision.Skip or OverwriteDecision.SkipAll)
                    {
                        results.Add(new OperationResult(op, sourcePath, targetPath, OperationOutcome.Skipped));
                        skipped++;
                        continue;
                    }

                    overwrite = true;
                }

                // 覆盖前先备份目标文件
                string? backupPath = null;

                if (overwrite)
                {
                    backupPath = backup.BackupFile(targetPath, plan.Id);
                }

                switch (op)
                {
                    case RenameOp r:
                        fileSystem.MoveFile(sourcePath, r.NewPath, overwrite);
                        break;
                    case CopyOp c:
                        fileSystem.CopyFile(sourcePath, targetPath, overwrite);
                        break;
                    case MoveOp m:
                        fileSystem.MoveFile(sourcePath, targetPath, overwrite);
                        break;
                }

                executed.Add(new ExecutedAction(op, sourcePath, targetPath, backupPath));
                results.Add(new OperationResult(op, sourcePath, targetPath, OperationOutcome.Succeeded));

                // Rename/Move 改变了源路径的实际位置（影响后续链式操作的源解析）
                if (op is RenameOp or MoveOp)
                {
                    currentPaths[op.SourcePath] = targetPath;
                }

                succeeded++;
            }
        }
        catch (OperationCanceledException)
        {
            // 用户取消：已完成部分保留，不回滚
            cancelled = true;
        }
        catch (Exception ex)
        {
            // 任意异常失败：逆序回滚全部已完成操作
            Rollback(executed, errors);
            rolledBack = true;
            errors.Add($"执行中止并已回滚: {ex.Message}");

            return new ExecutionReport
            {
                BatchId = plan.Id,
                Total = operations.Count,
                Succeeded = 0,
                Skipped = skipped,
                Failed = 1,
                Errors = errors,
                RolledBack = true,
                Cancelled = false,
                UndoFilePath = null
            };
        }

        // 提交：生成撤销批次（无实际操作时不生成）
        string? undoFilePath = null;

        if (executed.Count > 0)
        {
            var batch = BuildUndoBatch(plan, executed);
            undoFilePath = undoStore.Save(batch);
        }

        return new ExecutionReport
        {
            BatchId = plan.Id,
            Total = operations.Count,
            Succeeded = succeeded,
            Skipped = skipped,
            Failed = 0,
            Errors = errors,
            RolledBack = rolledBack,
            Cancelled = cancelled,
            UndoFilePath = undoFilePath,
            Results = results
        };
    }

    /// <summary>逆序回滚已执行操作，恢复被覆盖文件。</summary>
    private void Rollback(List<ExecutedAction> executed, List<string> errors)
    {
        for (var i = executed.Count - 1; i >= 0; i--)
        {
            var a = executed[i];

            try
            {
                switch (a.Kind)
                {
                    case RenameOp:
                        if (fileSystem.FileExists(a.ActualTargetPath))
                        {
                            fileSystem.MoveFile(a.ActualTargetPath, a.ResolvedSourcePath, overwrite: false);
                        }

                        break;

                    case CopyOp:
                        if (fileSystem.FileExists(a.ActualTargetPath))
                        {
                            fileSystem.DeleteFile(a.ActualTargetPath);
                        }

                        break;

                    case MoveOp:
                        if (fileSystem.FileExists(a.ActualTargetPath))
                        {
                            fileSystem.MoveFile(a.ActualTargetPath, a.ResolvedSourcePath, overwrite: true);
                        }

                        break;
                }

                if (a.OverwrittenBackupPath is not null)
                {
                    fileSystem.CopyFile(a.OverwrittenBackupPath, a.ActualTargetPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"回滚失败 [{a.ActualTargetPath}]: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 生成撤销批次。注意顺序约定：UndoOverwrite 排在对应操作 Undo **之前**，
    /// 逆序执行时操作撤销先发生、覆盖恢复最后发生（恢复被覆盖文件到目标位置）。
    /// </summary>
    private static UndoBatch BuildUndoBatch(OperationPlan plan, List<ExecutedAction> executed)
    {
        var actions = new List<UndoAction>(executed.Count);

        foreach (var a in executed)
        {
            if (a.OverwrittenBackupPath is not null)
            {
                actions.Add(new UndoOverwrite(a.ActualTargetPath, a.OverwrittenBackupPath));
            }

            switch (a.Kind)
            {
                case RenameOp:
                    actions.Add(new UndoRename(a.ActualTargetPath, a.ResolvedSourcePath));
                    break;
                case CopyOp:
                    // 覆盖场景下目标文件原本就存在，不能删除，仅恢复备份
                    if (a.OverwrittenBackupPath is null)
                    {
                        actions.Add(new UndoCopyCreated(a.ActualTargetPath));
                    }

                    break;
                case MoveOp:
                    actions.Add(new UndoMove(a.ActualTargetPath, a.ResolvedSourcePath));
                    break;
            }
        }

        return new UndoBatch(
            plan.Id,
            plan.CreatedAt,
            $"FileManage 批次（{plan.Entries.Count} 个文件）",
            actions);
    }

    private static string ResolveSource(Operation op, Dictionary<string, string> currentPaths)
    {
        return currentPaths.TryGetValue(op.SourcePath, out var current) ? current : op.SourcePath;
    }

    private static (string TargetPath, string TargetDir) ResolveTarget(Operation op)
    {
        return op switch
        {
            RenameOp r => (r.NewPath, Path.GetDirectoryName(r.NewPath)!),
            CopyOp c => (Path.Combine(c.TargetDir, c.TargetName), c.TargetDir),
            MoveOp m => (Path.Combine(m.TargetDir, m.TargetName), m.TargetDir),
            _ => throw new InvalidOperationException($"未知操作类型: {op.GetType().Name}")
        };
    }

    private OverwriteDecision Decide(
        string targetPath,
        IOverwriteResolver? resolver,
        ref OverwritePolicy currentPolicy)
    {
        switch (currentPolicy)
        {
            case OverwritePolicy.OverwriteAll:
                return OverwriteDecision.Overwrite;

            case OverwritePolicy.SkipAll:
                return OverwriteDecision.Skip;

            case OverwritePolicy.Ask:
                if (resolver is null)
                {
                    // 无询问渠道时保守跳过
                    return OverwriteDecision.Skip;
                }

                var decision = resolver.Resolve(targetPath);

                switch (decision)
                {
                    case OverwriteDecision.OverwriteAll:
                        currentPolicy = OverwritePolicy.OverwriteAll;
                        return OverwriteDecision.Overwrite;
                    case OverwriteDecision.SkipAll:
                        currentPolicy = OverwritePolicy.SkipAll;
                        return OverwriteDecision.Skip;
                    default:
                        return decision;
                }

            default:
                return OverwriteDecision.Skip;
        }
    }

    private static string Describe(Operation op)
    {
        return op switch
        {
            RenameOp r => $"重命名 {Path.GetFileName(r.SourcePath)} → {r.NewName}",
            CopyOp c => $"复制 {Path.GetFileName(c.SourcePath)} → {c.TargetName}",
            MoveOp m => $"移动 {Path.GetFileName(m.SourcePath)} → {m.TargetName}",
            _ => "处理中"
        };
    }
}
