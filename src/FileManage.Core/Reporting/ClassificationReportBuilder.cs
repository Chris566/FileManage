using FileManage.Core.Execution;
using FileManage.Core.Planning;

namespace FileManage.Core.Reporting;

/// <summary>
/// 报表组装器：OperationPlan（预览=执行的一致性数据）+ 执行逐操作结果
/// → 报表行。包含所有命中分类规则的文件（成功、跳过、未执行均如实记录）。
/// 回滚场景下执行器不产生结果，此时不应生成报表（由调用方保证）。
/// </summary>
public static class ClassificationReportBuilder
{
    /// <summary>
    /// 从计划与执行结果组装报表行（仅含命中分类规则的条目，按计划顺序）。
    /// </summary>
    /// <param name="plan">操作计划。</param>
    /// <param name="results">执行逐操作结果（取消时可能少于计划操作数，缺失记为未执行）。</param>
    public static IReadOnlyList<ClassificationReportRow> Build(
        OperationPlan plan,
        IReadOnlyList<OperationResult> results)
    {
        var outcomeByOp = new Dictionary<Operation, OperationOutcome>(results.Count);

        foreach (var result in results)
        {
            outcomeByOp[result.Operation] = result.Outcome;
        }

        var rows = new List<ClassificationReportRow>();

        foreach (var entry in plan.Entries)
        {
            // 仅记录参与分类整理的文件（命中规则的条目，含被阻断的）
            if (entry.Classification is null)
            {
                continue;
            }

            var renameOutcome = entry.Rename is not null ? outcome(entry.Rename) : null;
            var transferOutcome = entry.Transfer is not null ? outcome(entry.Transfer) : null;

            rows.Add(new ClassificationReportRow
            {
                OriginalName = entry.Item.Name,
                OriginalPath = entry.Item.FullPath,
                NewName = NewNameOf(entry),
                NewPath = NewPathOf(entry),
                Category = entry.Classification.TargetSubfolder,
                Operation = OperationText(entry),
                Conflict = ConflictText(entry, renameOutcome, transferOutcome),
                RuleName = entry.Classification.Rule.Name
            });
        }

        return rows;

        OperationOutcome? outcome(Operation op) =>
            outcomeByOp.TryGetValue(op, out var value) ? value : OperationOutcome.NotExecuted;
    }

    private static string NewNameOf(PlanEntry entry)
    {
        return entry.Transfer switch
        {
            CopyOp c => c.TargetName,
            MoveOp m => m.TargetName,
            _ => entry.FinalName
        };
    }

    private static string NewPathOf(PlanEntry entry)
    {
        return entry.Transfer switch
        {
            CopyOp c => Path.Combine(c.TargetDir, c.TargetName),
            MoveOp m => Path.Combine(m.TargetDir, m.TargetName),
            _ => entry.Rename?.NewPath ?? entry.Item.FullPath
        };
    }

    private static string OperationText(PlanEntry entry)
    {
        var parts = new List<string>(2);

        if (entry.Rename is not null)
        {
            parts.Add("重命名");
        }

        switch (entry.Transfer)
        {
            case MoveOp:
                parts.Add("移动");
                break;
            case CopyOp:
                parts.Add("复制");
                break;
        }

        return string.Join("+", parts);
    }

    /// <summary>合并源目录重命名冲突与目标转移冲突，并附执行结果注记。</summary>
    private static string ConflictText(
        PlanEntry entry,
        OperationOutcome? renameOutcome,
        OperationOutcome? transferOutcome)
    {
        var renameText = TypeText(entry.ConflictType);
        var transferText = TypeText(entry.CopyConflictType);

        var conflict = (renameText.Length, transferText.Length) switch
        {
            (0, 0) => "无冲突",
            (0, _) => transferText,
            (_, 0) => renameText,
            _ => $"源目录：{renameText}；目标：{transferText}"
        };

        var notes = new List<string>(2);

        if (renameOutcome is OperationOutcome.Skipped)
        {
            notes.Add("重命名已跳过");
        }
        else if (renameOutcome is OperationOutcome.NotExecuted)
        {
            notes.Add("重命名未执行");
        }

        if (transferOutcome is OperationOutcome.Skipped)
        {
            notes.Add(entry.Transfer is CopyOp ? "复制已跳过" : "移动已跳过");
        }
        else if (transferOutcome is OperationOutcome.NotExecuted)
        {
            notes.Add(entry.Transfer is CopyOp ? "复制未执行" : "移动未执行");
        }

        return notes.Count > 0 ? $"{conflict}（{string.Join("；", notes)}）" : conflict;
    }

    private static string TypeText(ConflictType type)
    {
        return type switch
        {
            ConflictType.PlanDuplicate => "文件名冲突（已自动改号）",
            ConflictType.TargetExists => "文件已存在",
            ConflictType.PathTooLong => "路径过长（阻断）",
            ConflictType.InvalidChars => "名称含非法字符（阻断）",
            _ => ""
        };
    }
}
