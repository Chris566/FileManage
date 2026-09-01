using System.Reflection;

namespace FileManage.App.Services;

/// <summary>
/// 版本信息：显示版本取程序集 InformationalVersion（CI 发布时由 tag 注入，本地构建为 dev），
/// 构建日期取 AssemblyMetadata("BuildDate")（csproj 生成时以 UTC 时间戳写入）。
/// 状态栏版本标签与"关于"窗口共用此来源，保证版本一致。
/// </summary>
public static class VersionInfo
{
    public static string Version { get; } = LoadVersion();

    public static string BuildDate { get; } = LoadBuildDate();

    /// <summary>状态栏/关于窗口显示文本：正式版加 v 前缀（如 v1.5.0），本地构建显示 dev。</summary>
    public static string VersionText => Version == "dev" ? "dev" : "v" + Version;

    private static string LoadVersion()
    {
        var attr = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var v = attr?.InformationalVersion;
        if (string.IsNullOrEmpty(v))
        {
            return "dev";
        }

        // .NET SDK 会把源码修订（git 提交哈希）以 +sha 追加，显示时剥离
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    private static string LoadBuildDate()
    {
        var all = Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>();
        return all?.FirstOrDefault(m => m.Key == "BuildDate")?.Value ?? "";
    }
}
