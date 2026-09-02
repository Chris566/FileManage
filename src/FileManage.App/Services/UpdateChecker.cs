using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FileManage.App.Services;

/// <summary>
/// 远程更新检查器：调用 GitHub Releases API 检测最新版本。
/// API 端点：https://api.github.com/repos/Chris566/FileManage/releases/latest
/// </summary>
public static class UpdateChecker
{
    private const string RepoUrl = "https://api.github.com/repos/Chris566/FileManage/releases/latest";

    /// <summary>
    /// 检查最新 Release。返回 null 表示无可用更新或网络错误。
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileManage", VersionInfo.Version));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await http.GetAsync(RepoUrl);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var info = JsonSerializer.Deserialize<UpdateInfo>(json);

        if (info is null || string.IsNullOrEmpty(info.TagName))
        {
            return null;
        }

        return info;
    }

    /// <summary>
    /// 比较版本号：返回 true 表示 remote 比 local 新。
    /// 使用 Version 解析比较；无法解析时做字符串比较。
    /// </summary>
    public static bool IsNewer(string remoteVersion, string localVersion)
    {
        if (string.IsNullOrEmpty(remoteVersion) || string.IsNullOrEmpty(localVersion))
        {
            return false;
        }

        // 去掉 v 前缀和可能的 +sha 后缀
        remoteVersion = remoteVersion.TrimStart('v', 'V').Split('+')[0];
        localVersion = localVersion.TrimStart('v', 'V').Split('+')[0];

        if (Version.TryParse(remoteVersion, out var remote) &&
            Version.TryParse(localVersion, out var local))
        {
            return remote > local;
        }

        return !string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase);
    }
}
