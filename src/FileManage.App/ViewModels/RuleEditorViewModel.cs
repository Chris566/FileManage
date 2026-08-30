using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManage.App.Services;
using FileManage.Core.Rules;
using Microsoft.Win32;

namespace FileManage.App.ViewModels;

/// <summary>
/// 规则管理窗口 ViewModel：规则列表增删改排序 + 条件编辑 + 导入/导出 JSON。
/// 保存写入 %AppData%/FileManage/rules.json（经 AppServices）。
/// </summary>
public partial class RuleEditorViewModel : ObservableObject
{
    private readonly FileManage.Infrastructure.Rules.RuleConfigStore _store = new();

    public ObservableCollection<RuleEditItem> Rules { get; } = [];

    [ObservableProperty]
    private RuleEditItem? _selectedRule;

    public bool HasSelection => SelectedRule is not null;

    [ObservableProperty]
    private string _statusText = "";

    public RuleEditorViewModel(IEnumerable<ClassificationRule> rules)
    {
        foreach (var rule in rules)
        {
            Rules.Add(new RuleEditItem(rule));
        }
    }

    partial void OnSelectedRuleChanged(RuleEditItem? value) => OnPropertyChanged(nameof(HasSelection));

    // ---------- 列表操作 ----------

    [RelayCommand]
    private void Add()
    {
        var item = new RuleEditItem(new ClassificationRule
        {
            Name = "新规则",
            Priority = Rules.Count + 1,
            TargetSubfolder = "新规则",
            Condition = new ExtensionIn()
        });
        Rules.Add(item);
        SelectedRule = item;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedRule is null)
        {
            return;
        }

        Rules.Remove(SelectedRule);
        SelectedRule = null;
        Renumber();
    }

    [RelayCommand]
    private void MoveUp()
    {
        Move(-1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        Move(1);
    }

    private void Move(int delta)
    {
        if (SelectedRule is null)
        {
            return;
        }

        var index = Rules.IndexOf(SelectedRule);
        var target = index + delta;

        if (target < 0 || target >= Rules.Count)
        {
            return;
        }

        Rules.Move(index, target);
        Renumber();
    }

    /// <summary>优先级 = 列表顺序（越靠前越先评估）。</summary>
    private void Renumber()
    {
        for (var i = 0; i < Rules.Count; i++)
        {
            Rules[i].Priority = i + 1;
        }
    }

    // ---------- 导入 / 导出 / 保存 ----------

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入分类规则",
            Filter = "JSON 文件 (*.json)|*.json|全部文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var loaded = _store.Load(dialog.FileName);

        if (loaded is null)
        {
            StatusText = "导入失败：文件不存在或格式不正确";
            return;
        }

        Rules.Clear();
        SelectedRule = null;

        foreach (var rule in loaded)
        {
            Rules.Add(new RuleEditItem(rule));
        }

        Renumber();
        StatusText = $"已导入 {loaded.Count} 条规则";
    }

    [RelayCommand]
    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出分类规则",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = "rules.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _store.Save(dialog.FileName, ToRuleList());
            StatusText = $"已导出 {Rules.Count} 条规则到 {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            AppServices.SaveRules(ToRuleList());
            StatusText = $"已保存 {Rules.Count} 条规则到 {AppServices.RulesFilePath}";
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败: {ex.Message}";
        }
    }

    private List<ClassificationRule> ToRuleList()
    {
        Renumber();
        return Rules.Select(r => r.ToRule()).ToList();
    }
}

/// <summary>单条规则的可编辑包装（Conditions 拆解为简单输入项）。</summary>
public partial class RuleEditItem : ObservableObject
{
    private readonly Guid _id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private bool _copyInsteadOfMove;

    [ObservableProperty]
    private string _targetSubfolder;

    [ObservableProperty]
    private string _extensionsText;

    [ObservableProperty]
    private string _nameRegexText;

    [ObservableProperty]
    private bool _useSize;

    [ObservableProperty]
    private string _minSizeText;

    [ObservableProperty]
    private string _maxSizeText;

    [ObservableProperty]
    private bool _useDate;

    [ObservableProperty]
    private DateTime _fromDate;

    [ObservableProperty]
    private DateTime _toDate;

    public string DisplayName => $"[{Priority}] {Name}{(Enabled ? "" : "（已禁用）")}";

