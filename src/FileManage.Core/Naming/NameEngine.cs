using System.Globalization;
using System.Text.RegularExpressions;
using FileManage.Core.Models;

namespace FileManage.Core.Naming;

/// <summary>
/// 命名引擎（设计文档 §4.2）：两段式"替换链 → 模板渲染"。
/// 纯逻辑无 IO，可完整单元测试。
/// </summary>
public sealed partial class NameEngine(TimeProvider? timeProvider = null)
{
    private static readonly string[] RandomChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray().Select(c => c.ToString()).ToArray();

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// 为单个文件生成新文件名。
    /// </summary>
    /// <param name="file">文件条目。</param>
    /// <param name="options">命名选项。</param>
    /// <param name="index">1 起始的文件序号（序号变量 = CounterStart + index - 1）。</param>
    public string BuildName(FileItem file, NamingOptions options, int index)
    {
        var extension = Path.GetExtension(file.Name);
        var baseName = Path.GetFileNameWithoutExtension(file.Name);
        var middleName = ApplyReplaceChain(baseName, options.ReplaceChain);
        var counterValue = options.CounterStart + index - 1;

        var newName = TemplateRegex().Replace(options.Template, match =>
        {
            var name = match.Groups["name"].Value;
            var arg = match.Groups["arg"].Success ? match.Groups["arg"].Value : null;

            return name switch
            {
                "Prefix" => options.Prefix,
                "OriginalName" => file.Name,
                "BaseName" => middleName,
                "Extension" => extension,
                "ParentDir" => GetParentDirectoryName(file.FullPath),
                "Hash8" => file.ContentHash8 ?? "",
                "Index" => counterValue.ToString("D3", CultureInfo.InvariantCulture),
                "Counter" => counterValue.ToString(arg ?? "D3", CultureInfo.InvariantCulture),
                "Date" => GetLocalNow().ToString(arg ?? "yyyyMMdd", CultureInfo.InvariantCulture),
                "FileDate" => file.ModifiedTime.ToString(arg ?? "yyyyMMdd", CultureInfo.InvariantCulture),
                "ExifDate" => (file.ExifDate ?? file.ModifiedTime).ToString(arg ?? "yyyyMMdd", CultureInfo.InvariantCulture),
                "Random" => GenerateRandom(ParseRandomLength(arg)),
                _ => match.Value // 未知变量保留原样，便于用户发现拼写问题
            };
        });

        return ApplyExtensionPolicy(newName, extension, options.KeepOriginalExtension);
    }

    /// <summary>按顺序对 BaseName 应用替换链。</summary>
    private static string ApplyReplaceChain(string input, IReadOnlyList<ReplaceStep> chain)
    {
        var result = input;

        foreach (var step in chain)
        {
            result = step switch
            {
                LiteralReplace s => s.IgnoreCase
                    ? result.Replace(s.Find, s.Replacement, StringComparison.OrdinalIgnoreCase)
                    : result.Replace(s.Find, s.Replacement, StringComparison.Ordinal),
                RegexReplace s => Regex.Replace(result, s.Pattern, s.Replacement, RegexOptions.None, TimeSpan.FromSeconds(1)),
                CaseTransform s => s.Mode switch
                {
                    CaseMode.Upper => result.ToUpperInvariant(),
                    CaseMode.Lower => result.ToLowerInvariant(),
                    CaseMode.Title => ToTitleCase(result),
                    _ => result
                },
                TrimSpacesStep => Regex.Replace(result, @"\s+", " ").Trim(),
                RemoveCharsStep s => string.Concat(result.Where(c => !s.CharSet.Contains(c))),
                _ => result
            };
        }

        return result;
    }

    private static string ApplyExtensionPolicy(string name, string extension, bool keepOriginalExtension)
    {
        if (keepOriginalExtension)
        {
            return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension;
        }

        if (extension.Length > 0 && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return name[..^extension.Length];
        }

        return name;
    }

    private static string ToTitleCase(string input)
    {
        var words = input.Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0)
            {
                continue;
            }

            words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..].ToLowerInvariant();
        }

        return string.Join(' ', words);
    }

    private static string GetParentDirectoryName(string fullPath)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        return parent is null ? "" : Path.GetFileName(parent);
    }

    private DateTime GetLocalNow()
    {
        return _timeProvider.GetLocalNow().DateTime;
    }

    private static int ParseRandomLength(string? arg)
    {
        return arg is not null && int.TryParse(arg, out var length) && length > 0 ? length : 6;
    }

    private static string GenerateRandom(int length)
    {
        return string.Create(length, (object?)null, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = char.Parse(RandomChars[Random.Shared.Next(RandomChars.Length)]);
            }
        });
    }

    [GeneratedRegex(@"\{(?<name>Prefix|OriginalName|BaseName|Extension|ParentDir|Hash8|Index|Counter|Date|FileDate|ExifDate|Random)(?::(?<arg>[^}]+))?\}", RegexOptions.Compiled)]
    private static partial Regex TemplateRegex();
}
