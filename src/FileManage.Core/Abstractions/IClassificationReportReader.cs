using FileManage.Core.Reporting;

namespace FileManage.Core.Abstractions;

/// <summary>
/// 分类整理报表读取器抽象（IO 实现位于 Infrastructure）：
/// 从 .xlsx 报表文件解析回覆条目列表。
/// </summary>
public interface IClassificationReportReader
{
    /// <summary>从 reportPath 读取 .xlsx 报表，返回回覆条目列表。</summary>
    IReadOnlyList<RestoreEntry> Read(string reportPath);
}
