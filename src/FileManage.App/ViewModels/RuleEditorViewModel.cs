using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManage.App.Services;
using FileManage.Core.Rules;
using FileManage.Infrastructure.Rules;
using Microsoft.Win32;

namespace FileManage.App.ViewModels;

/// <summary>
/// 规则管理窗口 ViewModel：预设切换与管理 + 规则列表增删改排序 + 条件编辑 + 导入/导出 JSON。
/// 切换即生效：切换/保存均写回 rules.json v2（经 AppServices），主窗口随后刷新即生效。
/// 系统默认预设（IsBuiltIn）完全锁定：仅可查看/复制/导出。
/// </summary>
public partial class RuleEditorViewModel : ObservableObject
{
    private readonly RuleConfigStore _store = new();
    private readonly RulePresetManager _manager;

    /// <summary>有未写盘的规则修改。</summary>
    private bool _dirty;

    /// <summary>构造初始化期间抑制切换流程。</summary>
    private bool _initializing = true;

    public ObservableCollection<RuleEditItem> Rules { get; } = [];

    public ObservableCollection<PresetItem> Presets { get; } = [];

    [ObservableProperty]
    private RuleEditItem? _selectedRule;

    [ObservableProperty]
    private PresetItem _selectedPreset;

    [ObservableProperty]
    private string _statusText = "";

    public bool HasSelection => SelectedRule is not null;

    /// <summary>激活预设是否可编辑（系统默认预设完全锁定）。</summary>
    public bool CanEditRules => SelectedPreset is { IsBuiltIn: false };

    /// <summary>右侧编辑面板可用性 = 选中规则 且 激活预设可编辑。</summary>
    public bool CanEditSelection => HasSelection && CanEditRules;

    /// <summary>预设条右侧状态徽标：区分系统默认（只读）与自定义（可编辑）。</summary>
    public string PresetBadge => SelectedPreset?.IsBuiltIn != false
        ? Localize.T("S.Preset.ReadOnlyBadge")
        : Localize.T("S.Preset.EditableBadge");

    public RuleEditorViewModel(RulePresetDocument document)
    {
        _manager = new RulePresetManager(document);
        foreach (var preset in _manager.Document.Presets)
        {
            Presets.Add(new PresetItem(preset.Id, preset.Name, preset.IsBuiltIn));
        }

        _selectedPreset = Presets.First(p => p.Id == _manager.ActivePreset.Id);
        LoadRulesFromActive();
        _initializing = false;
    }

