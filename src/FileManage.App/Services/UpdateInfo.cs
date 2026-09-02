using System.Text.Json.Serialization;

namespace FileManage.App.Services;

/// <summary>
/// GitHub Release 信息（仅提取更新检查所需字段）。
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>Release 标签名（如 v1.7.2）。</summary>
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    /// <summary>Release 名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Release 正文（Markdown，用作更新日志）。</summary>
    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    /// <summary>发布时间（ISO 8601）。</summary>
    [JsonPropertyName("published_at")]
    public string PublishedAt { get; set; } = "";

    /// <summary>Release 资产列表（包含可下载的 exe）。</summary>
    [JsonPropertyName("assets")]
    public List<UpdateAsset> Assets { get; set; } = [];

    /// <summary>从 TagName 提取的纯版本号（去 v 前缀）。</summary>
    public string Version => TagName.TrimStart('v', 'V');

    /// <summary>HTML 页面 URL（供用户手动下载）。</summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
}

/// <summary>
/// GitHub Release 资产（单个文件）。
/// </summary>
public sealed class UpdateAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; } = 0;
}
