using System.Windows;
using FileManage.Infrastructure.Settings;

namespace FileManage.App.Services;

/// <summary>
/// 界面状态服务：主题（浅/深）与语言（中/英）运行时切换，settings.json 持久化。
/// 字典替换方式：移除旧字典后将新字典追加到 MergedDictionaries 末尾（后加载优先）。
/// </summary>
public static class UIStateService
{
    private static AppSettingsStore? _store;

    public static AppSettings Settings { get; private set; } = new();

    public static void Initialize(string appDataRoot)
    {
        _store = new AppSettingsStore(appDataRoot);
        Settings = _store.Load();
        ApplyTheme(Settings.Theme);
        ApplyLanguage(Settings.Language);
    }

    public static void ApplyTheme(string theme)
    {
        var uri = new Uri(theme.Equals("dark", StringComparison.OrdinalIgnoreCase)
            ? "Themes/Dark.xaml"
            : "Themes/Light.xaml", UriKind.Relative);

        ReplaceDictionary(uri, d => d.Source?.OriginalString.Contains("Themes/") == true
            && !d.Source.OriginalString.Contains("Controls.xaml"));

        // 已打开的窗口同步切换标题栏深浅
        var dark = theme.Equals("dark", StringComparison.OrdinalIgnoreCase);

        foreach (Window window in Application.Current.Windows)
        {
            DarkTitleBar.Apply(window, dark);
        }
    }

    /// <summary>窗口句柄创建后应用当前主题的标题栏（在各窗口 SourceInitialized 时调用）。</summary>
    public static void AttachTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) => DarkTitleBar.Apply(window);
    }

    public static void ApplyLanguage(string language)
    {
        var uri = new Uri(language.Equals("en-US", StringComparison.OrdinalIgnoreCase)
            ? "Localization/en-US.xaml"
            : "Localization/zh-CN.xaml", UriKind.Relative);

        ReplaceDictionary(uri, d => d.Source?.OriginalString.Contains("Localization/") == true);
    }

    private static void ReplaceDictionary(Uri uri, Func<ResourceDictionary, bool> isSameGroup)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            if (isSameGroup(dictionaries[i]))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = uri });
    }

    /// <summary>保存当前设置（主题/语言/上次目录）。</summary>
    public static void Save(AppSettings? settings = null)
    {
        if (_store is null)
        {
            return;
        }

        if (settings is not null)
        {
            Settings = settings;
        }

        _store.Save(Settings);
    }
}
