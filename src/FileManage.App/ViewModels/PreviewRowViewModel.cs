using System.IO;
using FileManage.Core.Planning;

namespace FileManage.App.ViewModels;

/// <summary>
/// 命名模板预设（对齐旧版 $renameTemplateMap 四个下拉项）。
/// </summary>
public sealed record NamingTemplateItem(string DisplayName, string Template)
{
    public static NamingTemplateItem[] Defaults { get; } =
    [
        new("前缀 + 原文件名", "{Prefix}{OriginalName}"),
        new("前缀 + 序号 + 原文件名", "{Prefix}{Index}_{BaseName}"),
        new("原文件名", "{BaseName}"),
        new("序号 + 原文件名", "{Index}_{BaseName}")
    ];
}

/// <summary>
/// 预览表格行（只读，刷新时整体重建）。
/// </summary>
public sealed class PreviewRowViewModel
{
    public required string OriginalName { get; init; }

    public required string NewName { get; init; }

    public required string Category { get; init; }

    public required string Target { get; init; }

    public required string Conflict { get; init; }

    public required ConflictType ConflictType { get; init; }

    public static PreviewRowViewModel From(PlanEntry entry)
    {
        var conflict = ConflictText(entry.ConflictType);
        var copyConflictText = ConflictText(entry.CopyConflictType, "目标");

        if (conflict.Length > 0 && copyConflictText.Length > 0)
        {
            conflict += "；" + copyConflictText;
        }
        else if (copyConflictText.Length > 0)
        {
            conflict = copyConflictText;
        }

        return new PreviewRowViewModel
        {
            OriginalName = entry.Item.Name,
            NewName = entry.FinalName,
            Category = entry.Classification?.Rule.Name ?? "",
            Target = entry.Transfer switch
            {
                CopyOp c => $"复制 → {Path.Combine(c.TargetDir, c.TargetName)}",
                MoveOp m => $"移动 → {Path.Combine(m.TargetDir, m.TargetName)}",
                _ => ""
            },
            Conflict = conflict,
            ConflictType = MergeConflict(entry)
        };
    }

    private static string ConflictText(ConflictType type, string prefix = "")
    {
        return type switch
        {
            ConflictType.PlanDuplicate => "重名（已自动改号）",
            ConflictType.TargetExists => "目标已存在",
            ConflictType.PathTooLong => $"{prefix}路径过长（阻断）",
            ConflictType.InvalidChars => $"{prefix}名含非法字符（阻断）",
            _ => ""
        };
    }

    /// <summary>取行内较严重的冲突类型（用于行底色）。</summary>
    private static ConflictType MergeConflict(PlanEntry entry)
    {
        if (entry.ConflictType is ConflictType.PathTooLong or ConflictType.InvalidChars
            || entry.CopyConflictType is ConflictType.PathTooLong or ConflictType.InvalidChars)
        {
            return ConflictType.PathTooLong; // 阻断类统一红色
        }

        if (entry.ConflictType != ConflictType.None)
        {
            return entry.ConflictType;
        }

        return entry.CopyConflictType;
    }
}
