using FileManage.Core.Abstractions;

namespace FileManage.Infrastructure.Backup;

/// <summary>
/// 文件备份服务：覆盖前快照，按批次 ID 归档。
/// 目录结构：{backupRoot}/{batchId}/{n}_{fileName}（n 防同名）。
/// </summary>
public sealed class FileBackupService : IFileBackupService
{
    private readonly string _backupRoot;

    public FileBackupService(string backupRoot)
    {
        _backupRoot = backupRoot;
    }

    public string BackupFile(string sourcePath, Guid batchId)
    {
        var batchDir = Path.Combine(_backupRoot, batchId.ToString("N"));
        Directory.CreateDirectory(batchDir);

        var fileName = Path.GetFileName(sourcePath);
        string dest;
        var n = 0;

        do
        {
            dest = n == 0
                ? Path.Combine(batchDir, fileName)
                : Path.Combine(batchDir, $"{n}_{fileName}");
            n++;
        }
        while (File.Exists(dest));

        File.Copy(sourcePath, dest, overwrite: false);
        return dest;
    }
}
