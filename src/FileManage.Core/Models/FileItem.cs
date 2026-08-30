namespace FileManage.Core.Models;

/// <summary>
/// 扫描得到的单个文件条目（设计文档 §4.1）。
/// 扫描器、命名引擎、规划器均以此对象为输入。
/// </summary>
public sealed record FileItem
{
    /// <summary>完整路径。</summary>
    public required string FullPath { get; init; }

    /// <summary>文件名（含后缀）。</summary>
    public required string Name { get; init; }

    /// <summary>后缀（含点，小写），如 ".pdf"。</summary>
    public required string Extension { get; init; }

    /// <summary>文件大小（字节）。</summary>
    public required long SizeBytes { get; init; }

    /// <summary>修改时间。</summary>
    public required DateTime ModifiedTime { get; init; }

    /// <summary>命中的分类规则名（规则引擎填充，未命中为 null）。</summary>
    public string? MatchedCategory { get; init; }

    /// <summary>EXIF 拍摄时间（仅照片，缺失为 null）。</summary>
    public DateTime? ExifDate { get; init; }

    /// <summary>内容 SHA-256 前 8 位（可选，扫描时按需计算；缺失为 null）。</summary>
    public string? ContentHash8 { get; init; }
}
