namespace FileManage.Core.Undo;

/// <summary>
/// 撤销批次持久化（实现于 Infrastructure，JSON 存储）。
/// </summary>
public interface IUndoStore
{
    /// <summary>保存批次，返回存储文件路径。</summary>
    string Save(UndoBatch batch);

    /// <summary>加载全部批次（按时间升序）。</summary>
    IReadOnlyList<UndoBatch> LoadAll();

    /// <summary>删除批次记录。</summary>
    void Delete(Guid batchId);
}
