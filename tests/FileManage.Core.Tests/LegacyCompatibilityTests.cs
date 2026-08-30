using FileManage.Core.Naming;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// 旧版兼容回归测试：用例取自 FileRenameTool.ps1 V4.0.1 的 Build-RenameName
/// 与 $renameTemplateMap（docs/DESIGN.md §8 黄金用例基线的脚本内版本）。
/// 旧版模板预设：
///   "前缀 + 原文件名"       = "{Prefix}{OriginalName}"
///   "前缀 + 序号 + 原文件名" = "{Prefix}{Index}_{BaseName}"
///   "原文件名"              = "{BaseName}"
///   "序号 + 原文件名"        = "{Index}_{BaseName}"
/// </summary>
public class LegacyCompatibilityTests
{
    private readonly NameEngine _engine = new();

    private string Build(string name, string template, string prefix = "", int index = 1, bool keepExtension = true)
    {
        return _engine.BuildName(
            TestHelper.Item(name),
            new NamingOptions
            {
                Template = template,
                Prefix = prefix,
                KeepOriginalExtension = keepExtension
            },
            index);
    }

    [Theory]
    [InlineData("{Prefix}{OriginalName}", "合同_", 1, true, "合同_报告.pdf")]
    [InlineData("{Prefix}{OriginalName}", "", 1, true, "报告.pdf")]
    [InlineData("{Prefix}{Index}_{BaseName}", "合同_", 3, true, "合同_003_报告.pdf")]
    [InlineData("{BaseName}", "", 1, true, "报告.pdf")]
    [InlineData("{Index}_{BaseName}", "", 7, true, "007_报告.pdf")]
    public void LegacyTemplates_ProduceSameResultAsV401(
        string template, string prefix, int index, bool keepExtension, string expected)
    {
        var result = Build("报告.pdf", template, prefix, index, keepExtension);
        Assert.Equal(expected, result);
    }

    // 旧版 keepOriginalExtension=false：模板结果以原后缀结尾时截去
    [Fact]
    public void LegacyRemoveExtension_StripsWhenEndsWith()
    {
        Assert.Equal("IMG_002", Build("照片.jpg", "{Prefix}{Index}", "IMG_", 2, keepExtension: false));
    }

    [Fact]
    public void LegacyRemoveExtension_KeepsWhenNotEndsWith()
    {
        // 模板结果不以 .jpg 结尾时不截断
        Assert.Equal("IMG_002x", Build("照片.jpg", "{Prefix}{Index}x", "IMG_", 2, keepExtension: false));
    }

    // 旧版 keepOriginalExtension=true：模板无正确后缀时追加
    [Fact]
    public void LegacyKeepExtension_AppendsWhenMissing()
    {
        Assert.Equal("合同_报告.pdf", Build("报告.pdf", "{Prefix}{BaseName}", "合同_"));
    }

    // 无后缀文件
    [Fact]
    public void LegacyFileWithoutExtension_Works()
    {
        Assert.Equal("IMG_001", Build("README", "{Prefix}{Index}", "IMG_", 1, keepExtension: true));
        Assert.Equal("IMG_001", Build("README", "{Prefix}{Index}", "IMG_", 1, keepExtension: false));
    }
}
