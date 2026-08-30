using System.Text.Json;
using FileManage.Core.Models;
using FileManage.Core.Naming;
using FileManage.Core.Rules;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// 黄金快照回归测试（设计文档 §8）：
/// 基线由 tools/generate-golden-snapshot.ps1 从旧版 FileRenameTool.ps1
/// 提取 Build-RenameName 等核心逻辑实际运行生成（432 命名用例 + 12 分类用例）。
/// 重新生成快照：powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-golden-snapshot.ps1
/// </summary>
public class LegacyGoldenSnapshotTests
{
    private static readonly string SnapshotPath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "legacy-golden-snapshot.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static GoldenSnapshot LoadSnapshot()
    {
        Assert.True(File.Exists(SnapshotPath), $"未找到黄金快照: {SnapshotPath}");
        return JsonSerializer.Deserialize<GoldenSnapshot>(File.ReadAllText(SnapshotPath), JsonOptions)!;
    }

    [Fact]
    public void NamingEngine_MatchesLegacyBuildRenameName_AllCases()
    {
        var snapshot = LoadSnapshot();
        Assert.True(snapshot.NamingCases.Count >= 400, "快照用例数量异常");

        var engine = new NameEngine();
        var failures = new List<string>();

        foreach (var (c, i) in snapshot.NamingCases.Select((c, i) => (c, i)))
        {
            var actual = engine.BuildName(
                TestHelper.Item(c.OldName),
                new NamingOptions
                {
                    Template = c.Template,
                    Prefix = c.Prefix,
                    KeepOriginalExtension = c.KeepOriginalExtension
                },
                c.Index);

            if (!string.Equals(actual, c.Expected, StringComparison.Ordinal))
            {
                failures.Add(
                    $"[{i}] {c.OldName} | {c.Template} | prefix=\"{c.Prefix}\" | idx={c.Index} | keep={c.KeepOriginalExtension}"
                    + $"\n    旧版期望: \"{c.Expected}\"  新版实际: \"{actual}\"");

                if (failures.Count >= 5)
                {
                    break;
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"与旧版 Build-RenameName 存在 {failures.Count}+ 处差异:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void RuleEngine_MatchesLegacyFileTypeGroups_AllCases()
    {
        var snapshot = LoadSnapshot();
        Assert.Equal(12, snapshot.ClassificationCases.Count);

        // 用旧版 FileTypeGroups 构造等价规则集，验证 RuleEngine 的匹配语义
        var legacyRules = new ClassificationRule[]
        {
            new() { Name = "PDF",   Priority = 1, TargetSubfolder = "PDF",   Condition = new ExtensionIn(".pdf") },
            new() { Name = "WORD",  Priority = 2, TargetSubfolder = "WORD",  Condition = new ExtensionIn(".doc", ".docx") },
            new() { Name = "EXCEL", Priority = 3, TargetSubfolder = "EXCEL", Condition = new ExtensionIn(".xls", ".xlsx") },
            new() { Name = "PPT",   Priority = 4, TargetSubfolder = "PPT",   Condition = new ExtensionIn(".ppt", ".pptx") },
            new() { Name = "IMAGE", Priority = 5, TargetSubfolder = "IMAGE", Condition = new ExtensionIn(".jpg", ".jpeg", ".png", ".tif", ".tiff") }
        };

        var engine = new RuleEngine(legacyRules);
        var failures = new List<string>();

        foreach (var c in snapshot.ClassificationCases)
        {
            var item = TestHelper.Item($"file{c.Extension}");
            var result = engine.Evaluate(item);

            var actual = result?.Rule.Name ?? "(未命中)";
            if (!string.Equals(actual, c.ExpectedCategory, StringComparison.Ordinal))
            {
                failures.Add($"{c.Extension}: 旧版期望 {c.ExpectedCategory}, 新版实际 {actual}");
            }
        }

        Assert.True(failures.Count == 0,
            "与旧版分类映射存在差异:\n" + string.Join("\n", failures));
    }
}

// ---------- 快照 JSON 模型 ----------

public sealed record GoldenSnapshot(
    string GeneratedAt,
    string Source,
    List<LegacyTemplateInfo> Templates,
    List<LegacyNamingCase> NamingCases,
    List<LegacyClassificationCase> ClassificationCases,
    List<string> KnownDifferences);

public sealed record LegacyTemplateInfo(string Name, string Template);

public sealed record LegacyNamingCase(
    string OldName,
    string Prefix,
    string Template,
    int Index,
    bool KeepOriginalExtension,
    string Expected);

public sealed record LegacyClassificationCase(string Extension, string ExpectedCategory);
