namespace FileManage.Core.Naming;

/// <summary>大小写转换模式。</summary>
public enum CaseMode
{
    /// <summary>全部大写。</summary>
    Upper,

    /// <summary>全部小写。</summary>
    Lower,

    /// <summary>每个单词首字母大写（以空格分词）。</summary>
    Title
}

/// <summary>
/// 替换链步骤基类（设计文档 §4.2.2）。按顺序对不含后缀的文件名（BaseName）应用。
/// </summary>
public abstract record ReplaceStep;

/// <summary>字面替换。</summary>
public sealed record LiteralReplace(string Find, string Replacement, bool IgnoreCase = false) : ReplaceStep;

/// <summary>正则替换（1 秒超时防灾难性回溯）。</summary>
public sealed record RegexReplace(string Pattern, string Replacement) : ReplaceStep;

/// <summary>大小写转换。</summary>
public sealed record CaseTransform(CaseMode Mode) : ReplaceStep;

/// <summary>合并连续空白为单个空格并去除首尾空白。</summary>
public sealed record TrimSpacesStep : ReplaceStep;

/// <summary>移除字符集中任意字符。</summary>
public sealed record RemoveCharsStep(string CharSet) : ReplaceStep;
