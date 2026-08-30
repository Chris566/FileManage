namespace FileManage.Core.Abstractions;

/// <summary>
/// 文件备份服务（覆盖前快照，撤销时恢复）。实现于 Infrastructure。
/// </summary>
public interface IFileBackupService
{
    /// <summary>备份单个文件，返回备份文件路径。</summary>
    string BackupFile(string sourcePath, Guid batchId);
}
