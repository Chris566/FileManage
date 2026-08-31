using System.Text.Json;

namespace FileManage.Infrastructure.Settings;

/// <summary>
/// 应用设置（%AppData%/FileManage/settings.json）：主题、语言与上次使用的目录。
/// </summary>
public sealed record AppSettings
{
    /// <summary>主题："light" / "dark"。</summary>
    public string Theme { get; init; } = "light";

    /// <summary>语言："zh-CN" / "en-US"。</summary>
    public string Language { get; init; } = "zh-CN";

    public string LastSourceDirectory { get; init; } = "";

    public string LastTargetDirectory { get; init; } = "";

    /// <summary>分类整理完成后是否自动生成报表（写入目标目录）。</summary>
    public bool GenerateClassificationReport { get; init; }
}

/// <summary>settings.json 读写；任何异常返回默认设置（首次运行/文件损坏均可安全启动）。</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string SettingsFilePath { get; }

    public AppSettingsStore(string appDataRoot)
    {
        SettingsFilePath = Path.Combine(appDataRoot, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath), SerializerOptions)
                   ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, SerializerOptions));
    }
}
