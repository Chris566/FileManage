using FileManage.Core.Naming;
using Xunit;

namespace FileManage.Core.Tests;

public class NameEngineTests
{
    private readonly NameEngine _engine = new(new FakeTimeProvider(
        new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)));

    private static NamingOptions Options(
        string template,
        string prefix = "",
        IReadOnlyList<ReplaceStep>? replaceChain = null,
        bool keepOriginalExtension = true,
        int counterStart = 1)
    {
        return new NamingOptions
        {
            Template = template,
            Prefix = prefix,
            ReplaceChain = replaceChain ?? [],
            KeepOriginalExtension = keepOriginalExtension,
            CounterStart = counterStart
        };
    }

    // ---------- 模板变量 ----------

    [Theory]
    [InlineData("{Prefix}{OriginalName}", "合同_", 1, "合同_报告.pdf")]
    [InlineData("{Prefix}{BaseName}{Extension}", "合同_", 1, "合同_报告.pdf")]
    [InlineData("{BaseName}", "", 1, "报告.pdf")]        // keep=true 默认追加原后缀（旧版行为）
    [InlineData("{Extension}", "", 1, ".pdf")]
    [InlineData("{Index}", "", 12, "012.pdf")]
    [InlineData("{Counter:000}", "", 1, "001.pdf")]
    [InlineData("{Index}_{BaseName}", "IMG_", 2, "002_报告.pdf")]
    public void BuildName_BasicVariables(string template, string prefix, int index, string expected)
    {
        var result = _engine.BuildName(TestHelper.Item("报告.pdf"), Options(template, prefix), index);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildName_Counter_RespectsCounterStart()
    {
        var result = _engine.BuildName(
            TestHelper.Item("a.pdf"),
            Options("{Counter:000}", counterStart: 5),
            index: 1);
        Assert.Equal("005.pdf", result);
    }

    [Fact]
    public void BuildName_Date_UsesInjectedClock()
    {
        var result = _engine.BuildName(TestHelper.Item("a.pdf"), Options("{Date:yyyyMMdd}"), 1);
        Assert.Equal("20260830.pdf", result);
    }

    [Fact]
    public void BuildName_FileDate_UsesFileModifiedTime()
    {
        var result = _engine.BuildName(TestHelper.Item("a.pdf"), Options("{FileDate:yyyy-MM-dd}"), 1);
        Assert.Equal("2026-08-30.pdf", result);
    }

    [Fact]
    public void BuildName_ExifDate_FallsBackToModifiedTime()
    {
        var result = _engine.BuildName(
            TestHelper.Item("a.jpg", exifDate: null),
            Options("{ExifDate:yyyyMMdd}"), 1);
        Assert.Equal("20260830.jpg", result);
    }

    [Fact]
    public void BuildName_ExifDate_UsesExifWhenPresent()
    {
        var result = _engine.BuildName(
            TestHelper.Item("a.jpg", exifDate: new DateTime(2025, 1, 2)),
            Options("{ExifDate:yyyyMMdd}"), 1);
        Assert.Equal("20250102.jpg", result);
    }

    [Fact]
    public void BuildName_ParentDir_ExtractsFolderName()
    {
        var result = _engine.BuildName(
            TestHelper.Item("a.pdf", directory: @"D:\data\2025项目"),
            Options("{ParentDir}_{BaseName}"), 1);
        Assert.Equal("2025项目_a.pdf", result);
    }

    [Fact]
    public void BuildName_Hash8_RendersWhenPresent()
    {
        var result = _engine.BuildName(
            TestHelper.Item("a.pdf", hash8: "ab12cd34"),
            Options("{Hash8}_{BaseName}"), 1);
        Assert.Equal("ab12cd34_a.pdf", result);
    }

    [Fact]
    public void BuildName_Hash8_EmptyWhenMissing()
    {
        var result = _engine.BuildName(TestHelper.Item("a.pdf"), Options("{Hash8}_{BaseName}"), 1);
        Assert.Equal("_a.pdf", result);
    }

    [Fact]
    public void BuildName_Random_GeneratesRequestedLength()
    {
        var result = _engine.BuildName(TestHelper.Item("a.pdf"), Options("{Random:8}_{BaseName}"), 1);
        var random = result[..8];

        Assert.Equal(8, random.Length);
        Assert.EndsWith("_a.pdf", result);
        Assert.All(random, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    [Fact]
    public void BuildName_UnknownVariable_PreservedAsIs()
    {
        var result = _engine.BuildName(TestHelper.Item("a.pdf"), Options("{Unknown}_{BaseName}"), 1);
        Assert.Equal("{Unknown}_a.pdf", result);
    }

    // ---------- 替换链（模板渲染前应用于 BaseName）----------

    [Fact]
    public void ReplaceChain_LiteralReplace()
    {
        var result = _engine.BuildName(
            TestHelper.Item("旧报告.pdf"),
            Options("{BaseName}", replaceChain: [new LiteralReplace("旧", "新")]), 1);
        Assert.Equal("新报告.pdf", result);
    }

    [Fact]
    public void ReplaceChain_LiteralReplace_IgnoreCase()
    {
        var result = _engine.BuildName(
            TestHelper.Item("Photo-01.pdf"),
            Options("{BaseName}", replaceChain: [new LiteralReplace("photo", "IMG", IgnoreCase: true)]), 1);
        Assert.Equal("IMG-01.pdf", result);
    }

    [Fact]
    public void ReplaceChain_RegexReplace()
    {
        var result = _engine.BuildName(
            TestHelper.Item("版本2文件3.pdf"),
            Options("{BaseName}", replaceChain: [new RegexReplace(@"\d+", "#")]), 1);
        Assert.Equal("版本#文件#.pdf", result);
    }

    [Theory]
    [InlineData(CaseMode.Upper, "ABC 文件.pdf")]
    [InlineData(CaseMode.Lower, "abc 文件.pdf")]
    [InlineData(CaseMode.Title, "Abc 文件.pdf")]
    public void ReplaceChain_CaseTransform(CaseMode mode, string expected)
    {
        var result = _engine.BuildName(
            TestHelper.Item("abc 文件.pdf"),
            Options("{BaseName}", replaceChain: [new CaseTransform(mode)]), 1);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceChain_TrimSpaces_MergesAndTrims()
    {
        var result = _engine.BuildName(
            TestHelper.Item("  a   b  .pdf"),
            Options("{BaseName}", replaceChain: [new TrimSpacesStep()]), 1);
        Assert.Equal("a b.pdf", result);
    }

    [Fact]
    public void ReplaceChain_RemoveChars()
    {
        var result = _engine.BuildName(
            TestHelper.Item("xaybzc.pdf"),
            Options("{BaseName}", replaceChain: [new RemoveCharsStep("abc")]), 1);
        Assert.Equal("xyz.pdf", result);
    }

    [Fact]
    public void ReplaceChain_MultipleSteps_AppliedInOrder()
    {
        var result = _engine.BuildName(
            TestHelper.Item("  draft v2  .pdf"),
            Options("{BaseName}", replaceChain:
            [
                new TrimSpacesStep(),
                new RegexReplace(@"\s+", "_"),
                new LiteralReplace("draft", "final")
            ]), 1);
        Assert.Equal("final_v2.pdf", result);
    }

    // ---------- 后缀保持策略（对齐旧版）----------

    [Fact]
    public void KeepExtension_True_TemplateWithoutExtension_Appends()
    {
        var result = _engine.BuildName(TestHelper.Item("报告.pdf"), Options("{Prefix}{BaseName}"), 1);
        Assert.Equal("报告.pdf", result);
    }

    [Fact]
    public void KeepExtension_True_AlreadyEndsWith_DoesNotDuplicate()
    {
        var result = _engine.BuildName(TestHelper.Item("报告.pdf"), Options("{Prefix}{BaseName}{Extension}"), 1);
        Assert.Equal("报告.pdf", result);
    }

    [Fact]
    public void KeepExtension_False_StripsOriginalExtension()
    {
        var result = _engine.BuildName(
            TestHelper.Item("报告.pdf"),
            Options("{Prefix}{BaseName}{Extension}", keepOriginalExtension: false), 1);
        Assert.Equal("报告", result);
    }

    [Fact]
    public void KeepExtension_True_WrongExtension_StillAppendsOriginal()
    {
        var result = _engine.BuildName(TestHelper.Item("报告.pdf"), Options("{Prefix}{BaseName}.txt"), 1);
        Assert.Equal("报告.txt.pdf", result);
    }
}
