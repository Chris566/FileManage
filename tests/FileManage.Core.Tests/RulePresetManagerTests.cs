using FileManage.Core.Rules;

namespace FileManage.Core.Tests;

/// <summary>规则预设管理器（纯逻辑）测试：权限保护 / CRUD / 切换 / 迁移 / 多预设一致性。</summary>
public class RulePresetManagerTests
{
    private static ClassificationRule Rule(string name, int priority = 1) => new()
    {
        Name = name,
        Priority = priority,
        TargetSubfolder = name,
        Condition = new ExtensionIn("." + name.ToLowerInvariant())
    };

    private static RulePresetDocument SampleDocument()
    {
        var builtIn = RulePresetManager.CreateBuiltIn([Rule("图片"), Rule("PDF", 2)]);
        var custom = new RulePreset { Name = "照片归档", Rules = [Rule("照片")] };
        return new RulePresetDocument { ActivePresetId = builtIn.Id, Presets = [builtIn, custom] };
    }

    // ---------- 迁移 ----------

    [Fact]
    public void MigrateFromRules_PreservesAllRules_AsBuiltIn()
    {
        var rules = new[] { Rule("图片"), Rule("PDF", 2), Rule("文档", 3) };
        var doc = RulePresetManager.MigrateFromRules(rules);

        Assert.Equal(RulePresetDocument.CurrentVersion, doc.Version);
        var preset = Assert.Single(doc.Presets);
        Assert.True(preset.IsBuiltIn);
        Assert.Equal(doc.ActivePresetId, preset.Id);
        Assert.Equal(3, preset.Rules.Count);
        // 规则内容逐条无损（含 Id）
        Assert.Equal(rules.Select(r => r.Id), preset.Rules.Select(r => r.Id));
        Assert.Equal(rules.Select(r => r.Name), preset.Rules.Select(r => r.Name));
    }

    [Fact]
    public void MigrateFromRules_EmptyRules_CreatesEmptyBuiltIn()
    {
        var doc = RulePresetManager.MigrateFromRules([]);

        var preset = Assert.Single(doc.Presets);
        Assert.True(preset.IsBuiltIn);
        Assert.Empty(preset.Rules);
    }

    // ---------- 构造与切换 ----------

    [Fact]
    public void Constructor_InvalidActiveId_FallsBackToFirstPreset()
    {
        var doc = SampleDocument();
        var broken = doc with { ActivePresetId = Guid.NewGuid() };

        var manager = new RulePresetManager(broken);

        Assert.Equal(doc.Presets[0].Id, manager.ActivePreset.Id);
    }

    [Fact]
    public void SwitchPreset_Valid_UpdatesActiveRules()
    {
        var manager = new RulePresetManager(SampleDocument());
        var custom = manager.Document.Presets.Single(p => !p.IsBuiltIn);

        var result = manager.SwitchPreset(custom.Id);

        Assert.True(result.Success);
        Assert.Equal(custom.Id, manager.ActivePreset.Id);
        Assert.Equal(custom.Rules, manager.ActiveRules);
    }

