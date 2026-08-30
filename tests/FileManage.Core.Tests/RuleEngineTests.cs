using FileManage.Core.Models;
using FileManage.Core.Rules;
using Xunit;

namespace FileManage.Core.Tests;

public class RuleEngineTests
{
    private readonly RuleEngine _engine = new(
        Rules(),
        new FakeTimeProvider(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)));

    private static ClassificationRule[] Rules()
    {
        return
        [
            new ClassificationRule
            {
                Name = "PDF", Priority = 1,
                TargetSubfolder = "PDF",
                Condition = new ExtensionIn(".pdf")
            },
            new ClassificationRule
            {
                Name = "WORD", Priority = 2,
                TargetSubfolder = "WORD",
                Condition = new ExtensionIn("doc", ".DOCX")
            },
            new ClassificationRule
            {
                Name = "大图片", Priority = 3,
                TargetSubfolder = "BIG/{ExifYear}",
                Condition = new AllOf(new ExtensionIn(".jpg", ".png"), new SizeBetween(1_000_000, null))
            },
            new ClassificationRule
            {
                Name = "2025年文档", Priority = 4,
                TargetSubfolder = "文档/{Date:yyyy}",
                Condition = new DateBetween(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31, 23, 59, 59))
            },
            new ClassificationRule
            {
                Name = "禁用规则", Priority = 0,
                TargetSubfolder = "DISABLED",
                Condition = new NameRegex(".*"),
                Enabled = false
            }
        ];
    }

    [Fact]
    public void Evaluate_ExtensionMatch_ReturnsFirstHitByPriority()
    {
        var result = _engine.Evaluate(TestHelper.Item("报告.PDF"));
        Assert.NotNull(result);
        Assert.Equal("PDF", result.Rule.Name);
        Assert.Equal("PDF", result.TargetSubfolder);
    }

    [Fact]
    public void Evaluate_ExtensionWithoutDotInput_Normalized()
    {
        var result = _engine.Evaluate(TestHelper.Item("doc.docx"));
        Assert.NotNull(result);
        Assert.Equal("WORD", result.Rule.Name);
    }

    [Fact]
    public void Evaluate_DisabledRule_Skipped()
    {
        // 禁用规则 Priority=0 且 NameRegex(".*") 恒命中，但应被跳过
        var result = _engine.Evaluate(TestHelper.Item("随便.xyz"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_AllOf_RequiresAllConditions()
    {
        // 1MB 以上的 jpg → 大图片；低于 1MB 不命中
        var big = _engine.Evaluate(TestHelper.Item("big.jpg", size: 2_000_000, exifDate: new DateTime(2024, 5, 1)));
        Assert.NotNull(big);
        Assert.Equal("BIG/2024", big.TargetSubfolder);

        var small = _engine.Evaluate(TestHelper.Item("small.jpg", size: 100));
        Assert.Null(small);
    }

    [Fact]
    public void Evaluate_DateBetween_InclusiveBounds()
    {
        var inRange = _engine.Evaluate(TestHelper.Item(
            "doc.dat",
            modifiedTime: new DateTime(2025, 6, 15)));
        Assert.NotNull(inRange);
        Assert.Equal("文档/2026", inRange.TargetSubfolder); // {Date:yyyy} 用当前时钟

        var outOfRange = _engine.Evaluate(TestHelper.Item(
            "doc.dat",
            modifiedTime: new DateTime(2026, 1, 1)));
        Assert.Null(outOfRange);
    }

    [Fact]
    public void Evaluate_CategoryVariable_RendersRuleName()
    {
        var rules = new ClassificationRule[]
        {
            new()
            {
                Name = "IMAGE", Priority = 1,
                TargetSubfolder = "照片/{Category}",
                Condition = new ExtensionIn(".jpg")
            }
        };
        var engine = new RuleEngine(rules);
        var result = engine.Evaluate(TestHelper.Item("a.jpg"));
        Assert.Equal("照片/IMAGE", result!.TargetSubfolder);
    }

    [Fact]
    public void Evaluate_EmptyRuleSet_ReturnsNull()
    {
        var engine = new RuleEngine([]);
        Assert.Null(engine.Evaluate(TestHelper.Item("a.pdf")));
    }
}
