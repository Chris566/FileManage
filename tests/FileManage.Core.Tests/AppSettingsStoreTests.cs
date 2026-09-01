using FileManage.Infrastructure.Settings;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// M5 界面记忆：settings.json 新增分组折叠与窗口状态字段。
/// 重点回归向后兼容：旧格式（缺失新字段）必须无损加载并取默认值。
/// </summary>
public class AppSettingsStoreTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettingsStore _store;

    public AppSettingsStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fm-settings-tests", Guid.NewGuid().ToString("N"));
        _store = new AppSettingsStore(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Load_LegacyJson_MissingNewFields_TakesDefaults()
    {
        Directory.CreateDirectory(_root);
        // v1.4.x 格式：仅 5 个旧字段
        var legacy = """
            {
              "Theme": "dark",
              "Language": "en-US",
              "LastSourceDirectory": "E:\\1",
              "LastTargetDirectory": "E:\\2",
              "GenerateClassificationReport": true
            }
            """;
        File.WriteAllText(_store.SettingsFilePath, legacy);

        var s = _store.Load();

        Assert.Equal("dark", s.Theme);
        Assert.Equal("en-US", s.Language);
        Assert.True(s.GenerateClassificationReport);
        // 新字段默认值：分组全部展开、窗口状态为空
        Assert.True(s.SourceGroupExpanded);
        Assert.True(s.RenameGroupExpanded);
        Assert.True(s.ClassifyGroupExpanded);
        Assert.True(s.ExecOptionsGroupExpanded);
        Assert.Null(s.WindowX);
        Assert.Null(s.WindowY);
        Assert.Null(s.WindowWidth);
        Assert.Null(s.WindowHeight);
        Assert.False(s.WindowMaximized);
    }

    [Fact]
    public void Save_Then_Load_RoundTripsNewFields()
    {
        var original = new AppSettings
        {
            Theme = "dark",
            Language = "zh-CN",
            LastSourceDirectory = "C:\\a",
            LastTargetDirectory = "C:\\b",
            GenerateClassificationReport = false,
            SourceGroupExpanded = false,
            RenameGroupExpanded = true,
            ClassifyGroupExpanded = false,
            ExecOptionsGroupExpanded = true,
            WindowMaximized = true,
            WindowX = -1920,
            WindowY = 0,
            WindowWidth = 1280,
            WindowHeight = 1024
        };

        _store.Save(original);
        var loaded = _store.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_store.SettingsFilePath, "{ not valid json !!!");

        var s = _store.Load();

        Assert.Equal("light", s.Theme);
        Assert.True(s.SourceGroupExpanded);
        Assert.Null(s.WindowWidth);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = _store.Load();

        Assert.Equal("light", s.Theme);
        Assert.True(s.ExecOptionsGroupExpanded);
        Assert.False(s.WindowMaximized);
    }
}