    partial void OnSelectedRuleChanged(RuleEditItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEditSelection));
    }

    partial void OnSelectedPresetChanged(PresetItem value) => OnPresetSwitched(value);

    private void OnPresetSwitched(PresetItem? newItem)
    {
        if (_initializing || newItem is null)
        {
            return;
        }

        var previous = _manager.ActivePreset;

        // 切换即生效：先提交旧预设的未保存修改（防丢失），再切换、写盘、重载
        if (_dirty && !previous.IsBuiltIn)
        {
            CommitRulesToPreset(previous.Id);
        }

        _manager.SwitchPreset(newItem.Id);
        PersistDocument();
        _dirty = false;
        LoadRulesFromActive();

        OnPropertyChanged(nameof(CanEditRules));
        OnPropertyChanged(nameof(CanEditSelection));
        OnPropertyChanged(nameof(PresetBadge));
        NotifyEditCommands();
        StatusText = string.Format(Localize.T("S.Preset.Switched"), newItem.DisplayName);
    }

    // ---------- 预设管理 ----------

    [RelayCommand]
    private void NewPreset()
    {
        var name = PromptName(
            Localize.T("S.Preset.DialogTitleNew"),
            Localize.T("S.Preset.PromptNew"),
            Localize.T("S.Preset.NewPresetName"));

        if (name is null)
        {
            return;
        }

        name = UniqueName(name);
        var result = _manager.CreatePreset(name);

        if (!result.Success)
        {
            StatusText = LocalizeError(result.Error);
            return;
        }

        _dirty = false;
        PersistDocumentAndRebuild();
        StatusText = string.Format(Localize.T("S.Preset.Created"), name);
    }

    [RelayCommand]
    private void CopyPreset()
    {
        var source = _manager.ActivePreset;
        var baseName = source.IsBuiltIn
            ? Localize.T("S.Preset.DefaultName")
            : source.Name;
        var name = PromptName(
            Localize.T("S.Preset.DialogTitleCopy"),
            Localize.T("S.Preset.PromptCopy"),
            baseName + Localize.T("S.Preset.CopySuffix"));

        if (name is null)
        {
            return;
        }

        name = UniqueName(name);
        var result = _manager.CopyPreset(source.Id, name);

        if (!result.Success)
        {
            StatusText = LocalizeError(result.Error);
            return;
        }

        _dirty = false;
        PersistDocumentAndRebuild();
        StatusText = string.Format(Localize.T("S.Preset.Copied"), name);
    }

    [RelayCommand(CanExecute = nameof(CanManageActivePreset))]
    private void RenamePreset()
    {
        var current = _manager.ActivePreset;
        var name = PromptName(
            Localize.T("S.Preset.DialogTitleRename"),
            Localize.T("S.Preset.PromptRename"),
            current.Name);

        if (name is null)
        {
            return;
        }

        var result = _manager.RenamePreset(current.Id, name);

        if (!result.Success)
        {
            StatusText = LocalizeError(result.Error);
            return;
        }

        PersistDocumentAndRebuild();
        StatusText = string.Format(Localize.T("S.Preset.Renamed"), name);
    }

    [RelayCommand]
    private void DeletePreset()
    {
        var current = _manager.ActivePreset;

        if (current.IsBuiltIn)
        {
            // 权限控制：默认预设不可删除（按钮已禁用，此处兜底明确提示）
            System.Windows.MessageBox.Show(
                Localize.T("S.Preset.DeleteProtected"),
                Localize.T("S.RE.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            string.Format(Localize.T("S.Preset.DeleteConfirm"), current.Name),
            Localize.T("S.RE.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var result = _manager.DeletePreset(current.Id);

        if (!result.Success)
        {
            StatusText = LocalizeError(result.Error);
            return;
        }

        PersistDocumentAndRebuild();
        StatusText = string.Format(
            Localize.T("S.Preset.Deleted"),
            current.Name,
            Presets.First(p => p.Id == _manager.ActivePreset.Id).DisplayName);
    }

    private bool CanManageActivePreset => CanEditRules;

    /// <summary>弹出名称输入框；取消返回 null。</summary>
    private string? PromptName(string title, string label, string initial)
    {
        var owner = System.Windows.Application.Current.Windows.OfType<Views.RuleEditorWindow>().FirstOrDefault();
        return Views.PromptDialog.Show(owner!, title, label, initial);
    }

    /// <summary>与其他预设名不重名（自动追加序号）。</summary>
    private string UniqueName(string baseName)
    {
        if (!Presets.Any(p => string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";

            if (!Presets.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private void PersistDocument()
    {
        try
        {
            AppServices.SavePresetDocument(_manager.Document);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Localize.T("S.Preset.SaveFailed"), ex.Message);
        }
    }

    /// <summary>写盘后重建预设集合与选择（保持激活项选中）。</summary>
    private void PersistDocumentAndRebuild()
    {
        PersistDocument();
        var activeId = _manager.ActivePreset.Id;
        _initializing = true; // Clear/重设选择期间抑制切换流程
        Presets.Clear();

        foreach (var preset in _manager.Document.Presets)
        {
            Presets.Add(new PresetItem(preset.Id, preset.Name, preset.IsBuiltIn));
        }

        SelectedPreset = Presets.First(p => p.Id == activeId);
        _initializing = false;
        LoadRulesFromActive();

        OnPropertyChanged(nameof(CanEditRules));
        OnPropertyChanged(nameof(CanEditSelection));
        OnPropertyChanged(nameof(PresetBadge));
        NotifyEditCommands();
    }

    private void LoadRulesFromActive()
    {
        Rules.Clear();
        SelectedRule = null;

        foreach (var rule in _manager.ActiveRules)
        {
            var item = new RuleEditItem(rule);
            item.PropertyChanged += (_, _) => _dirty = true;
            Rules.Add(item);
        }
    }

    /// <summary>把编辑列表写回激活预设（内置预设调用方保证不触发）。</summary>
    private void CommitRulesToPreset(Guid presetId)
    {
        _manager.UpdateRules(presetId, ToRuleList());
    }

    private string LocalizeError(PresetError error) => Localize.T(error switch
    {
        PresetError.CannotModifyBuiltIn => "S.Preset.DeleteProtected",
        PresetError.DuplicateName => "S.Preset.DuplicateName",
        PresetError.NameRequired => "S.Preset.NameRequired",
        _ => "S.Preset.SaveFailed"
    });

    private void NotifyEditCommands()
    {
        AddCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        RenamePresetCommand.NotifyCanExecuteChanged();
    }

    // ---------- 列表操作 ----------

    [RelayCommand(CanExecute = nameof(CanEditRules))]
    private void Add()
    {
        var item = new RuleEditItem(new ClassificationRule
        {
            Name = Localize.T("S.RE.NewRule"),
            Priority = Rules.Count + 1,
            TargetSubfolder = Localize.T("S.RE.NewRule"),
            Condition = new ExtensionIn()
        });
        item.PropertyChanged += (_, _) => _dirty = true;
        Rules.Add(item);
        _dirty = true;
        SelectedRule = item;
    }

    [RelayCommand(CanExecute = nameof(CanEditRules))]
    private void Delete()
    {
        if (SelectedRule is null)
        {
            return;
        }

        Rules.Remove(SelectedRule);
        SelectedRule = null;
        _dirty = true;
        Renumber();
    }

    [RelayCommand(CanExecute = nameof(CanEditRules))]
    private void MoveUp()
    {
        Move(-1);
    }

    [RelayCommand(CanExecute = nameof(CanEditRules))]
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
        _dirty = true;
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
            Title = Localize.T("S.RE.ImportDialogTitle"),
            Filter = Localize.T("S.RE.JsonFilter")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var loaded = _store.Load(dialog.FileName);

        if (loaded is null)
        {
            StatusText = Localize.T("S.RE.ImportFailed");
            return;
        }

        // 导入 = 创建新的自定义预设（以文件名命名），避免误覆盖现有预设
        var name = UniqueName(Path.GetFileNameWithoutExtension(dialog.FileName));
        var result = _manager.CreatePreset(name, loaded);

        if (!result.Success)
        {
            StatusText = LocalizeError(result.Error);
            return;
        }

        _dirty = false;
        PersistDocumentAndRebuild();
        StatusText = string.Format(Localize.T("S.Preset.Imported"), loaded.Count, name);
    }

    [RelayCommand]
    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = Localize.T("S.RE.ExportDialogTitle"),
            Filter = Localize.T("S.RE.JsonFilter"),
            FileName = "rules.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _store.Save(dialog.FileName, ToRuleList());
            StatusText = string.Format(Localize.T("S.RE.Exported"), Rules.Count, dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Localize.T("S.RE.ExportFailed"), ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditRules))]
    private void Save()
    {
        try
        {
            Renumber();
            _manager.UpdateRules(_manager.ActivePreset.Id, ToRuleList());
            AppServices.SavePresetDocument(_manager.Document);
            _dirty = false;
            StatusText = string.Format(
                Localize.T("S.Preset.Saved"),
                _manager.ActivePreset.Name,
                Rules.Count);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Localize.T("S.Preset.SaveFailed"), ex.Message);
        }
    }

    private List<ClassificationRule> ToRuleList()
    {
        Renumber();
        return Rules.Select(r => r.ToRule()).ToList();
    }
}

/// <summary>预设下拉项：内置项以本地化"默认规则（系统）"展示，自定义项显示原名。</summary>
public record PresetItem(Guid Id, string Name, bool IsBuiltIn)
{
    public string DisplayName => IsBuiltIn
        ? $"{Localize.T("S.Preset.DefaultName")}（{Localize.T("S.Preset.BuiltInSuffix")}）"
        : Name;

    // 兜底：模板未生效的场合（如选中框回退渲染）也显示友好名称
    public override string ToString() => DisplayName;
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
