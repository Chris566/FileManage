namespace FileManage.Core.Rules;

/// <summary>
/// 规则预设管理器（纯内存状态机，零 IO）。
/// 所有变更产出新的 <see cref="RulePresetDocument"/>（record 不可变），调用方持久化时机自定。
/// 权限：IsBuiltIn 预设完全锁定 —— UpdateRules / RenamePreset / DeletePreset 一律拒绝。
/// </summary>
public sealed class RulePresetManager
{
    /// <summary>内置预设的存储名（UI 显示走本地化资源，不直接读取此名）。</summary>
    public const string BuiltInPresetName = "默认规则";

    private RulePresetDocument _document;

    public RulePresetManager(RulePresetDocument document)
    {
        _document = document.Presets.Count > 0 && document.Presets.Any(p => p.Id == document.ActivePresetId)
            ? document
            : document with { ActivePresetId = document.Presets[0].Id };
    }

    public RulePresetDocument Document => _document;

    public RulePreset ActivePreset => _document.Presets.First(p => p.Id == _document.ActivePresetId);

    /// <summary>当前生效的规则集（= 激活预设的规则）。</summary>
    public IReadOnlyList<ClassificationRule> ActiveRules => ActivePreset.Rules;

    /// <summary>创建系统默认预设（IsBuiltIn=true）。</summary>
    public static RulePreset CreateBuiltIn(IReadOnlyList<ClassificationRule> rules) => new()
    {
        Name = BuiltInPresetName,
        IsBuiltIn = true,
        Rules = rules
    };

    /// <summary>v1 → v2 迁移：现有规则集原样包装为系统默认预设（数据无损）。</summary>
    public static RulePresetDocument MigrateFromRules(IReadOnlyList<ClassificationRule> rules)
    {
        var preset = CreateBuiltIn(rules);
        return new RulePresetDocument { ActivePresetId = preset.Id, Presets = [preset] };
    }

    /// <summary>切换激活预设（切换即生效语义：调用方随后写盘）。</summary>
    public PresetResult SwitchPreset(Guid id)
    {
        if (_document.Presets.All(p => p.Id != id))
        {
            return PresetResult.Fail(PresetError.PresetNotFound);
        }

        if (id != _document.ActivePresetId)
        {
            _document = _document with { ActivePresetId = id };
        }

        return PresetResult.Ok();
    }

    /// <summary>创建自定义预设（重名拒绝；创建后自动激活，便于立即编辑）。</summary>
    public PresetResult CreatePreset(string name, IEnumerable<ClassificationRule>? rules = null)
    {
        var nameError = ValidateName(name);
        if (nameError != PresetError.None)
        {
            return PresetResult.Fail(nameError);
        }

        var preset = new RulePreset
        {
            Name = name.Trim(),
            IsBuiltIn = false,
            Rules = rules is null ? [] : [.. rules]
        };
        _document = _document with
        {
            ActivePresetId = preset.Id,
            Presets = [.. _document.Presets, preset]
        };
        return PresetResult.Ok();
    }

    /// <summary>复制预设：新 Id、IsBuiltIn=false、规则逐条复制（副本独立，改副本不影响源）。</summary>
    public PresetResult CopyPreset(Guid sourceId, string newName)
    {
        var source = _document.Presets.FirstOrDefault(p => p.Id == sourceId);
        if (source is null)
        {
            return PresetResult.Fail(PresetError.PresetNotFound);
        }

        var nameError = ValidateName(newName);
        if (nameError != PresetError.None)
        {
            return PresetResult.Fail(nameError);
        }

        var copy = new RulePreset
        {
            Name = newName.Trim(),
            IsBuiltIn = false,
            Rules = source.Rules.Select(r => r with { }).ToArray()
        };
        _document = _document with
        {
            ActivePresetId = copy.Id,
            Presets = [.. _document.Presets, copy]
        };
        return PresetResult.Ok();
    }

    public PresetResult RenamePreset(Guid id, string newName)
    {
        var preset = _document.Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null)
        {
            return PresetResult.Fail(PresetError.PresetNotFound);
        }

        if (preset.IsBuiltIn)
        {
            return PresetResult.Fail(PresetError.CannotModifyBuiltIn);
        }

        var nameError = ValidateName(newName, excludeId: id);
        if (nameError != PresetError.None)
        {
            return PresetResult.Fail(nameError);
        }

        _document = _document with
        {
            Presets = _document.Presets.Select(p => p.Id == id ? p with { Name = newName.Trim() } : p).ToArray()
        };
        return PresetResult.Ok();
    }

    /// <summary>删除预设：内置拒绝；删除激活项后激活落到剩余首个预设。</summary>
    public PresetResult DeletePreset(Guid id)
    {
        var preset = _document.Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null)
        {
            return PresetResult.Fail(PresetError.PresetNotFound);
        }

        if (preset.IsBuiltIn)
        {
            return PresetResult.Fail(PresetError.CannotModifyBuiltIn);
        }

        var remaining = _document.Presets.Where(p => p.Id != id).ToArray();
        _document = _document with
        {
            Presets = remaining,
            ActivePresetId = _document.ActivePresetId == id ? remaining[0].Id : _document.ActivePresetId
        };
        return PresetResult.Ok();
    }

    /// <summary>更新预设规则集（内置拒绝 —— 完全锁定）。</summary>
    public PresetResult UpdateRules(Guid id, IReadOnlyList<ClassificationRule> rules)
    {
        var preset = _document.Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null)
        {
            return PresetResult.Fail(PresetError.PresetNotFound);
        }

        if (preset.IsBuiltIn)
        {
            return PresetResult.Fail(PresetError.CannotModifyBuiltIn);
        }

        _document = _document with
        {
            Presets = _document.Presets.Select(p => p.Id == id ? p with { Rules = rules } : p).ToArray()
        };
        return PresetResult.Ok();
    }

    /// <summary>重名校验（与自身以外任意预设比较，忽略大小写/首尾空白）。</summary>
    private PresetError ValidateName(string name, Guid? excludeId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return PresetError.NameRequired;
        }

        var trimmed = name.Trim();
        return _document.Presets.Any(p => p.Id != excludeId && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ? PresetError.DuplicateName
            : PresetError.None;
    }
}
