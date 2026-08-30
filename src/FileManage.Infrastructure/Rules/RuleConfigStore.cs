using System.Text.Encodings.Web;
using System.Text.Json;
using FileManage.Core.Rules;

namespace FileManage.Infrastructure.Rules;

/// <summary>rules.json 文件模型（设计文档 §5）。</summary>
public sealed record RuleFile(int Version, IReadOnlyList<ClassificationRule> Rules);

/// <summary>
/// 分类规则 JSON 持久化：%AppData%/FileManage/rules.json（加载 / 保存 / 导入 / 导出共用）。
/// 加载失败（不存在或损坏）返回 null，由调用方回退到内置默认规则集。
/// </summary>
public sealed class RuleConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>加载规则文件；不存在或解析失败返回 null。</summary>
    public IReadOnlyList<ClassificationRule>? Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(filePath), SerializerOptions);
            return file?.Rules;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>保存规则集（含 UTF-8 BOM，兼容中文文件名与旧版习惯）。</summary>
    public void Save(string filePath, IReadOnlyList<ClassificationRule> rules)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(new RuleFile(1, rules), SerializerOptions);
        File.WriteAllText(filePath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
