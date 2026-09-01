namespace FileManage.Core.Rules;

/// <summary>预设操作错误类型（文案由 UI 层本地化）。</summary>
public enum PresetError
{
    None,
    /// <summary>系统默认预设受保护，禁止修改/重命名/删除。</summary>
    CannotModifyBuiltIn,
    /// <summary>预设名称为空。</summary>
    NameRequired,
    /// <summary>已存在同名预设。</summary>
    DuplicateName,
    /// <summary>目标预设不存在。</summary>
    PresetNotFound
}

/// <summary>预设操作结果。</summary>
public readonly record struct PresetResult(bool Success, PresetError Error = PresetError.None)
{
    public static PresetResult Ok() => new(true);
    public static PresetResult Fail(PresetError error) => new(false, error);
}

/// <summary>
/// 规则预设（规则预设系统 v2）。IsBuiltIn=true 为系统默认预设：完全锁定（可查看/复制/导出，
/// 禁止编辑内容、重命名、删除）。持久化于 rules.json（Version=2）。
/// </summary>
public sealed record RulePreset
{
    /// <summary>预设唯一标识。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>预设名（UI 对内置预设以本地化资源显示，忽略此存储名）。</summary>
    public required string Name { get; init; }

    /// <summary>true = 系统默认预设（完全锁定）。</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>该预设包含的分类规则（与 v1 规则集同构）。</summary>
    public IReadOnlyList<ClassificationRule> Rules { get; init; } = [];
}

/// <summary>rules.json v2 文档：全部预设 + 激活项。切换即生效 = 修改 ActivePresetId 后整体写盘。</summary>
public sealed record RulePresetDocument
{
    /// <summary>当前文件格式版本。</summary>
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>当前生效（激活）的预设 Id。</summary>
    public Guid ActivePresetId { get; init; }

    public required IReadOnlyList<RulePreset> Presets { get; init; }
}
