using FileManage.Core.Abstractions;
using FileManage.Core.Execution;
using FileManage.Core.Planning;
using FileManage.Core.Undo;
using FileManage.Infrastructure.Backup;
using FileManage.Infrastructure.FileSystem;
using FileManage.Infrastructure.Undo;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// TransactionExecutor 集成测试：真实临时目录 IO，覆盖执行/覆盖策略/回滚/取消。
/// </summary>
public class TransactionExecutorTests : IDisposable
{
    private readonly TempDir _source = new();
    private readonly TempDir _backup = new();
    private readonly TempDir _undoRoot = new();

    private TransactionExecutor CreateExecutor() => new(
        new FileSystemService(),
        new FileBackupService(_backup.Path),
        new JsonUndoStore(_undoRoot.Path));

    [Fact]
    public async Task Execute_RenameOp_RenamesFileOnDisk()
    {
        _source.CreateFileWithContent("a.txt", "AAA");
        var plan = new OperationPlan(
            Guid.NewGuid(), DateTime.Now,
            [new PlanEntry { Item = TestHelper.Item("a.txt", _source.Path), RequestedName = "b.txt", FinalName = "b.txt", ConflictType = ConflictType.None, Rename = new RenameOp(Path.Combine(_source.Path, "a.txt"), "b.txt", Path.Combine(_source.Path, "b.txt")) }]);

        var report = await CreateExecutor().ExecuteAsync(plan, OverwritePolicy.SkipAll);

        Assert.Equal(1, report.Succeeded);
        Assert.False(File.Exists(Path.Combine(_source.Path, "a.txt")));
        Assert.Equal("AAA", File.ReadAllText(Path.Combine(_source.Path, "b.txt")));
    }

