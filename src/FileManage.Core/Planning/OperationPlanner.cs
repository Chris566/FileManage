using FileManage.Core.Naming;
using FileManage.Core.Rules;
using FileManage.Core.Scanning;

namespace FileManage.Core.Planning;

/// <summary>
/// 计划器选项：重命名与分类复制可独立启用。
/// </summary>
public sealed record PlannerOptions
{
    /// <summary>源目录（扫描起始目录）。</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>命名选项（null = 不重命名，仅分类）。</summary>
    public NamingOptions? Naming { get; init; }

    /// <summary>分类目标根目录（null = 不分类）。</summary>
    public string? CategoryTargetRoot { get; init; }

    public bool NamingEnabled => Naming is not null;

    public bool ClassificationEnabled => CategoryTargetRoot is not null;
}

/// <summary>
/// 操作计划器（设计文档 §3"计划-执行分离"的核心）：
/// Scanner 结果 + NameEngine + RuleEngine + ConflictDetector → 不可变 OperationPlan。
/// 预览所见（Entries）= 执行所得（Operations）= 撤销所逆（UndoBatch）。
/// </summary>
public sealed class OperationPlanner(
    NameEngine nameEngine,
    RuleEngine ruleEngine,
    ConflictDetector conflictDetector,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public OperationPlan Build(ScanResult scanResult, PlannerOptions options, CancellationToken ct = default)
    {
        var items = scanResult.Items;
        var itemByPath = items.ToDictionary(i => i.FullPath, StringComparer.Ordinal);

        // 1. 命名：RequestedName（未启用命名时 = 原名）
        var requestedNames = new string[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            requestedNames[i] = options.NamingEnabled
                ? nameEngine.BuildName(items[i], options.Naming!, i + 1)
                : items[i].Name;
        }

        // 2. 源目录内重命名冲突检测 → FinalName
        var renameCandidates = items
            .Select((item, i) => new RenameCandidate(item, requestedNames[i]));

        var renameReport = conflictDetector.Detect(renameCandidates, options.SourceDirectory, ct);
        var finalNames = renameReport.Items.ToDictionary(c => c.Item.FullPath, c => c.FinalName, StringComparer.Ordinal);
        var renameConflicts = renameReport.Items.ToDictionary(c => c.Item.FullPath, c => c.Type, StringComparer.Ordinal);

        // 3. 分类：命中规则后按目标子目录分组做冲突检测 → CopyFinalName
        var classifications = new Dictionary<string, ClassificationResult>(StringComparer.Ordinal);
        var copyNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var copyConflicts = new Dictionary<string, ConflictType>(StringComparer.Ordinal);

        if (options.ClassificationEnabled)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var classification = ruleEngine.Evaluate(items[i]);

                if (classification is not null)
                {
                    classifications[items[i].FullPath] = classification;
                }
            }

            foreach (var group in classifications.GroupBy(
                kvp => kvp.Value.TargetSubfolder,
                StringComparer.Ordinal))
            {
                var targetDir = Path.Combine(options.CategoryTargetRoot!, group.Key);

                var candidates = group
                    .Select(kvp => new RenameCandidate(itemByPath[kvp.Key], finalNames[kvp.Key]));

                var report = conflictDetector.Detect(candidates, targetDir, ct);

                foreach (var c in report.Items)
                {
                    copyNames[c.Item.FullPath] = c.FinalName;
                    copyConflicts[c.Item.FullPath] = c.Type;
                }
            }
        }

        // 4. 组装条目与操作
        var entries = new List<PlanEntry>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var item = items[i];
            var finalName = finalNames[item.FullPath];
            var conflictType = renameConflicts[item.FullPath];
            var renameBlocked = conflictType is ConflictType.PathTooLong or ConflictType.InvalidChars;

            RenameOp? rename = null;
            Operation? transfer = null;
            ClassificationResult? classification = null;
            string? copyFinalName = null;
            var copyConflict = ConflictType.None;

            if (options.NamingEnabled && !renameBlocked
                && !string.Equals(item.Name, finalName, StringComparison.Ordinal))
            {
                rename = new RenameOp(item.FullPath, finalName, Path.Combine(options.SourceDirectory, finalName));
            }

            if (options.ClassificationEnabled
                && classifications.TryGetValue(item.FullPath, out classification))
            {
                copyFinalName = copyNames[item.FullPath];
                copyConflict = copyConflicts[item.FullPath];

                if (copyConflict is not (ConflictType.PathTooLong or ConflictType.InvalidChars))
                {
                    var targetDir = Path.Combine(options.CategoryTargetRoot!, classification.TargetSubfolder);
                    transfer = classification.Rule.CopyInsteadOfMove
                        ? new CopyOp(item.FullPath, targetDir, copyFinalName)
                        : new MoveOp(item.FullPath, targetDir, copyFinalName);
                }
            }

            entries.Add(new PlanEntry
            {
                Item = item,
                RequestedName = requestedNames[i],
                FinalName = finalName,
                ConflictType = conflictType,
                Classification = classification,
                CopyFinalName = copyFinalName,
                CopyConflictType = copyConflict,
                Rename = rename,
                Transfer = transfer
            });
        }

        return new OperationPlan(Guid.NewGuid(), _timeProvider.GetLocalNow().DateTime, entries);
    }
}