    public string ConditionSummary
    {
        get
        {
            var parts = new List<string>();

            if (ParseExtensions().Length > 0)
            {
                parts.Add("扩展名 " + string.Join(",", ParseExtensions()));
            }

            if (NameRegexText.Trim().Length > 0)
            {
                parts.Add("正则 /" + NameRegexText.Trim() + "/");
            }

            if (UseSize)
            {
                parts.Add($"大小 {MinSizeText ?? "0"}~{MaxSizeText ?? "∞"}");
            }

            if (UseDate)
            {
                parts.Add($"日期 {FromDate:yyyy-MM-dd}~{ToDate:yyyy-MM-dd}");
            }

            return parts.Count == 0 ? "（无条件，永不命中）" : string.Join(" 且 ", parts);
        }
    }

    public RuleEditItem(ClassificationRule rule)
    {
        _id = rule.Id;
        _name = rule.Name;
        _priority = rule.Priority;
        _enabled = rule.Enabled;
        _copyInsteadOfMove = rule.CopyInsteadOfMove;
        _targetSubfolder = rule.TargetSubfolder;

        var (exts, regex, size, date) = Decompose(rule.Condition);
        _extensionsText = string.Join(";", exts);
        _nameRegexText = regex ?? "";
        _useSize = size is not null;
        _minSizeText = size?.Min?.ToString() ?? "";
        _maxSizeText = size?.Max?.ToString() ?? "";
        _useDate = date is not null;
        _fromDate = date?.From ?? DateTime.Today.AddYears(-1);
        _toDate = date?.To ?? DateTime.Today;
    }

    /// <summary>从任意条件树中提取各类条件的首个实例。</summary>
    private static (string[] Exts, string? Regex, SizeBetween? Size, DateBetween? Date) Decompose(
        MatchCondition condition)
    {
        string[] exts = [];
        string? regex = null;
        SizeBetween? size = null;
        DateBetween? date = null;

        void Walk(MatchCondition c)
        {
            switch (c)
            {
                case AllOf all:
                    foreach (var sub in all.Conditions)
                    {
                        Walk(sub);
                    }

                    break;
                case ExtensionIn e when exts.Length == 0:
                    exts = e.Exts;
                    break;
                case NameRegex r when regex is null:
                    regex = r.Pattern;
                    break;
                case SizeBetween s when size is null:
                    size = s;
                    break;
                case DateBetween d when date is null:
                    date = d;
                    break;
            }
        }

        Walk(condition);
        return (exts, regex, size, date);
    }

    /// <summary>重组为 ClassificationRule：启用中的条件 AND 组合。</summary>
    public ClassificationRule ToRule()
    {
        var conditions = new List<MatchCondition>();
        var exts = ParseExtensions();

        if (exts.Length > 0)
        {
            conditions.Add(new ExtensionIn(exts));
        }

        if (NameRegexText.Trim().Length > 0)
        {
            conditions.Add(new NameRegex(NameRegexText.Trim()));
        }

        if (UseSize)
        {
            conditions.Add(new SizeBetween(ParseLong(MinSizeText), ParseLong(MaxSizeText)));
        }

        if (UseDate)
        {
            conditions.Add(new DateBetween(FromDate, ToDate));
        }

        var condition = conditions.Count switch
        {
            0 => new ExtensionIn(),
            1 => conditions[0],
            _ => new AllOf([.. conditions])
        };

        return new ClassificationRule
        {
            Id = _id,
            Name = Name,
            Priority = Priority,
            Enabled = Enabled,
            CopyInsteadOfMove = CopyInsteadOfMove,
            TargetSubfolder = TargetSubfolder,
            Condition = condition
        };
    }

    private string[] ParseExtensions()
    {
        return ExtensionsText
            .Split([';', '，', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .Where(e => e.Length > 1)
            .ToArray();
    }

    private static long? ParseLong(string text)
    {
        return long.TryParse(text?.Trim(), out var value) ? value : null;
    }

    partial void OnNameChanged(string value) => NotifyChanged();
    partial void OnPriorityChanged(int value) => NotifyChanged();
    partial void OnEnabledChanged(bool value) => NotifyChanged();
    partial void OnExtensionsTextChanged(string value) => NotifyChanged();
    partial void OnNameRegexTextChanged(string value) => NotifyChanged();
    partial void OnUseSizeChanged(bool value) => NotifyChanged();
    partial void OnMinSizeTextChanged(string value) => NotifyChanged();
    partial void OnMaxSizeTextChanged(string value) => NotifyChanged();
    partial void OnUseDateChanged(bool value) => NotifyChanged();

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ConditionSummary));
    }
}