    [Fact]
    public async Task Execute_ChainedRenameThenCopy_CopyReadsRenamedFile()
    {
        _source.CreateFileWithContent("a.txt", "AAA");
        var target = new TempDir();

        try
        {
            var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
            [
                new PlanEntry
                {
                    Item = TestHelper.Item("a.txt", _source.Path),
                    RequestedName = "r.txt", FinalName = "r.txt",
                    ConflictType = ConflictType.None,
                    Rename = new RenameOp(Path.Combine(_source.Path, "a.txt"), "r.txt", Path.Combine(_source.Path, "r.txt")),
                    Transfer = new CopyOp(Path.Combine(_source.Path, "a.txt"), target.Path, "r.txt")
                }
            ]);

            var report = await CreateExecutor().ExecuteAsync(plan, OverwritePolicy.SkipAll);

            Assert.Equal(2, report.Succeeded);
            Assert.Equal("AAA", File.ReadAllText(Path.Combine(_source.Path, "r.txt")));
            Assert.Equal("AAA", File.ReadAllText(Path.Combine(target.Path, "r.txt")));
        }
        finally
        {
            target.Dispose();
        }
    }

    [Fact]
    public async Task Execute_OverwriteAll_CopiesAndBacksUpOriginal()
    {
        _source.CreateFileWithContent("new.txt", "NEW");
        var target = new TempDir();
        File.WriteAllText(Path.Combine(target.Path, "old.txt"), "OLD");

        try
        {
            var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
            [
                new PlanEntry
                {
                    Item = TestHelper.Item("new.txt", _source.Path),
                    RequestedName = "old.txt", FinalName = "old.txt",
                    ConflictType = ConflictType.TargetExists,
                    Transfer = new CopyOp(Path.Combine(_source.Path, "new.txt"), target.Path, "old.txt")
                }
            ]);

            var report = await CreateExecutor().ExecuteAsync(plan, OverwritePolicy.OverwriteAll);

            Assert.Equal(1, report.Succeeded);
            Assert.Equal("NEW", File.ReadAllText(Path.Combine(target.Path, "old.txt")));

            // 备份中应有 OLD 原文件
            var backupFiles = Directory.GetFiles(Path.Combine(_backup.Path, report.BatchId.ToString("N")));
            var backupFile = Assert.Single(backupFiles);
            Assert.Equal("OLD", File.ReadAllText(backupFile));

            // 撤销批次已保存
            Assert.NotNull(report.UndoFilePath);
            Assert.True(File.Exists(report.UndoFilePath));
        }
        finally
        {
            target.Dispose();
        }
    }

    [Fact]
    public async Task Execute_SkipAll_LeavesTargetUntouched()
    {
        _source.CreateFileWithContent("new.txt", "NEW");
        var target = new TempDir();
        File.WriteAllText(Path.Combine(target.Path, "old.txt"), "OLD");

        try
        {
            var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
            [
                new PlanEntry
                {
                    Item = TestHelper.Item("new.txt", _source.Path),
                    RequestedName = "old.txt", FinalName = "old.txt",
                    ConflictType = ConflictType.TargetExists,
                    Transfer = new CopyOp(Path.Combine(_source.Path, "new.txt"), target.Path, "old.txt")
                }
            ]);

            var report = await CreateExecutor().ExecuteAsync(plan, OverwritePolicy.SkipAll);

            Assert.Equal(0, report.Succeeded);
            Assert.Equal(1, report.Skipped);
            Assert.Equal("OLD", File.ReadAllText(Path.Combine(target.Path, "old.txt")));
            Assert.Null(report.UndoFilePath); // 无已执行操作 → 无撤销批次
        }
        finally
        {
            target.Dispose();
        }
    }

    [Fact]
    public async Task Execute_AskWithResolver_SkipAllDecisionCascades()
    {
        _source.CreateFileWithContent("a.txt", "A");
        _source.CreateFileWithContent("b.txt", "B");
        var target = new TempDir();
        File.WriteAllText(Path.Combine(target.Path, "a.txt"), "OLD-A");
        File.WriteAllText(Path.Combine(target.Path, "b.txt"), "OLD-B");

        try
        {
            var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
            [
                CopyEntry("a.txt", target.Path),
                CopyEntry("b.txt", target.Path)
            ]);

            var resolver = new StubResolver(OverwriteDecision.SkipAll);
            var report = await CreateExecutor().ExecuteAsync(plan, OverwritePolicy.Ask, resolver);

            Assert.Equal(2, report.Skipped);
            Assert.Equal(1, resolver.CallCount); // 第二个冲突由 SkipAll 策略接管
            Assert.Equal("OLD-A", File.ReadAllText(Path.Combine(target.Path, "a.txt")));
            Assert.Equal("OLD-B", File.ReadAllText(Path.Combine(target.Path, "b.txt")));
        }
        finally
        {
            target.Dispose();
        }
    }

    [Fact]
    public async Task Execute_MidwayFailure_RollsBackEverything()
    {
        _source.CreateFileWithContent("a.txt", "AAA");
        _source.CreateFile("fail.txt");
        var target = new TempDir();

        try
        {
            var failing = new FailingFileSystem(new FileSystemService(), "fail.txt");
            var executor = new TransactionExecutor(
                failing,
                new FileBackupService(_backup.Path),
                new JsonUndoStore(_undoRoot.Path));

            var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
            [
                new PlanEntry
                {
                    Item = TestHelper.Item("a.txt", _source.Path),
                    RequestedName = "r.txt", FinalName = "r.txt",
                    ConflictType = ConflictType.None,
                    Rename = new RenameOp(Path.Combine(_source.Path, "a.txt"), "r.txt", Path.Combine(_source.Path, "r.txt"))
                },
                new PlanEntry
                {
                    Item = TestHelper.Item("fail.txt", _source.Path),
                    RequestedName = "fail.txt", FinalName = "fail.txt",
                    ConflictType = ConflictType.None,
                    Transfer = new CopyOp(Path.Combine(_source.Path, "fail.txt"), target.Path, "fail.txt")
                }
            ]);

            // 失败操作的目标文件必须真实存在才能触发（Copy 到 target\fail.txt 正常成功——
            // 因此用 FailingFileSystem 在 Copy 阶段注入失败）
            var report = await executor.ExecuteAsync(plan, OverwritePolicy.SkipAll);

            Assert.True(report.RolledBack);
            Assert.NotEmpty(report.Errors);

            // 回滚后：rename 撤销，文件恢复原名
            Assert.Equal("AAA", File.ReadAllText(Path.Combine(_source.Path, "a.txt")));
            Assert.False(File.Exists(Path.Combine(_source.Path, "r.txt")));
        }
        finally
        {
            target.Dispose();
        }
    }

    [Fact]
    public async Task Execute_Cancelled_KeepsCompletedAndSavesUndo()
    {
        _source.CreateFileWithContent("a.txt", "AAA");
        _source.CreateFileWithContent("b.txt", "BBB");

        using var cts = new CancellationTokenSource();
        var cancelledOnce = false;

        // 同步进度回调：第一个操作开始后取消（Progress<T> 投递是异步的，时序不可靠）
        var progress = new SyncProgress(_ =>
        {
            if (!cancelledOnce)
            {
                cancelledOnce = true;
                cts.Cancel();
            }
        });

        var plan = new OperationPlan(Guid.NewGuid(), DateTime.Now,
        [
            RenameEntry("a.txt", "ra.txt"),
            RenameEntry("b.txt", "rb.txt")
        ]);

        var report = await CreateExecutor().ExecuteAsync(plan, OverwritePolicy.SkipAll, progress: progress, ct: cts.Token);

        Assert.True(report.Cancelled);
        Assert.Equal(1, report.Succeeded); // 第一个操作已完成
        Assert.True(File.Exists(Path.Combine(_source.Path, "ra.txt")));
        Assert.True(File.Exists(Path.Combine(_source.Path, "b.txt")));
        Assert.NotNull(report.UndoFilePath); // 已完成部分可撤销
    }

    private PlanEntry RenameEntry(string from, string to)
    {
        return new PlanEntry
        {
            Item = TestHelper.Item(from, _source.Path),
            RequestedName = to, FinalName = to,
            ConflictType = ConflictType.None,
            Rename = new RenameOp(Path.Combine(_source.Path, from), to, Path.Combine(_source.Path, to))
        };
    }

    private PlanEntry CopyEntry(string name, string targetDir)
    {
        return new PlanEntry
        {
            Item = TestHelper.Item(name, _source.Path),
            RequestedName = name, FinalName = name,
            ConflictType = ConflictType.TargetExists,
            Transfer = new CopyOp(Path.Combine(_source.Path, name), targetDir, name)
        };
    }

    public void Dispose()
    {
        _source.Dispose();
        _backup.Dispose();
        _undoRoot.Dispose();
    }
}

