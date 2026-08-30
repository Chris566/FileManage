namespace FileManage.Core.Naming;

/// <summary>
/// 命名选项（设计文档 §4.2.3）。
/// </summary>
public sealed record NamingOptions
{
    /// <summary>前缀（渲染 {Prefix} 变量）。</summary>
    public string Prefix { get; init; } = "";

    /// <summary>命名模板。内置预设（对齐旧版三个下拉项）：
    /// "{Prefix}{OriginalName}" / "{Prefix}{BaseName}{Extension}" / "{Prefix}{Index}{Extension}"。</summary>
    public string Template { get; init; } = "{Prefix}{BaseName}{Extension}";

    /// <summary>替换链，在模板渲染前按顺序应用于 BaseName。</summary>
    public IReadOnlyList<ReplaceStep> ReplaceChain { get; init; } = [];

    /// <summary>保留原文件后缀（对齐旧版勾选）。</summary>
    public bool KeepOriginalExtension { get; init; } = true;

    /// <summary>序号起始值（{Index} / {Counter} 从此值开始）。</summary>
    public int CounterStart { get; init; } = 1;
}
