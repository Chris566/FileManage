using FileManage.Core.Execution;
using FileManage.Core.Planning;
using FileManage.Core.Undo;
using FileManage.Infrastructure.Backup;
using FileManage.Infrastructure.FileSystem;
using FileManage.Infrastructure.Undo;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// 撤销闭环集成测试：执行 → 生成撤销批次 → 撤销 → 目录状态与执行前一致（设计文档 §8）。
/// </summary>
public class UndoTests : IDisposable
{
    private readonly TempDir _source = new();
    private readonly TempDir _target = new();
    private readonly TempDir _backup = new();
    private readonly TempDir _undoRoot = new();

    private TransactionExecutor CreateExecutor() => new(
        new FileSystemService(),
        new FileBackupService(_backup.Path),
        new JsonUndoStore(_undoRoot.Path));

    [Fact]
    public async Task ExecuteThenUndo_FullCycle_RestoresOriginalState()
    {
        // 执行前状态：src: a.txt(AAA), b.txt(BBB)；target: r.txt(OLD)
        var aPath = _source.CreateFileWithContent("a.txt", "AAA");
        _source.CreateFileWithContent("b.txt", "BBB");
        var targetOldPath = Path.Combine(_target.Path, "r.txt");
        File.WriteAllText(targetOldPath, "OLD");

        var before = new Dictionary<string, string>
        {
            ["a.txt"] = "AAA",
            ["b.txt"] = "BBB",
            ["r.txt"] = "OLD"
        };

        // 计划：rename a→r（src 内）+ copy b→target + copy a→target\r（覆盖）
        var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
        [
            new PlanEntry
            {
                Item = TestHelper.Item("a.txt", _source.Path),
                RequestedName = "r.txt", FinalName = "r.txt",
                ConflictType = ConflictType.None,
                Rename = new RenameOp(aPath, "r.txt", Path.Combine(_source.Path, "r.txt"))
            },
            new PlanEntry
            {
                Item = TestHelper.Item("b.txt", _source.Path),
                RequestedName = "b.txt", FinalName = "b.txt",
                ConflictType = ConflictType.None,
                Transfer = new CopyOp(Path.Combine(_source.Path, "b.txt"), _target.Path, "b.txt")
            },
            new PlanEntry
            {
                Item = TestHelper.Item("a.txt", _source.Path),
                RequestedName = "r.txt", FinalName = "r.txt",
                ConflictType = ConflictType.TargetExists,
                Transfer = new CopyOp(aPath, _target.Path, "r.txt")
            }
        ]);

        var executor = CreateExecutor();
        var report = await executor.ExecuteAsync(plan, OverwritePolicy.OverwriteAll);

        Assert.Equal(3, report.Succeeded);
        Assert.NotNull(report.UndoFilePath);

        // 撤销
        var store = new JsonUndoStore(_undoRoot.Path);
        var batch = Assert.Single(store.LoadAll());
        var undoResult = new UndoManager(new FileSystemService()).Undo(batch);

        Assert.True(undoResult.Success, string.Join("; ", undoResult.Errors));

