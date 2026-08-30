using System.Globalization;
using System.Text.RegularExpressions;
using FileManage.Core.Models;

namespace FileManage.Core.Rules;

/// <summary>
/// 分类规则引擎（设计文档 §4.3）：按优先级评估规则，返回首个命中结果。
/// </summary>
public sealed partial class RuleEngine(IReadOnlyList<ClassificationRule> rules, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>评估单个文件。未命中返回 null（不处理）。</summary>
    public ClassificationResult? Evaluate(FileItem file)
    {
        foreach (var rule in rules
            .Where(r => r.Enabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name, StringComparer.Ordinal))
        {
            if (!Matches(rule.Condition, file))
            {
                continue;
            }

            return new ClassificationResult(rule, RenderTargetSubfolder(rule, file));
        }

        return null;
    }

    private string RenderTargetSubfolder(ClassificationRule rule, FileItem file)
    {
        return SubfolderVariableRegex().Replace(rule.TargetSubfolder, match =>
        {
            return match.Groups["name"].Value switch
            {
                "Category" => rule.Name,
                "Date" => GetLocalNow().ToString(
                    match.Groups["arg"].Success ? match.Groups["arg"].Value : "yyyy",
                    CultureInfo.InvariantCulture),
                "ExifYear" => (file.ExifDate ?? file.ModifiedTime).Year.ToString(CultureInfo.InvariantCulture),
                _ => match.Value
            };
        });
    }

    private static bool Matches(MatchCondition condition, FileItem file)
    {
        return condition switch
        {
            ExtensionIn c => c.NormalizedExtensions.Contains(file.Extension, StringComparer.Ordinal),
            NameRegex c => Regex.IsMatch(file.Name, c.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
            SizeBetween c => (!c.Min.HasValue || file.SizeBytes >= c.Min.Value)
                          && (!c.Max.HasValue || file.SizeBytes <= c.Max.Value),
            DateBetween c => (!c.From.HasValue || file.ModifiedTime >= c.From.Value)
                          && (!c.To.HasValue || file.ModifiedTime <= c.To.Value),
            AllOf c => c.Conditions.All(sub => Matches(sub, file)),
            _ => false
        };
    }

    private DateTime GetLocalNow()
    {
        return _timeProvider.GetLocalNow().DateTime;
    }

    [GeneratedRegex(@"\{(?<name>Category|Date|ExifYear)(?::(?<arg>[^}]+))?\}", RegexOptions.Compiled)]
    private static partial Regex SubfolderVariableRegex();
}
