using FileManage.Core.Rules;
using FileManage.Infrastructure.Rules;

namespace FileManage.Core.Tests;

/// <summary>
/// rules.json 持久化测试：多态条件序列化往返 + 损坏文件回退。
/// </summary>
public class RuleConfigStoreTests
{
    private static RuleConfigStore CreateStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileManageRuleTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new RuleConfigStore();
    }

    private static string TempPath(string fileName)
    {
        return Path.Combine(Path.GetTempPath(),
            "FileManageRuleTests_" + Guid.NewGuid().ToString("N"), fileName);
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesAllConditionTypes()
    {
        var store = new RuleConfigStore();
        var path = TempPath("rules.json");

        var rules = new List<ClassificationRule>
        {
            new()
            {
                Name = "PDF", Priority = 1, TargetSubfolder = "PDF",
                Condition = new ExtensionIn(".pdf", ".PDF")
            },
            new()
            {
                Name = "截图", Priority = 2, CopyInsteadOfMove = false,
                TargetSubfolder = "截图/{ExifYear}",
                Condition = new AllOf(
                    new ExtensionIn(".png"),
                    new NameRegex("^screenshot.*$"),
                    new SizeBetween(1024, 10_000_000),
                    new DateBetween(new DateTime(2025, 1, 1), new DateTime(2026, 12, 31)))
            },
            new()
            {
                Name = "大文件", Priority = 3, Enabled = false,
                TargetSubfolder = "大文件",
                Condition = new SizeBetween(null, 1_000_000_000)
            }
        };

        var ids = rules.Select(r => r.Id).ToArray();
        store.Save(path, rules);
        var loaded = store.Load(path)!;

        Assert.Equal(3, loaded.Count);

        // 扩展名条件
        var ext = Assert.IsType<ExtensionIn>(loaded[0].Condition);
        Assert.Equal([".pdf", ".PDF"], ext.Exts);
        Assert.Equal(ids[0], loaded[0].Id);

        // AllOf 嵌套四类条件
        var all = Assert.IsType<AllOf>(loaded[1].Condition);
        Assert.IsType<ExtensionIn>(all.Conditions[0]);
        Assert.IsType<NameRegex>(all.Conditions[1]);
        Assert.IsType<SizeBetween>(all.Conditions[2]);
        Assert.IsType<DateBetween>(all.Conditions[3]);
        Assert.Equal("^screenshot.*$", ((NameRegex)all.Conditions[1]).Pattern);
        Assert.False(loaded[1].CopyInsteadOfMove);

        // disabled + 开区间
        Assert.False(loaded[2].Enabled);
        Assert.Null(((SizeBetween)loaded[2].Condition).Min);
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        var store = new RuleConfigStore();
        Assert.Null(store.Load(TempPath("not-exist.json")));
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsNull()
    {
        var store = new RuleConfigStore();
        var path = TempPath("corrupt.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ 不是合法 JSON ");

        Assert.Null(store.Load(path));
    }

    [Fact]
    public void Save_CreatesParentDirectory_AndWritesBomForChinese()
    {
        var store = new RuleConfigStore();
        var path = TempPath("中文目录/rules.json");

        store.Save(path, [new ClassificationRule { Name = "图片", Priority = 1, TargetSubfolder = "图片", Condition = new ExtensionIn(".jpg") }]);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);

        // UTF-8 BOM
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3].ToArray());

        // 中文名未转义（可直接阅读）
        Assert.Contains("\"图片\"", File.ReadAllText(path));
    }
}
