using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FileManage.Core.Rules;

namespace FileManage.Infrastructure.Rules;

/// <summary>
/// 规则预设持久化（rules.json v2）：加载 / 保存 / v1 迁移 / 导入导出（v1 单规则集格式）。
/// </summary>
public sealed class RulePresetStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly RuleConfigStore _legacyStore = new();

    /// <summary>
    /// 加载 rules.json 并按需迁移，返回可用文档：
    /// v2 格式 → 直接加载；v1 格式 → 迁移为系统默认预设并写回 v2；不存在 → 由默认集创建并写盘；
    /// 损坏 → 回退默认集（不覆盖坏文件，便于手工恢复）。
    /// </summary>
    public RulePresetDocument LoadOrMigrate(string filePath, IReadOnlyList<ClassificationRule> fallbackDefaults)
    {
        var v2 = Load(filePath);
        if (v2 is not null)
        {
            return v2;
        }

        if (!File.Exists(filePath))
        {
            var created = RulePresetManager.MigrateFromRules(fallbackDefaults);
            Save(filePath, created);
            return created;
        }

        // 文件存在但非 v2：尝试 v1 迁移（现有用户规则 → 系统默认预设，无损）
        var legacy = _legacyStore.Load(filePath);
        if (legacy is { Count: > 0 })
        {
            var migrated = RulePresetManager.MigrateFromRules(legacy);
            Save(filePath, migrated);
            return migrated;
        }

        // 损坏：回退默认集，不写盘
        return RulePresetManager.MigrateFromRules(fallbackDefaults);
    }

    /// <summary>加载 v2 文档；不存在 / 版本不符 / 预设为空 / 激活项无效 / 解析失败返回 null。</summary>
    public RulePresetDocument? Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<RulePresetDocument>(File.ReadAllText(filePath), SerializerOptions);
            return doc is { Version: RulePresetDocument.CurrentVersion, Presets.Count: > 0 }
                && doc.Presets.Any(p => p.Id == doc.ActivePresetId)
                    ? doc
                    : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>保存 v2 文档（含 UTF-8 BOM，兼容中文文件名与旧版习惯）。</summary>
    public void Save(string filePath, RulePresetDocument document)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        File.WriteAllText(filePath, json, Utf8WithBom);
    }

    /// <summary>导入用：读取 v1 单规则集 JSON。</summary>
    public IReadOnlyList<ClassificationRule>? LoadLegacyRules(string filePath) => _legacyStore.Load(filePath);

    /// <summary>导出用：写出 v1 单规则集 JSON（旧版本程序可读）。</summary>
    public void SaveLegacyRules(string filePath, IReadOnlyList<ClassificationRule> rules)
        => _legacyStore.Save(filePath, rules);
}
