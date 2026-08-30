using FileManage.Core.Abstractions;

namespace FileManage.Core.Undo;

/// <summary>
/// 撤销管理器：逆序执行 UndoBatch 中的逆操作，恢复到执行前状态。
/// 单条失败不中断整体（记录错误继续），保证尽量多的文件被恢复。
/// </summary>
public sealed class UndoManager(IFileSystemService fileSystem)
{
    public UndoResult Undo(UndoBatch batch)
    {
        var reverted = 0;
        var skipped = 0;
        var errors = new List<string>();

        for (var i = batch.Actions.Count - 1; i >= 0; i--)
        {
            try
            {
                if (UndoAction(batch.Actions[i]))
                {
                    reverted++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{batch.Actions[i].GetType().Name}: {ex.Message}");
            }
        }

        return new UndoResult
        {
            Reverted = reverted,
            Skipped = skipped,
            Errors = errors
        };
    }

    /// <summary>执行单条逆操作。返回 false 表示因条件不满足而跳过。</summary>
    private bool UndoAction(UndoAction action)
    {
        switch (action)
        {
            case UndoRename a:
                if (!fileSystem.FileExists(a.CurrentPath))
                {
                    return false; // 已被用户手动处理
                }

                fileSystem.MoveFile(a.CurrentPath, a.OriginalPath, overwrite: false);
                return true;

            case UndoCopyCreated a:
                if (!fileSystem.FileExists(a.CreatedPath))
                {
                    return false;
                }

                fileSystem.DeleteFile(a.CreatedPath);
                return true;

            case UndoMove a:
                if (!fileSystem.FileExists(a.MovedPath))
                {
                    return false;
                }

                fileSystem.MoveFile(a.MovedPath, a.OriginalPath, overwrite: true);
                return true;

            case UndoOverwrite a:
                if (!fileSystem.FileExists(a.BackupPath))
                {
                    return false; // 备份缺失，无法恢复
                }

                fileSystem.CopyFile(a.BackupPath, a.OverwrittenPath, overwrite: true);
                return true;

            default:
                return false;
        }
    }
}
