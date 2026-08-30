using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FileManage.Core.Rules;

/// <summary>
/// 分类匹配条件基类（设计文档 §4.3）。多个条件可通过 <see cref="AllOf"/> 组合（AND 语义）。
/// JSON 多态序列化（rules.json / 导入导出）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(ExtensionIn), "extIn")]
[JsonDerivedType(typeof(NameRegex), "nameRegex")]
[JsonDerivedType(typeof(SizeBetween), "sizeBetween")]
[JsonDerivedType(typeof(DateBetween), "dateBetween")]
[JsonDerivedType(typeof(AllOf), "allOf")]
public abstract record MatchCondition;

/// <summary>扩展名匹配。输入允许带点或不带点（".pdf" / "pdf"），比较不区分大小写。</summary>
public sealed record ExtensionIn(params string[] Exts) : MatchCondition
{
    public IReadOnlyList<string> NormalizedExtensions { get; } =
        Exts.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()).ToArray();
}

/// <summary>文件名正则匹配（不区分大小写，1 秒超时）。</summary>
public sealed record NameRegex(string Pattern) : MatchCondition;

/// <summary>文件大小区间（字节），含边界。null 表示该侧不限制。</summary>
public sealed record SizeBetween(long? Min, long? Max) : MatchCondition;

/// <summary>修改时间区间，含边界。null 表示该侧不限制。</summary>
public sealed record DateBetween(DateTime? From, DateTime? To) : MatchCondition;

/// <summary>条件组合，全部满足才算命中（AND）。</summary>
public sealed record AllOf(params MatchCondition[] Conditions) : MatchCondition;
