namespace FileManage.Core.Rules;

/// <summary>
/// 分类规则（设计文档 §4.3）。持久化于 rules.json，可导入导出。
/// </summary>
public sealed record ClassificationRule
{
    /// <summary>规则唯一标识。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>规则名（也是 {Category} 变量的值，如 "PDF"）。</summary>
    public required string Name { get; init; }

    /// <summary>优先级，数字越小越先评估；同优先级按列表顺序。</summary>
    public int Priority { get; init; }

    /// <summary>是否启用。禁用规则直接跳过。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>true = 复制到目标（对齐旧版行为）；false = 移动。</summary>
    public bool CopyInsteadOfMove { get; init; } = true;

    /// <summary>目标子目录模板，支持变量：{Category}、{Date:格式}、{ExifYear}。</summary>
    public required string TargetSubfolder { get; init; }

    /// <summary>匹配条件。</summary>
    public required MatchCondition Condition { get; init; }
}

/// <summary>规则命中结果。</summary>
public sealed record ClassificationResult(ClassificationRule Rule, string TargetSubfolder);
