using System.Text.RegularExpressions;

namespace FileManage.Core.Scanning;

/// <summary>
/// 轻量通配匹配器（支持 * 与 ?），Windows 文件名语义：不区分大小写。
/// </summary>
public static partial class WildcardMatcher
{
    [GeneratedRegex(@"[.*+?^${}()|[\]\\]", RegexOptions.Compiled)]
    private static partial Regex RegexEscapeRegex();

    /// <summary>判断文件名是否匹配任一通配模式。patterns 为空时视为通过（true）。</summary>
    public static bool IsMatchAny(string fileName, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (IsMatch(fileName, pattern))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>判断文件名是否匹配单个通配模式（* 任意串、? 单字符）。</summary>
    public static bool IsMatch(string fileName, string pattern)
    {
        var regexPattern = RegexEscapeRegex().Replace(pattern, m => m.Value switch
        {
            "*" => ".*",
            "?" => ".",
            _ => Regex.Escape(m.Value)
        });

        return Regex.IsMatch(fileName, $"^{regexPattern}$", RegexOptions.IgnoreCase);
    }
}