    [Fact]
    public void SwitchPreset_UnknownId_Fails()
    {
        var manager = new RulePresetManager(SampleDocument());

        var result = manager.SwitchPreset(Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(PresetError.PresetNotFound, result.Error);
    }

    // ---------- 创建 / 复制 ----------

    [Fact]
    public void CreatePreset_AddsCustomAndActivates()
    {
        var manager = new RulePresetManager(SampleDocument());

        var result = manager.CreatePreset("办公文档", [Rule("文档")]);

        Assert.True(result.Success);
        Assert.Equal(3, manager.Document.Presets.Count);
        Assert.False(manager.ActivePreset.IsBuiltIn);
        Assert.Equal("办公文档", manager.ActivePreset.Name);
        Assert.Single(manager.ActiveRules);
    }

    [Fact]
    public void CreatePreset_DuplicateName_Fails()
    {
        var manager = new RulePresetManager(SampleDocument());

        var result = manager.CreatePreset("照片归档");

        Assert.False(result.Success);
        Assert.Equal(PresetError.DuplicateName, result.Error);
    }

    [Fact]
    public void CreatePreset_BlankName_Fails()
    {
        var manager = new RulePresetManager(SampleDocument());

        var result = manager.CreatePreset("   ");

        Assert.False(result.Success);
        Assert.Equal(PresetError.NameRequired, result.Error);
    }

    [Fact]
    public void CopyPreset_IndependentCopy_WithNewId()
    {
        var manager = new RulePresetManager(SampleDocument());
        var source = manager.Document.Presets.Single(p => p.IsBuiltIn);

        var result = manager.CopyPreset(source.Id, "默认规则副本");

        Assert.True(result.Success);
        var copy = manager.ActivePreset;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.False(copy.IsBuiltIn);
        Assert.Equal("默认规则副本", copy.Name);
        Assert.Equal(source.Rules.Count, copy.Rules.Count);
        // 副本与源规则内容一致（record 不可变，共享条件树安全）
        Assert.Equal(source.Rules.Select(r => r.Id), copy.Rules.Select(r => r.Id));
    }

    // ---------- 重命名 / 更新 / 删除 权限 ----------

    [Fact]
    public void RenamePreset_BuiltIn_Fails()
    {
        var manager = new RulePresetManager(SampleDocument());
        var builtIn = manager.Document.Presets.Single(p => p.IsBuiltIn);

        var result = manager.RenamePreset(builtIn.Id, "新名字");

        Assert.False(result.Success);
        Assert.Equal(PresetError.CannotModifyBuiltIn, result.Error);
    }

    [Fact]
    public void RenamePreset_Custom_Succeeds()
    {
        var manager = new RulePresetManager(SampleDocument());
        var custom = manager.Document.Presets.Single(p => !p.IsBuiltIn);

        var result = manager.RenamePreset(custom.Id, "旅行照片");

        Assert.True(result.Success);
        Assert.Equal("旅行照片", manager.Document.Presets.Single(p => p.Id == custom.Id).Name);
    }

    [Fact]
    public void DeletePreset_BuiltIn_Fails()
    {
        var manager = new RulePresetManager(SampleDocument());
        var builtIn = manager.Document.Presets.Single(p => p.IsBuiltIn);

        var result = manager.DeletePreset(builtIn.Id);

        Assert.False(result.Success);
        Assert.Equal(PresetError.CannotModifyBuiltIn, result.Error);
        Assert.Equal(2, manager.Document.Presets.Count);
    }

    [Fact]
    public void DeletePreset_ActiveCustom_ReactivatesFirstRemaining()
    {
        var manager = new RulePresetManager(SampleDocument());
        var custom = manager.Document.Presets.Single(p => !p.IsBuiltIn);
        manager.SwitchPreset(custom.Id);

        var result = manager.DeletePreset(custom.Id);

        Assert.True(result.Success);
        var preset = Assert.Single(manager.Document.Presets);
        Assert.True(preset.IsBuiltIn);
        Assert.Equal(preset.Id, manager.ActivePreset.Id);
    }

    [Fact]
    public void UpdateRules_BuiltIn_Fails_CompletelyLocked()
    {
        var manager = new RulePresetManager(SampleDocument());
        var builtIn = manager.Document.Presets.Single(p => p.IsBuiltIn);

        var result = manager.UpdateRules(builtIn.Id, [Rule("新规则")]);

        Assert.False(result.Success);
        Assert.Equal(PresetError.CannotModifyBuiltIn, result.Error);
        // 原规则未被改动
        Assert.Equal(2, manager.Document.Presets.Single(p => p.IsBuiltIn).Rules.Count);
    }

    [Fact]
    public void UpdateRules_Custom_Succeeds()
    {
        var manager = new RulePresetManager(SampleDocument());
        var custom = manager.Document.Presets.Single(p => !p.IsBuiltIn);
        var newRules = new[] { Rule("照片"), Rule("视频", 2) };

        var result = manager.UpdateRules(custom.Id, newRules);

        Assert.True(result.Success);
        Assert.Equal(2, manager.Document.Presets.Single(p => p.Id == custom.Id).Rules.Count);
    }

    // ---------- 多预设数据一致性 ----------

    [Fact]
    public void MultiPresets_SwitchingBackAndForth_RulesRemainIndependent()
    {
        var manager = new RulePresetManager(SampleDocument());
        var builtIn = manager.Document.Presets.Single(p => p.IsBuiltIn);
        var custom = manager.Document.Presets.Single(p => !p.IsBuiltIn);

        // 修改自定义预设 → 切回内置 → 再切回自定义
        manager.UpdateRules(custom.Id, [Rule("照片"), Rule("视频", 2), Rule("音频", 3)]);
        manager.SwitchPreset(builtIn.Id);
        Assert.Equal(2, manager.ActiveRules.Count); // 内置不受影响
        manager.SwitchPreset(custom.Id);
        Assert.Equal(3, manager.ActiveRules.Count); // 自定义修改保留

        // 内置预设内容始终不变
        Assert.Equal([.. new[] { "图片", "PDF" }], manager.Document.Presets.Single(p => p.IsBuiltIn).Rules.Select(r => r.Name));
    }
}
