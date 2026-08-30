using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FileManage.Core.Undo;

namespace FileManage.Infrastructure.Undo;

/// <summary>
/// 撤销批次 JSON 持久化：{undoRoot}/{batchId}.json。
/// </summary>
public sealed class JsonUndoStore : IUndoStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly string _undoRoot;

    public JsonUndoStore(string undoRoot)
    {
        _undoRoot = undoRoot;
        Directory.CreateDirectory(undoRoot);
    }

    public string Save(UndoBatch batch)
    {
        var filePath = Path.Combine(_undoRoot, batch.Id.ToString("N") + ".json");
        var json = JsonSerializer.Serialize(batch, SerializerOptions);
        File.WriteAllText(filePath, json);
        return filePath;
    }

    public IReadOnlyList<UndoBatch> LoadAll()
    {
        var batches = new List<UndoBatch>();

        foreach (var file in Directory.EnumerateFiles(_undoRoot, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                batches.Add(JsonSerializer.Deserialize<UndoBatch>(json, SerializerOptions)!);
            }
            catch (Exception)
            {
                // 单个损坏文件跳过，不影响整体加载
            }
        }

        return batches.OrderBy(b => b.Time).ToArray();
    }

    public void Delete(Guid batchId)
    {
        var filePath = Path.Combine(_undoRoot, batchId.ToString("N") + ".json");

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
