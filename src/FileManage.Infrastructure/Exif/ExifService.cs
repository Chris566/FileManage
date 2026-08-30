using FileManage.Core.Abstractions;
using MetadataExtractor;

namespace FileManage.Infrastructure.Exif;

/// <summary>
/// EXIF 读取实现（MetadataExtractor，设计文档 §2 选型）。
/// 任何异常都吞掉返回 null——EXIF 缺失只是回退修改时间，不应中断扫描。
/// </summary>
public sealed class ExifService : IExifService
{
    public DateTime? ReadCaptureDate(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var directories = ImageMetadataReader.ReadMetadata(stream);

            // 优先 EXIF SubIfd/Ifd0 的 DateTimeOriginal，其次 Digitized / CreateDate
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    if (tag.Name is "DateTimeOriginal" or "Digitized" or "CreateDate"
                        && TryParseExifDate(tag.Description, out var date))
                    {
                        return date;
                    }
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    /// <summary>解析 EXIF 日期文本："2026:08:30 12:34:56" / ISO8601 两种格式。</summary>
    private static bool TryParseExifDate(string? text, out DateTime value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Replace(':', '-');

        if (normalized.Length >= 19)
        {
            normalized = normalized[..19];
        }

        return DateTime.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out value);
    }
}
