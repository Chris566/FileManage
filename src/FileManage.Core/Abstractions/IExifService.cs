namespace FileManage.Core.Abstractions;

/// <summary>
/// EXIF 读取抽象（设计文档 §4.2 {ExifDate} 变量 / §4.3 {ExifYear} 归档）。
/// </summary>
public interface IExifService
{
    /// <summary>
    /// 读取照片拍摄时间（EXIF DateTimeOriginal 优先，其次 Digitized/CreateDate）。
    /// 无 EXIF 或解析失败返回 null（调用方回退文件修改时间）。
    /// </summary>
    DateTime? ReadCaptureDate(string filePath);
}
