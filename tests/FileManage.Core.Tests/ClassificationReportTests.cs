using FileManage.Core.Execution;
using FileManage.Core.Naming;
using FileManage.Core.Planning;
using FileManage.Core.Reporting;
using FileManage.Core.Rules;
using FileManage.Core.Scanning;
using FileManage.Infrastructure.FileSystem;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// 分类整理报表：文件命名与行组装测试。
/// </summary>
public class ClassificationReportNamerTests
{
    [Theory]
    [InlineData(@"D:\data\照片", "照片")]
    [InlineData(@"D:\data\照片\", "照片")]
    [InlineData(@"D:\data\照片\\", "照片")]
    public void BuildFileName_UsesSourceFolderNameAndTimestampAndSequence(string sourceDir, string expectedPrefix)
    {
        var time = new DateTime(2026, 8, 31, 12, 34, 56);

        var name = ClassificationReportNamer.BuildFileName(sourceDir, time, _ => false);

        Assert.Equal($"{expectedPrefix}202608311234561.xlsx", name);
    }

    [Fact]
    public void BuildFileName_SequenceIncrementsWhileFileExists()
    {
        var time = new DateTime(2026, 8, 31, 12, 34, 56);
        var existing = new HashSet<string> { "照片202608311234561.xlsx", "照片202608311234562.xlsx" };

        var name = ClassificationReportNamer.BuildFileName(@"D:\data\照片", time, existing.Contains);

        Assert.Equal("照片202608311234563.xlsx", name);
    }

    [Fact]
    public void BuildFileName_EmptySourceName_FallsBackToReport()
    {
        var name = ClassificationReportNamer.BuildFileName(@"D:\", new DateTime(2026, 8, 31, 0, 0, 0), _ => false);

        Assert.Equal("Report202608310000001.xlsx", name);
    }
}

/// <summary>
/// 报表行组装：经真实 Planner 构建计划，覆盖复制/移动/重命名组合与冲突、执行结果注记。
/// </summary>
public class ClassificationReportBuilderTests : IDisposable
{
    private readonly TempDir _source = new();
    private readonly TempDir _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    private OperationPlan BuildPlan(string[] fileNames, NamingOptions? naming, bool copy = true)
    {
        var items = fileNames
            .Select(n => TestHelper.Item(n, directory: _source.Path))
            .ToArray();

        var planner = new OperationPlanner(
            new NameEngine(),
            new RuleEngine([PdfRule(copy)]),
            new ConflictDetector(new FileSystemService()));

        return planner.Build(
            new ScanResult(items),
            new PlannerOptions
            {
                SourceDirectory = _source.Path,
                Naming = naming,
                CategoryTargetRoot = _target.Path
            });
    }

    private static ClassificationRule PdfRule(bool copy) => new()
    {
        Name = "PDF", Priority = 1, TargetSubfolder = "PDF", CopyInsteadOfMove = copy,
        Condition = new ExtensionIn(".pdf")
    };

    /// <summary>全部操作按成功生成执行结果（对齐正常完成场景）。</summary>
    private static List<OperationResult> Succeeded(OperationPlan plan) =>
        plan.Operations
            .Select(op => new OperationResult(op, op.SourcePath, "", OperationOutcome.Succeeded))
            .ToList();

    [Fact]
    public void Build_CopyOperation_RecordsAllColumns()
    {
        var plan = BuildPlan(["a.pdf"], naming: null);

        var rows = ClassificationReportBuilder.Build(plan, Succeeded(plan));
        var row = Assert.Single(rows);

        Assert.Equal("a.pdf", row.OriginalName);
        Assert.Equal(Path.Combine(_source.Path, "a.pdf"), row.OriginalPath);
        Assert.Equal("a.pdf", row.NewName);
        Assert.Equal(Path.Combine(_target.Path, "PDF", "a.pdf"), row.NewPath);
        Assert.Equal("PDF", row.Category);
        Assert.Equal("复制", row.Operation);
        Assert.Equal("无冲突", row.Conflict);
        Assert.Equal("PDF", row.RuleName);
    }

    [Fact]
    public void Build_MoveOperation_RecordsMove()
    {
        var plan = BuildPlan(["a.pdf"], naming: null, copy: false);

        var row = Assert.Single(ClassificationReportBuilder.Build(plan, []));

        Assert.Equal("移动", row.Operation);
    }

    [Fact]
    public void Build_RenamePlusCopy_RecordsCombinedOperationAndFinalName()
    {
        var plan = BuildPlan(
            ["a.pdf"],
            new NamingOptions { Template = "{Prefix}{BaseName}{Extension}", Prefix = "合同_" });

        var row = Assert.Single(ClassificationReportBuilder.Build(plan, []));

        Assert.Equal("重命名+复制", row.Operation);
        Assert.Equal("合同_a.pdf", row.NewName);
        Assert.Equal(Path.Combine(_target.Path, "PDF", "合同_a.pdf"), row.NewPath);
    }

    [Fact]
    public void Build_UnmatchedFiles_Excluded()
    {
        var plan = BuildPlan(["a.pdf", "b.txt"], naming: null);

        var row = Assert.Single(ClassificationReportBuilder.Build(plan, []));

        Assert.Equal("a.pdf", row.OriginalName);
    }

    [Fact]
    public void Build_TargetExistsOnDisk_RecordsFileExistsConflict()
    {
        // 目标目录已存在同名文件 → 计划期标记 TargetExists
        _target.SubDir("PDF");
        File.WriteAllText(Path.Combine(_target.Path, "PDF", "a.pdf"), "OLD");

        var plan = BuildPlan(["a.pdf"], naming: null);

        var row = Assert.Single(ClassificationReportBuilder.Build(plan, Succeeded(plan)));

        Assert.Equal("文件已存在", row.Conflict);
    }

    [Fact]
    public void Build_SkippedTransfer_AppendsSkipNote()
    {
        var plan = BuildPlan(["a.pdf"], naming: null);
        var copy = Assert.IsType<CopyOp>(Assert.Single(plan.Entries).Transfer!);

        var results = new List<OperationResult>
        {
            new(copy, copy.SourcePath, Path.Combine(copy.TargetDir, copy.TargetName), OperationOutcome.Skipped)
        };

        var row = Assert.Single(ClassificationReportBuilder.Build(plan, results));

        Assert.Equal("无冲突（复制已跳过）", row.Conflict);
    }

    [Fact]
    public void Build_CancelledExecution_MissingResultsNotedAsNotExecuted()
    {
        var plan = BuildPlan(["a.pdf"], naming: null);

        var row = Assert.Single(ClassificationReportBuilder.Build(plan, []));

        Assert.Equal("无冲突（复制未执行）", row.Conflict);
    }
}