        // 验证与执行前完全一致
        Assert.Equal("AAA", File.ReadAllText(Path.Combine(_source.Path, "a.txt")));
        Assert.Equal("BBB", File.ReadAllText(Path.Combine(_source.Path, "b.txt")));
        Assert.Equal("OLD", File.ReadAllText(targetOldPath));
        Assert.False(File.Exists(Path.Combine(_target.Path, "b.txt")));
    }

    [Fact]
    public async Task Undo_MultipleBatches_RestoresStepByStep()
    {
        var aPath = _source.CreateFileWithContent("a.txt", "AAA");

        // 批次 1：a.txt → b.txt
        var plan1 = new OperationPlan(Guid.NewGuid(), DateTime.Now,
        [
            new PlanEntry
            {
                Item = TestHelper.Item("a.txt", _source.Path),
                RequestedName = "b.txt", FinalName = "b.txt",
                ConflictType = ConflictType.None,
                Rename = new RenameOp(aPath, "b.txt", Path.Combine(_source.Path, "b.txt"))
            }
        ]);

        // 批次 2：b.txt → c.txt
        var plan2 = new OperationPlan(Guid.NewGuid(), DateTime.Now,
        [
            new PlanEntry
            {
                Item = TestHelper.Item("b.txt", _source.Path),
                RequestedName = "c.txt", FinalName = "c.txt",
                ConflictType = ConflictType.None,
                Rename = new RenameOp(Path.Combine(_source.Path, "b.txt"), "c.txt", Path.Combine(_source.Path, "c.txt"))
            }
        ]);

        var executor = CreateExecutor();
        await executor.ExecuteAsync(plan1, OverwritePolicy.SkipAll);
        await executor.ExecuteAsync(plan2, OverwritePolicy.SkipAll);

        Assert.True(File.Exists(Path.Combine(_source.Path, "c.txt")));

        var store = new JsonUndoStore(_undoRoot.Path);
        var batches = store.LoadAll();
        Assert.Equal(2, batches.Count);

        var manager = new UndoManager(new FileSystemService());

        // 撤销批次 2 → 回到 b.txt
        manager.Undo(batches[1]);
        Assert.True(File.Exists(Path.Combine(_source.Path, "b.txt")));
        Assert.False(File.Exists(Path.Combine(_source.Path, "c.txt")));

        // 撤销批次 1 → 回到 a.txt
        manager.Undo(batches[0]);
        Assert.True(File.Exists(Path.Combine(_source.Path, "a.txt")));
        Assert.Equal("AAA", File.ReadAllText(aPath));
    }

    [Fact]
    public async Task Execute_RolledBack_NoUndoBatchSaved()
    {
        var aPath = _source.CreateFileWithContent("a.txt", "AAA");

        // 构造必然失败的计划：源文件不存在
        var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
        [
            new PlanEntry
            {
                Item = TestHelper.Item("ghost.txt", _source.Path),
                RequestedName = "g.txt", FinalName = "g.txt",
                ConflictType = ConflictType.None,
                Rename = new RenameOp(Path.Combine(_source.Path, "ghost.txt"), "g.txt", Path.Combine(_source.Path, "g.txt"))
            }
        ]);

        var report = await CreateExecutor().ExecuteAsync(plan, OverwritePolicy.SkipAll);

        Assert.True(report.RolledBack);
        Assert.Null(report.UndoFilePath);
        Assert.Empty(new JsonUndoStore(_undoRoot.Path).LoadAll());
        Assert.True(File.Exists(aPath)); // 源文件不受影响
    }

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
        _backup.Dispose();
        _undoRoot.Dispose();
    }
}

/// <summary>
/// JsonUndoStore 持久化往返：多态 UndoAction 序列化保留。
/// </summary>
public class JsonUndoStoreTests : IDisposable
{
    private readonly TempDir _undoRoot = new();

    [Fact]
    public void SaveThenLoadAll_RoundTripsAllActionTypes()
    {
        var store = new JsonUndoStore(_undoRoot.Path);
        var batch = new UndoBatch(Guid.NewGuid(), new DateTime(2026, 8, 30, 12, 0, 0), "测试批次",
        [
            new UndoRename(@"D:\dst\new.txt", @"D:\src\old.txt"),
            new UndoCopyCreated(@"D:\dst\copied.txt"),
            new UndoMove(@"D:\dst\moved.txt", @"D:\src\moved.txt"),
            new UndoOverwrite(@"D:\dst\overwritten.txt", @"D:\backup\1_overwritten.txt")
        ]);

        store.Save(batch);
        var loaded = Assert.Single(store.LoadAll());

        Assert.Equal(batch.Id, loaded.Id);
        Assert.Equal(batch.Description, loaded.Description);
        Assert.Equal(4, loaded.Actions.Count);
        Assert.IsType<UndoRename>(loaded.Actions[0]);
        Assert.IsType<UndoCopyCreated>(loaded.Actions[1]);
        Assert.IsType<UndoMove>(loaded.Actions[2]);
        Assert.IsType<UndoOverwrite>(loaded.Actions[3]);

        var rename = Assert.IsType<UndoRename>(loaded.Actions[0]);
        Assert.Equal(@"D:\dst\new.txt", rename.CurrentPath);
        Assert.Equal(@"D:\src\old.txt", rename.OriginalPath);
    }

    [Fact]
    public void Delete_RemovesBatchFile()
    {
        var store = new JsonUndoStore(_undoRoot.Path);
        var batch = new UndoBatch(Guid.NewGuid(), DateTime.Now, "x", [new UndoCopyCreated(@"D:\f.txt")]);

        store.Save(batch);
        Assert.Single(store.LoadAll());

        store.Delete(batch.Id);
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void LoadAll_SkipsCorruptedFiles()
    {
        var store = new JsonUndoStore(_undoRoot.Path);
        File.WriteAllText(Path.Combine(_undoRoot.Path, "corrupted.json"), "{ 不是 JSON");

        var valid = new UndoBatch(Guid.NewGuid(), DateTime.Now, "valid", []);
        store.Save(valid);

        var batches = store.LoadAll();
        Assert.Single(batches);
        Assert.Equal(valid.Id, batches[0].Id);
    }

    public void Dispose() => _undoRoot.Dispose();
}
