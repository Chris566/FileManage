using FileManage.Core.Rules;
using FileManage.Infrastructure.Rules;

namespace FileManage.Core.Tests;

/// <summary>规则预设持久化测试：v1 迁移 / v2 往返 / 损坏回退 / 导入导出兼容。</summary>
public class RulePresetStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly RulePresetStore _store = new();

    public RulePresetStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fm-preset-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "rules.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 忽略 */ }
    }

    private static ClassificationRule Rule(string name, int priority = 1) => new()
    {
        Name = name,
        Priority = priority,
        TargetSubfolder = name,
        Condition = new ExtensionIn("." + name.ToLowerInvariant())
    };

    private static IReadOnlyList<ClassificationRule> Defaults() =>
    [
        Rule("图片"), Rule("PDF", 2), Rule("文档", 3)
    ];

    [Fact]
    public void LoadOrMigrate_NoFile_CreatesV2FromDefaults()
    {
        var doc = _store.LoadOrMigrate(_path, Defaults());

        var preset = Assert.Single(doc.Presets);
        Assert.True(preset.IsBuiltIn);
        Assert.Equal(3, preset.Rules.Count);
        Assert.True(File.Exists(_path), "首次加载应写盘");
        // 写盘后可再次加载为 v2
        Assert.NotNull(_store.Load(_path));
    }

    [Fact]
    public void LoadOrMigrate_V1File_MigratesLosslessAndWritesBackV2()
    {
        var v1Rules = new[] { Rule("自定义A"), Rule("自定义B", 2), Rule("自定义C", 3) };
        new RuleConfigStore().Save(_path, v1Rules);

        var doc = _store.LoadOrMigrate(_path, Defaults());

        // 现有配置 → 系统默认预设，规则逐条无损
        var preset = Assert.Single(doc.Presets);
        Assert.True(preset.IsBuiltIn);
        Assert.Equal(v1Rules.Select(r => r.Id), preset.Rules.Select(r => r.Id));
        Assert.Equal(v1Rules.Select(r => r.Name), preset.Rules.Select(r => r.Name));
        // 文件已升级为 v2
        Assert.Equal(2, _store.Load(_path)!.Version);
    }

    [Fact]
    public void LoadOrMigrate_V2File_LoadsDirectly()
    {
        var builtIn = RulePresetManager.CreateBuiltIn(Defaults());
        var custom = new RulePreset { Name = "旅行照片", Rules = [Rule("照片")] };
        var original = new RulePresetDocument
        {
            ActivePresetId = custom.Id,
            Presets = [builtIn, custom]
        };
        _store.Save(_path, original);

        var doc = _store.LoadOrMigrate(_path, Defaults());

        Assert.Equal(2, doc.Presets.Count);
        Assert.Equal(custom.Id, doc.ActivePresetId);
        Assert.Equal("旅行照片", doc.Presets.Single(p => p.Id == doc.ActivePresetId).Name);
    }

    [Fact]
    public void LoadOrMigrate_CorruptFile_FallsBackWithoutOverwrite()
    {
        File.WriteAllText(_path, "{ 这不是合法 JSON !!!");

        var before = File.ReadAllText(_path);
        var doc = _store.LoadOrMigrate(_path, Defaults());

        // 回退默认集但不覆盖坏文件（便于手工恢复）
        Assert.True(doc.Presets.Single(p => p.IsBuiltIn).Rules.Count == 3);
        Assert.Equal(before, File.ReadAllText(_path));
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesPresetsWithChineseNames()
    {
        var builtIn = RulePresetManager.CreateBuiltIn(Defaults());
        var custom = new RulePreset { Name = "照片归档（旅行）", Rules = [Rule("照片"), Rule("视频", 2)] };
        var original = new RulePresetDocument
        {
            ActivePresetId = builtIn.Id,
            Presets = [builtIn, custom]
        };

        _store.Save(_path, original);
        var loaded = _store.Load(_path);

        Assert.NotNull(loaded);
        Assert.Equal(original.ActivePresetId, loaded.ActivePresetId);
        Assert.Equal(original.Presets.Count, loaded.Presets.Count);
        Assert.Equal("照片归档（旅行）", loaded.Presets[1].Name);
        Assert.False(loaded.Presets[1].IsBuiltIn);
        Assert.Equal(original.Presets[1].Rules.Select(r => r.Id), loaded.Presets[1].Rules.Select(r => r.Id));
    }

    [Fact]
    public void LegacyImportExport_V1Format_CompatibleWithRuleConfigStore()
    {
        // 导出 v2 预设 → v1 单规则集 JSON；再用旧 RuleConfigStore 读回（旧版本兼容）
        var rules = new[] { Rule("图片"), Rule("文档", 2) };
        var exportPath = Path.Combine(_dir, "export.json");

        _store.SaveLegacyRules(exportPath, rules);
        var loadedByLegacyStore = new RuleConfigStore().Load(exportPath);

        Assert.NotNull(loadedByLegacyStore);
        Assert.Equal(rules.Select(r => r.Name), loadedByLegacyStore.Select(r => r.Name));

        // 反向：v1 JSON 可作为预设导入
        var imported = _store.LoadLegacyRules(exportPath);
        Assert.NotNull(imported);
        Assert.Equal(2, imported.Count);
    }
}
