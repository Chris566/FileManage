using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManage.Core.Abstractions;
using FileManage.Core.Execution;
using FileManage.Core.Naming;
using FileManage.Core.Planning;
using FileManage.Core.Rules;
using FileManage.Core.Scanning;
using FileManage.Core.Undo;
using FileManage.App.Services;
using FileManage.Infrastructure.Settings;

namespace FileManage.App.ViewModels;

/// <summary>
/// 主窗口 ViewModel：扫描 → 规划（预览）→ 执行 → 撤销 完整流程。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly FileScanner _scanner;
    private readonly NameEngine _nameEngine;
    private readonly ConflictDetector _conflictDetector;
    private readonly TransactionExecutor _executor;
    private readonly UndoManager _undoManager;
    private readonly IUndoStore _undoStore;
    private readonly IOverwriteResolver _overwriteResolver;

    public MainViewModel(
        FileScanner scanner,
        NameEngine nameEngine,
        ConflictDetector conflictDetector,
        TransactionExecutor executor,
        UndoManager undoManager,
        IUndoStore undoStore,
        IOverwriteResolver overwriteResolver)
    {
        _scanner = scanner;
        _nameEngine = nameEngine;
        _conflictDetector = conflictDetector;
        _executor = executor;
        _undoManager = undoManager;
        _undoStore = undoStore;
        _overwriteResolver = overwriteResolver;

        // 恢复上次会话设置（主题/语言已在 App.OnStartup 应用于字典）
        var settings = UIStateService.Settings;
        _selectedThemeIndex = settings.Theme.Equals("dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _selectedLanguageIndex = settings.Language.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _sourceDirectory = settings.LastSourceDirectory;
        _targetDirectory = settings.LastTargetDirectory;
    }

    // ---------- 源目录与扫描 ----------

    [ObservableProperty]
    private string _sourceDirectory = "";

    [ObservableProperty]
    private bool _includeSubdirectories;

    [ObservableProperty]
    private string _includeGlobs = "";

    [ObservableProperty]
    private string _excludeGlobs = "~$*";

    // ---------- 重命名 ----------

    [ObservableProperty]
    private bool _namingEnabled = true;

    [ObservableProperty]
    private NamingTemplateItem _selectedTemplate = NamingTemplateItem.Defaults[0];

    [ObservableProperty]
    private string _prefix = "";

    [ObservableProperty]
    private bool _keepOriginalExtension = true;

    [ObservableProperty]
    private bool _readExifDate;

    /// <summary>替换链（由替换规则窗口编辑，随预览/执行一起生效）。</summary>
    public IReadOnlyList<ReplaceStep> ReplaceChain { get; private set; } = [];

    public int ReplaceChainCount => ReplaceChain.Count;

    // ---------- 分类 ----------

    [ObservableProperty]
    private bool _classificationEnabled;

    [ObservableProperty]
    private string _targetDirectory = "";

    [ObservableProperty]
    private bool _copyInsteadOfMove = true;

    // ---------- 执行 ----------

    [ObservableProperty]
    private OverwritePolicy _selectedPolicy = OverwritePolicy.Ask;

    // ---------- 界面状态（主题 / 语言） ----------

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    /// <summary>"替换规则… (N)"（模板文案来自语言字典，随语言切换刷新）。</summary>
    public string ReplaceChainButtonText
    {
        get
        {
            var template = Application.Current.TryFindResource("S.ReplaceChain") as string ?? "替换规则… ({0})";
            return string.Format(template, ReplaceChain.Count);
        }
    }

    // ---------- 状态 ----------

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<PreviewRowViewModel> PreviewRows { get; } = [];

    public NamingTemplateItem[] Templates { get; } = NamingTemplateItem.Defaults;

    public OverwritePolicy[] Policies { get; } =
        [OverwritePolicy.Ask, OverwritePolicy.OverwriteAll, OverwritePolicy.SkipAll];

    // ---------- 命令 ----------

    [RelayCommand]
    private void BrowseSource()
    {
        var folder = PickFolder("选择源目录");

        if (folder is not null)
        {
            SourceDirectory = folder;
        }
    }

    [RelayCommand]
    private void BrowseTarget()
    {
        var folder = PickFolder("选择分类目标目录");

        if (folder is not null)
        {
            TargetDirectory = folder;
        }
    }

    [RelayCommand]
    private async Task RefreshPreviewAsync()
    {
        if (!Validate())
        {
            return;
        }

        try
        {
            var plan = await Task.Run(BuildPlan);
            FillPreview(plan);
            StatusText = $"预览已加载：{plan.Entries.Count} 个文件，{plan.Operations.Count} 个操作";
        }
        catch (Exception ex)
        {
            StatusText = $"预览失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (!Validate())
        {
            return;
        }

        IsBusy = true;
        ProgressPercent = 0;

        try
        {
            var plan = await Task.Run(BuildPlan);

            if (plan.Operations.Count == 0)
            {
                StatusText = "没有可执行的操作（无文件或全部被阻断）";
                return;
            }

            var progress = new Progress<ProgressInfo>(p =>
            {
                ProgressPercent = p.Total == 0 ? 0 : p.Completed * 100.0 / p.Total;
                StatusText = $"({p.Completed}/{p.Total}) {p.CurrentDescription}";
            });

            var report = await _executor.ExecuteAsync(plan, SelectedPolicy, _overwriteResolver, progress);

            StatusText = report.RolledBack
                ? $"执行失败已回滚: {(report.Errors.Count > 0 ? report.Errors[0] : "未知错误")}"
                : report.Cancelled
                    ? $"已取消（完成 {report.Succeeded}/{report.Total}，可撤销）"
                    : $"完成：成功 {report.Succeeded}，跳过 {report.Skipped}" +
                      (report.UndoFilePath is not null ? "（可撤销）" : "");

            await RefreshPreviewCoreAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"执行失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 0;
        }
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        IsBusy = true;

        try
        {
            var latest = _undoStore.LoadAll().LastOrDefault();

            if (latest is null)
            {
                StatusText = "没有可撤销的操作";
                return;
            }

            var result = await Task.Run(() => _undoManager.Undo(latest));
            _undoStore.Delete(latest.Id);

            StatusText = result.Success
                ? $"已撤销上一次操作（{result.Reverted} 项）"
                : $"撤销完成：{result.Reverted} 项成功，{result.Errors.Count} 项失败";

            await RefreshPreviewCoreAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"撤销失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        UIStateService.ApplyTheme(value == 1 ? "dark" : "light");
        UIStateService.Save(UIStateService.Settings with { Theme = value == 1 ? "dark" : "light" });
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        var language = value == 1 ? "en-US" : "zh-CN";
        UIStateService.ApplyLanguage(language);
        UIStateService.Save(UIStateService.Settings with { Language = language });
        OnPropertyChanged(nameof(ReplaceChainButtonText));
    }

    /// <summary>主窗口关闭时保存上次使用的目录。</summary>
    public void SaveSessionState()
    {
        UIStateService.Save(new AppSettings
        {
            Theme = SelectedThemeIndex == 1 ? "dark" : "light",
            Language = SelectedLanguageIndex == 1 ? "en-US" : "zh-CN",
            LastSourceDirectory = SourceDirectory,
            LastTargetDirectory = TargetDirectory
        });
    }

    [RelayCommand]
    private void OpenReplaceChain()
    {
        var window = new Views.ReplaceChainWindow(ReplaceChain)
        {
            Owner = Application.Current.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            ReplaceChain = [.. window.ViewModel.BuildChain()];
            OnPropertyChanged(nameof(ReplaceChainCount));
            StatusText = $"已设置 {ReplaceChain.Count} 条替换规则";

            if (Validate())
            {
                _ = RefreshPreviewCoreAsync();
            }
        }
    }

    [RelayCommand]
    private void OpenRuleEditor()
    {
        var window = new Views.RuleEditorWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        StatusText = "规则已更新（规则管理窗口已保存则立即生效）";

        if (Validate())
        {
            _ = RefreshPreviewCoreAsync();
        }
    }

    [RelayCommand]
    private async Task OpenHistoryAsync()
    {
        var window = new Views.HistoryWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();

        if (Validate())
        {
            await RefreshPreviewCoreAsync();
        }
    }

    [RelayCommand]
    private async Task OpenDuplicateAsync()
    {
        var window = new Views.DuplicateWindow(SourceDirectory) { Owner = Application.Current.MainWindow };
        window.ShowDialog();

        if (Validate())
        {
            await RefreshPreviewCoreAsync();
        }
    }

    // ---------- 内部 ----------

    private OperationPlan BuildPlan()
    {
        var scanOptions = new ScanOptions
        {
            RootDirectory = SourceDirectory,
            MaxDepth = IncludeSubdirectories ? int.MaxValue : 0,
            IncludeGlobs = ParseGlobs(IncludeGlobs),
            ExcludeGlobs = ParseGlobs(ExcludeGlobs),
            ReadExifDate = ReadExifDate
        };

        var scanResult = _scanner.Scan(scanOptions);

        var plannerOptions = new PlannerOptions
        {
            SourceDirectory = SourceDirectory,
            Naming = NamingEnabled
                ? new NamingOptions
                {
                    Template = SelectedTemplate.Template,
                    Prefix = Prefix,
                    KeepOriginalExtension = KeepOriginalExtension,
                    ReplaceChain = ReplaceChain
                }
                : null,
            CategoryTargetRoot = ClassificationEnabled ? TargetDirectory : null
        };

        var ruleEngine = new RuleEngine(BuildRules());

        return new OperationPlanner(_nameEngine, ruleEngine, _conflictDetector).Build(scanResult, plannerOptions);
    }

    private IReadOnlyList<ClassificationRule> BuildRules()
    {
        return AppServices.LoadRules()
            .Select(r => r with { CopyInsteadOfMove = CopyInsteadOfMove })
            .ToArray();
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory) || !Directory.Exists(SourceDirectory))
        {
            StatusText = "请选择有效的源目录";
            return false;
        }

        if (ClassificationEnabled && string.IsNullOrWhiteSpace(TargetDirectory))
        {
            StatusText = "启用分类时请选择目标目录";
            return false;
        }

        return true;
    }

    private async Task RefreshPreviewCoreAsync()
    {
        var plan = await Task.Run(BuildPlan);
        FillPreview(plan);
    }

    private void FillPreview(OperationPlan plan)
    {
        PreviewRows.Clear();

        foreach (var entry in plan.Entries)
        {
            PreviewRows.Add(PreviewRowViewModel.From(entry));
        }
    }

    private static string[] ParseGlobs(string text)
    {
        return text
            .Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(g => g.Length > 0)
            .ToArray();
    }

    private string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
