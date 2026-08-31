using FileManage.Core.Reporting;

namespace FileManage.Core.Abstractions;

/// <summary>
/// 分类整理报表写入器抽象（IO 实现位于 Infrastructure）：
/// 将报表行写入指定目录下的 .xlsx 文件，返回完整文件路径。
/// </summary>
public interface IClassificationReportWriter
{
    /// <summary>在 targetDirectory 下写入 fileName（.xlsx）报表，返回完整路径。</summary>
    string Write(string targetDirectory, string fileName, IReadOnlyList<ClassificationReportRow> rows);
}