/// <summary>预置决策的覆盖询问 stub。</summary>
public sealed class StubResolver(OverwriteDecision decision) : IOverwriteResolver
{
    public int CallCount { get; private set; }

    public OverwriteDecision Resolve(string targetFile)
    {
        CallCount++;
        return decision;
    }
}

/// <summary>同步进度回调（避免 Progress&lt;T&gt; 异步投递的时序不确定性）。</summary>
public sealed class SyncProgress(Action<ProgressInfo> action) : IProgress<ProgressInfo>
{
    public void Report(ProgressInfo value) => action(value);
}

/// <summary>在指定路径相关的 Copy 操作上注入失败的文件系统包装。</summary>
public sealed class FailingFileSystem(IFileSystemService inner, string failOnName) : IFileSystemService
{
    public IReadOnlyList<string> EnumerateFiles(string directory, int maxDepth = 0, CancellationToken ct = default)
        => inner.EnumerateFiles(directory, maxDepth, ct);

    public (long SizeBytes, DateTime ModifiedTime) GetFileInfo(string filePath) => inner.GetFileInfo(filePath);

    public bool FileExists(string filePath) => inner.FileExists(filePath);

    public void MoveFile(string sourcePath, string destPath, bool overwrite) => inner.MoveFile(sourcePath, destPath, overwrite);

    public void CopyFile(string sourcePath, string destPath, bool overwrite)
    {
        if (Path.GetFileName(destPath).Contains(failOnName, StringComparison.Ordinal))
        {
            throw new IOException("测试注入的复制失败");
        }

        inner.CopyFile(sourcePath, destPath, overwrite);
    }

    public void DeleteFile(string filePath) => inner.DeleteFile(filePath);

    public void CreateDirectory(string directoryPath) => inner.CreateDirectory(directoryPath);

    public string ComputeSha256(string filePath) => inner.ComputeSha256(filePath);
}
