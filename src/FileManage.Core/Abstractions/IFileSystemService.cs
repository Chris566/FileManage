namespace FileManage.Core.Abstractions;

/// <summary>
/// 文件系统访问抽象。Core 层所有 IO 依赖均通过此接口注入，测试时可替换实现。
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// 枚举指定目录下的文件完整路径。
    /// </summary>
    /// <param name="directory">起始目录。</param>
    /// <param name="maxDepth">递归深度，0 = 仅当前层（对齐旧版行为）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <remarks>无权限子目录自动跳过，不抛异常。</remarks>
    IReadOnlyList<string> EnumerateFiles(string directory, int maxDepth = 0, CancellationToken ct = default);

    /// <summary>获取文件大小（字节）与修改时间。</summary>
    (long SizeBytes, DateTime ModifiedTime) GetFileInfo(string filePath);

    /// <summary>判断文件是否已存在。</summary>
    bool FileExists(string filePath);

    /// <summary>移动/重命名文件。目标已存在时按 overwrite 决定。</summary>
    void MoveFile(string sourcePath, string destPath, bool overwrite);

    /// <summary>复制文件。目标已存在时按 overwrite 决定。</summary>
    void CopyFile(string sourcePath, string destPath, bool overwrite);

    /// <summary>删除文件。</summary>
    void DeleteFile(string filePath);

    /// <summary>创建目录（含父目录，已存在时静默）。</summary>
    void CreateDirectory(string directoryPath);

    /// <summary>计算文件 SHA-256（十六进制小写）。用于重复检测。</summary>
    string ComputeSha256(string filePath);
}
