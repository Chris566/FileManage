namespace FileManage.Core.Execution;

/// <summary>
/// 覆盖决策辅助（与执行器策略一致，供执行/文件回覆等场景复用）：
/// OverwriteAll → 覆盖；SkipAll → 跳过；Ask → 逐个询问（OverwriteAll/SkipAll 决策会收敛后续策略；
/// 无询问渠道时保守跳过）。
/// </summary>
public static class OverwriteDecider
{
    public static OverwriteDecision Decide(
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
}
