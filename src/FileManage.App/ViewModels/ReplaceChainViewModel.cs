using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManage.Core.Naming;

namespace FileManage.App.ViewModels;

/// <summary>替换步骤类型下拉项。</summary>
public enum ReplaceStepKind
{
    Literal,
    Regex,
    Case,
    Trim,
    RemoveChars
}

/// <summary>
/// 替换链编辑窗口 ViewModel：对 BaseName 按顺序应用替换步骤（设计文档 §4.2.2）。
/// 编辑结果经 BuildChain() 返回给主窗口，随预览/执行一起生效。
/// </summary>
public partial class ReplaceChainViewModel : ObservableObject
{
    public ObservableCollection<StepItem> Steps { get; } = [];

    [ObservableProperty]
    private StepItem? _selectedStep;

    // ---------- 新增步骤的参数输入 ----------

    [ObservableProperty]
    private ReplaceStepKind _newKind = ReplaceStepKind.Literal;

    [ObservableProperty]
    private string _find = "";

    [ObservableProperty]
    private string _replacement = "";

    [ObservableProperty]
    private bool _ignoreCase;

    [ObservableProperty]
    private string _pattern = "";

    [ObservableProperty]
    private string _regexReplacement = "";

    public string[] CaseModeNames { get; } = ["全部大写", "全部小写", "首字母大写"];

    [ObservableProperty]
    private int _caseModeIndex;

    [ObservableProperty]
    private string _charSet = "";

    public ReplaceChainViewModel(IEnumerable<ReplaceStep> steps)
    {
        foreach (var step in steps)
        {
            Steps.Add(new StepItem(step));
        }
    }

    public static string[] KindNames { get; } = ["字面替换", "正则替换", "大小写转换", "整理空格", "移除字符"];

    [RelayCommand]
    private void Add()
    {
        ReplaceStep step = NewKind switch
        {
            ReplaceStepKind.Literal => new LiteralReplace(Find, Replacement, IgnoreCase),
            ReplaceStepKind.Regex => new RegexReplace(Pattern, RegexReplacement),
            ReplaceStepKind.Case => new CaseTransform((CaseMode)CaseModeIndex),
            ReplaceStepKind.Trim => new TrimSpacesStep(),
            ReplaceStepKind.RemoveChars => new RemoveCharsStep(CharSet),
            _ => new TrimSpacesStep()
        };

        Steps.Add(new StepItem(step));
        SelectedStep = Steps[^1];
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedStep is null)
        {
            return;
        }

        Steps.Remove(SelectedStep);
        SelectedStep = null;
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedStep is null)
        {
            return;
        }

        var index = Steps.IndexOf(SelectedStep);
        var target = index + delta;

        if (target < 0 || target >= Steps.Count)
        {
            return;
        }

        Steps.Move(index, target);
    }

    [RelayCommand]
    private void ClearAll()
    {
        Steps.Clear();
        SelectedStep = null;
    }

    public IReadOnlyList<ReplaceStep> BuildChain()
    {
        return Steps.Select(s => s.Step).ToArray();
    }
}

/// <summary>替换链步骤列表项（只读展示，修改 = 删除重建）。</summary>
public sealed class StepItem(ReplaceStep step)
{
    public ReplaceStep Step { get; } = step;

    public string Summary { get; } = Describe(step);

    private static string Describe(ReplaceStep step)
    {
        return step switch
        {
            LiteralReplace s => $"字面替换: \"{s.Find}\" → \"{s.Replacement}\"{(s.IgnoreCase ? "（忽略大小写）" : "")}",
            RegexReplace s => $"正则替换: /{s.Pattern}/ → \"{s.Replacement}\"",
            CaseTransform s => $"大小写: {s.Mode switch { CaseMode.Upper => "全部大写", CaseMode.Lower => "全部小写", _ => "首字母大写" }}",
            TrimSpacesStep => "整理空格（合并连续空白、去首尾）",
            RemoveCharsStep s => $"移除字符: {s.CharSet}",
            _ => step.GetType().Name
        };
    }
}
