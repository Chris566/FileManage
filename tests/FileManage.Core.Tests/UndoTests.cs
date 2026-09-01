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
/// 撤销与关联报表删除的原子性：撤销批次先删关联分类报表（全部成功才恢复文件；
/// 报表删除失败则中止，文件不动）。
/// </summary>
public class UndoReportTests : IDisposable
{
    private readonly TempDir _source = new();
    private readonly TempDir _target = new();
    private readonly TempDir _backup = new();
    private readonly TempDir _undoRoot = new();

    /// <summary>执行一个 copy b.txt → target 的计划，返回（批次, 报表路径写入器）。</summary>
    private async Task<(UndoBatch Batch, Func<string, UndoBatch> Attach)> ExecuteCopyAsync()
    {
        var bPath = _source.CreateFileWithContent("b.txt", "BBB");
        var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
        [
            new PlanEntry
            {
                Item = TestHelper.Item("b.txt", _source.Path),
                RequestedName = "b.txt", FinalName = "b.txt",
                ConflictType = ConflictType.None,
                Transfer = new CopyOp(bPath, _target.Path, "b.txt")
            }
        ]);

        var executor = new TransactionExecutor(
            new FileSystemService(),
            new FileBackupService(_backup.Path),
            new JsonUndoStore(_undoRoot.Path));
        var report = await executor.ExecuteAsync(plan, OverwritePolicy.SkipAll);

        Assert.True(report.Succeeded > 0);
        var store = new JsonUndoStore(_undoRoot.Path);
        var batch = Assert.Single(store.LoadAll());

        // 模拟 MainViewModel.AttachReportToUndoBatch：报表路径写回批次
        Func<string, UndoBatch> attach = reportPath =>
        {
            var updated = batch with { ReportPaths = [.. batch.ReportPaths, reportPath] };
            store.Save(updated);
            return Assert.Single(store.LoadAll());
        };

        return (batch, attach);
    }

    [Fact]
    public async Task Undo_WithAttachedReports_DeletesReportsAndRestoresFiles()
    {
        var (batch, attach) = await ExecuteCopyAsync();

        var report1 = Path.Combine(_target.Path, "src202608311200001.xlsx");
        var report2 = Path.Combine(_target.Path, "src202608311200002.xlsx");
        File.WriteAllText(report1, "xlsx1");
        File.WriteAllText(report2, "xlsx2");

        var updated = attach(report1);
        updated = updated with { ReportPaths = [.. updated.ReportPaths, report2] };
        new JsonUndoStore(_undoRoot.Path).Save(updated);

        var result = new UndoManager(new FileSystemService()).Undo(updated);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2, result.ReportsDeleted);
        Assert.False(result.Aborted);
        Assert.False(File.Exists(report1));
        Assert.False(File.Exists(report2));
        // 文件恢复：复制出的 b.txt 已删除
        Assert.False(File.Exists(Path.Combine(_target.Path, "b.txt")));
    }

    [Fact]
    public async Task Undo_ReportDeleteFails_AbortsAndKeepsFiles()
    {
        var (batch, attach) = await ExecuteCopyAsync();

        var reportPath = Path.Combine(_target.Path, "src202608311200003.xlsx");
        File.WriteAllText(reportPath, "xlsx");
        var updated = attach(reportPath);

        var manager = new UndoManager(new FileSystemService());

        // 独占锁定报表 → 删除失败 → 撤销中止
        using (var lockStream = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var aborted = manager.Undo(updated);

            Assert.True(aborted.Aborted);
            Assert.Equal(0, aborted.Reverted);
            Assert.NotEmpty(aborted.Errors);
            // 文件未被修改：复制出的文件仍在
            Assert.True(File.Exists(Path.Combine(_target.Path, "b.txt")));
            // 报表仍在
            Assert.True(File.Exists(reportPath));
        }

        // 释放锁定后重试：成功，报表删除、文件恢复
        var result = manager.Undo(updated);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(1, result.ReportsDeleted);
        Assert.False(File.Exists(reportPath));
        Assert.False(File.Exists(Path.Combine(_target.Path, "b.txt")));
    }

    [Fact]
    public async Task Undo_MissingReportFile_CountsAsSkippedNotError()
    {
        var (batch, attach) = await ExecuteCopyAsync();

        var ghost = Path.Combine(_target.Path, "ghost_report.xlsx");
        var updated = attach(ghost); // 路径已关联但文件不存在（如已被手动删除）

        var result = new UndoManager(new FileSystemService()).Undo(updated);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(0, result.ReportsDeleted);
        Assert.False(result.Aborted);
        Assert.False(File.Exists(Path.Combine(_target.Path, "b.txt")));
    }

    [Fact]
    public async Task Undo_LegacyBatchWithoutReports_RestoresFilesNormally()
    {
        // 旧版本批次（无 ReportPaths 字段）撤销行为不变
        var (batch, _) = await ExecuteCopyAsync();

        Assert.Empty(batch.ReportPaths);
        var result = new UndoManager(new FileSystemService()).Undo(batch);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(0, result.ReportsDeleted);
        Assert.False(File.Exists(Path.Combine(_target.Path, "b.txt")));
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
        ])
        {
            ReportPaths = [@"D:\target\报表202608301200001.xlsx"]
        };

        store.Save(batch);
        var loaded = Assert.Single(store.LoadAll());

        Assert.Equal(batch.Id, loaded.Id);
        Assert.Equal(batch.Description, loaded.Description);
        Assert.Equal(4, loaded.Actions.Count);
        Assert.IsType<UndoRename>(loaded.Actions[0]);
        Assert.IsType<UndoCopyCreated>(loaded.Actions[1]);
        Assert.IsType<UndoMove>(loaded.Actions[2]);
        Assert.IsType<UndoOverwrite>(loaded.Actions[3]);
        Assert.Equal(batch.ReportPaths, loaded.ReportPaths);

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
