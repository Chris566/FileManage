using FileManage.Core.Abstractions;

namespace FileManage.Infrastructure.FileSystem;

/// <summary>
/// 基于真实磁盘的 IFileSystemService 实现。
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    public IReadOnlyList<string> EnumerateFiles(string directory, int maxDepth = 0, CancellationToken ct = default)
    {
        var result = new List<string>();
        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((directory, 0));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (dir, depth) = pending.Pop();

            try
            {
                result.AddRange(Directory.EnumerateFiles(dir));

                if (depth < maxDepth)
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        pending.Push((sub, depth + 1));
                    }
                }
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                    or DirectoryNotFoundException
                    or IOException)
            {
                // 无权限/失效目录跳过，不影响整体扫描
            }
        }

        return result;
    }

    public (long SizeBytes, DateTime ModifiedTime) GetFileInfo(string filePath)
    {
        var info = new FileInfo(filePath);
        return (info.Length, info.LastWriteTime);
    }

    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    public void MoveFile(string sourcePath, string destPath, bool overwrite)
    {
        File.Move(sourcePath, destPath, overwrite);
    }

    public void CopyFile(string sourcePath, string destPath, bool overwrite)
    {
        File.Copy(sourcePath, destPath, overwrite);
    }

    public void DeleteFile(string filePath)
    {
        File.Delete(filePath);
    }

    public void CreateDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }

    public string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
