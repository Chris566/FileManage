using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManage.Core.Abstractions;
using FileManage.Core.Execution;
using FileManage.Core.Naming;
using FileManage.Core.Planning;
using FileManage.Core.Reporting;
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
        _generateReport = settings.GenerateClassificationReport;
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

    /// <summary>模板下拉框索引（显示文本由语言字典 S.Template.* 提供，随语言切换即时刷新）。</summary>
    public int SelectedTemplateIndex
    {
        get => Array.IndexOf(NamingTemplateItem.Defaults, SelectedTemplate);
        set => SelectedTemplate = NamingTemplateItem.Defaults[value];
    }

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

    /// <summary>分类整理完成后在目标目录生成 Excel 报表。</summary>
    [ObservableProperty]
    private bool _generateReport;

    // ---------- 执行 ----------

    [ObservableProperty]
    private OverwritePolicy _selectedPolicy = OverwritePolicy.Ask;

    /// <summary>覆盖策略下拉框索引（0=每次询问 1=全部覆盖 2=全部跳过）。
    /// 用索引绑定而非 SelectedValuePath，保证选择框显示本地化文本（DynamicResource 即时切换）。</summary>
    public int SelectedPolicyIndex
    {
        get => (int)SelectedPolicy;
        set => SelectedPolicy = (OverwritePolicy)value;
    }

    // ---------- 界面状态（主题 / 语言） ----------

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    /// <summary>外观菜单：浅色单选（拒绝取消选中，与 SelectedThemeIndex 同步）。</summary>
    public bool IsLightTheme
    {
        get => SelectedThemeIndex == 0;
        set
        {
            if (SelectedThemeIndex == 0) { OnPropertyChanged(); }
            else if (value) { SelectedThemeIndex = 0; }
        }
    }

    /// <summary>外观菜单：深色单选。</summary>
    public bool IsDarkTheme
    {
        get => SelectedThemeIndex == 1;
        set
        {
            if (SelectedThemeIndex == 1) { OnPropertyChanged(); }
            else if (value) { SelectedThemeIndex = 1; }
        }
    }

    /// <summary>语言菜单：中文单选。</summary>
    public bool IsChinese
    {
        get => SelectedLanguageIndex == 0;
        set
        {
            if (SelectedLanguageIndex == 0) { OnPropertyChanged(); }
            else if (value) { SelectedLanguageIndex = 0; }
        }
    }

    /// <summary>语言菜单：English 单选。</summary>
    public bool IsEnglish
    {
        get => SelectedLanguageIndex == 1;
        set
        {
            if (SelectedLanguageIndex == 1) { OnPropertyChanged(); }
            else if (value) { SelectedLanguageIndex = 1; }
        }
    }

    /// <summary>"替换规则… (N)"（模板文案来自语言字典，随语言切换刷新）。</summary>
    public string ReplaceChainButtonText
    {
        get
        {
            var template = Application.Current.TryFindResource("S.ReplaceChain") as string ?? "替换规则… ({0})";
            return string.Format(template, ReplaceChain.Count);
        }
    }

    // ---------- 版本信息（状态栏右下角；与关于窗口同源） ----------

    public string VersionText => Services.VersionInfo.VersionText;

    /// <summary>悬停提示：完整版本 + 构建日期（文案随语言切换刷新）。</summary>
    public string VersionToolTip =>
        string.Format(Localize.T("S.Tip.Version"), Services.VersionInfo.VersionText, Services.VersionInfo.BuildDate);

    // ---------- 状态 ----------

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<PreviewRowViewModel> PreviewRows { get; } = [];

    // ---------- 命令 ----------

    [RelayCommand]
    private void BrowseSource()
    {
        var folder = PickFolder(Localize.T("S.Dialog.PickSource"));

        if (folder is not null)
        {
            SourceDirectory = folder;
        }
    }

    [RelayCommand]
    private void BrowseTarget()
    {
        var folder = PickFolder(Localize.T("S.Dialog.PickTarget"));

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
                StatusText = Localize.T("S.Status.NoOperations");
                return;
            }

            var progress = new Progress<ProgressInfo>(p =>
            {
                ProgressPercent = p.Total == 0 ? 0 : p.Completed * 100.0 / p.Total;
                StatusText = Localize.F("S.Status.Progress", p.Completed, p.Total, p.CurrentDescription);
            });

            var report = await _executor.ExecuteAsync(plan, SelectedPolicy, _overwriteResolver, progress);

            StatusText = report.RolledBack
                ? $"执行失败已回滚: {(report.Errors.Count > 0 ? report.Errors[0] : "未知错误")}"
                : report.Cancelled
                    ? $"已取消（完成 {report.Succeeded}/{report.Total}，可撤销）"
                    : $"完成：成功 {report.Succeeded}，跳过 {report.Skipped}" +
                      (report.UndoFilePath is not null ? "（可撤销）" : "");

            // 分类整理报表（可选）：所有操作完成后在目标目录生成
            if (GenerateReport && ClassificationEnabled && !report.RolledBack)
            {
                try
                {
                    var reportPath = await Task.Run(() => TryWriteClassificationReport(plan, report));

                    if (reportPath is not null)
                    {
                        // 报表路径写回本批次撤销记录：撤销时同步删除（原子性）
                        AttachReportToUndoBatch(report.BatchId, reportPath);
                        StatusText += Localize.F("S.Status.ReportGenerated", reportPath);
                    }
                }
                catch (Exception ex)
                {
                    StatusText += Localize.F("S.Status.ReportFailed", ex.Message);
                }
            }

            await RefreshPreviewCoreAsync();
        }
        catch (Exception ex)
        {
            StatusText = Localize.F("S.Status.ExecuteFailed", ex.Message);
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
                StatusText = Localize.T("S.Status.NothingToUndo");
                return;
            }

            var result = await Task.Run(() => _undoManager.Undo(latest));

            if (result.Aborted)
            {
                // 原子性：关联报表删除失败 → 撤销中止，保留批次记录供重试
                StatusText = $"撤销已中止：关联报表删除失败（{result.Errors[0]}）。文件未被修改，可关闭占用该报表的程序后重试。";
                return;
            }

            _undoStore.Delete(latest.Id);

            var reportNote = result.ReportsDeleted > 0
                ? $"，同步删除关联报表 {result.ReportsDeleted} 份"
                : "";
            StatusText = result.Success
                ? $"已撤销上一次操作（{result.Reverted} 项{reportNote}）"
                : $"撤销完成：{result.Reverted} 项成功，{result.Errors.Count} 项失败";

            await RefreshPreviewCoreAsync();
        }
        catch (Exception ex)
        {
            StatusText = Localize.F("S.Status.UndoFailed", ex.Message);
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
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        var language = value == 1 ? "en-US" : "zh-CN";
        UIStateService.ApplyLanguage(language);
        UIStateService.Save(UIStateService.Settings with { Language = language });
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(ReplaceChainButtonText));
        OnPropertyChanged(nameof(VersionToolTip));
        StatusText = Localize.T("S.Status.LanguageSwitched");
    }

    /// <summary>主窗口关闭时保存上次使用的目录。</summary>
    public void SaveSessionState()
    {
        UIStateService.Save(new AppSettings
        {
            Theme = SelectedThemeIndex == 1 ? "dark" : "light",
            Language = SelectedLanguageIndex == 1 ? "en-US" : "zh-CN",
            LastSourceDirectory = SourceDirectory,
            LastTargetDirectory = TargetDirectory,
            GenerateClassificationReport = GenerateReport
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
            StatusText = Localize.F("S.Status.ReplaceChainSet", ReplaceChain.Count);

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
        StatusText = Localize.T("S.Status.RulesUpdated");
        if (Validate())
        {
            _ = RefreshPreviewCoreAsync();
        }
    }

    [RelayCommand]
    private void OpenAbout()
    {
        var window = new Views.AboutWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();
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

    /// <summary>
    /// 生成分类整理报表：报表行取自计划（命中规则的条目）+ 执行逐操作结果，
    /// 文件名 = 源文件夹名 + 执行时间 + 序号，写入目标目录后返回完整路径。
    /// 无命中条目时不生成，返回 null。
    /// </summary>
    private string? TryWriteClassificationReport(OperationPlan plan, ExecutionReport report)
    {
        var rows = ClassificationReportBuilder.Build(plan, report.Results);

        if (rows.Count == 0)
        {
            return null;
        }

        var exists = (string name) => File.Exists(Path.Combine(TargetDirectory, name));
        var fileName = ClassificationReportNamer.BuildFileName(SourceDirectory, DateTime.Now, exists);
        return AppServices.ReportWriter.Write(TargetDirectory, fileName, rows);
    }

    /// <summary>
    /// 将报表文件路径写回本次执行对应的撤销批次（按批次 Id 匹配），
    /// 撤销该批次时由 UndoManager 同步删除报表，保证撤销与报表删除的原子性。
    /// 关联失败仅降级为"撤销时少删一份报表"，不影响执行结果本身。
    /// </summary>
    private void AttachReportToUndoBatch(Guid batchId, string reportPath)
    {
        try
        {
            var batch = _undoStore.LoadAll().FirstOrDefault(b => b.Id == batchId);

            if (batch is not null)
            {
                _undoStore.Save(batch with { ReportPaths = [.. batch.ReportPaths, reportPath] });
            }
        }
        catch
        {
            // 忽略关联失败
        }
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory) || !Directory.Exists(SourceDirectory))
        {
            StatusText = Localize.T("S.Status.InvalidSource");
            return false;
        }

        if (ClassificationEnabled && string.IsNullOrWhiteSpace(TargetDirectory))
        {
            StatusText = Localize.T("S.Status.NeedTarget");
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
