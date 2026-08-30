using System.IO;
using FileManage.Core.Abstractions;
using FileManage.Core.Execution;
using FileManage.Core.Naming;
using FileManage.Core.Planning;
using FileManage.Core.Rules;
using FileManage.Core.Scanning;
using FileManage.Core.Undo;
using FileManage.Infrastructure.Backup;
using FileManage.Infrastructure.FileSystem;
using FileManage.Infrastructure.Rules;
using FileManage.Infrastructure.Undo;

namespace FileManage.App.Services;

/// <summary>
/// 组合根：装配 Core/Infrastructure 服务与分类规则集。
/// 数据目录：%AppData%/FileManage/{backup,undo,rules.json}。
/// </summary>
public static class AppServices
{
    private static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileManage");

    public static string RulesFilePath { get; } = Path.Combine(AppDataRoot, "rules.json");

    public static FileScanner Scanner { get; } = new(new FileSystemService(), new Infrastructure.Exif.ExifService());

    public static NameEngine NameEngine { get; } = new();

    public static ConflictDetector ConflictDetector { get; } = new(new FileSystemService());

    public static TransactionExecutor Executor { get; } = new(
        new FileSystemService(),
        new FileBackupService(Path.Combine(AppDataRoot, "backup")),
        new JsonUndoStore(Path.Combine(AppDataRoot, "undo")));

    public static UndoManager UndoManager { get; } = new(new FileSystemService());

    public static IUndoStore UndoStore { get; } = new JsonUndoStore(Path.Combine(AppDataRoot, "undo"));

    public static RuleConfigStore RuleStore { get; } = new();

    /// <summary>
    /// 内置默认规则集（设计文档 §4.3），优先级即列表顺序。
    /// </summary>
    public static IReadOnlyList<ClassificationRule> DefaultRules { get; } =
    [
        new ClassificationRule
        {
            Name = "图片", Priority = 1, TargetSubfolder = "图片",
            Condition = new ExtensionIn(".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic")
        },
        new ClassificationRule
        {
            Name = "PDF", Priority = 2, TargetSubfolder = "PDF",
            Condition = new ExtensionIn(".pdf")
        },
        new ClassificationRule
        {
            Name = "文档", Priority = 3, TargetSubfolder = "文档",
            Condition = new ExtensionIn(".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md")
        },
        new ClassificationRule
        {
            Name = "视频", Priority = 4, TargetSubfolder = "视频",
            Condition = new ExtensionIn(".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv")
        },
        new ClassificationRule
        {
            Name = "音频", Priority = 5, TargetSubfolder = "音频",
            Condition = new ExtensionIn(".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a")
        },
        new ClassificationRule
        {
            Name = "压缩包", Priority = 6, TargetSubfolder = "压缩包",
            Condition = new ExtensionIn(".zip", ".rar", ".7z", ".tar", ".gz")
        }
    ];

    /// <summary>
    /// 加载分类规则：优先 rules.json（用户自定义），无则写入默认规则集并返回默认值。
    /// 加载异常（损坏文件）时回退默认集，不覆盖坏文件（便于用户手工恢复）。
    /// </summary>
    public static IReadOnlyList<ClassificationRule> LoadRules()
    {
        var loaded = RuleStore.Load(RulesFilePath);

        if (loaded is { Count: > 0 })
        {
            return loaded;
        }

        if (!File.Exists(RulesFilePath))
        {
            RuleStore.Save(RulesFilePath, DefaultRules);
        }

        return DefaultRules;
    }

    /// <summary>保存分类规则到 rules.json。</summary>
    public static void SaveRules(IReadOnlyList<ClassificationRule> rules)
    {
        RuleStore.Save(RulesFilePath, rules);
    }
}
