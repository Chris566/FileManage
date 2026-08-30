using System.Globalization;
using System.Windows;

namespace FileManage.App.Services;

/// <summary>
/// 从当前合并的语言资源字典取字符串（键约定 S.*）。
/// 切换语言时 UIStateService 会整体替换字典，此后调用即返回新语言文本。
/// </summary>
public static class Localize
{
    public static string T(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);
}
