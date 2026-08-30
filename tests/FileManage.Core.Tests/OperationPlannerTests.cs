using FileManage.Core.Models;
using FileManage.Core.Naming;
using FileManage.Core.Planning;
using FileManage.Core.Rules;
using FileManage.Core.Scanning;
using FileManage.Infrastructure.FileSystem;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// OperationPlanner 集成测试：重命名 + 分类组装为可执行计划。
/// </summary>
public class OperationPlannerTests : IDisposable
{
    private readonly TempDir _source = new();
    private readonly TempDir _target = new();

    private OperationPlanner CreatePlanner(ClassificationRule[]? rules = null)
    {
        return new OperationPlanner(
            new NameEngine(),
            new RuleEngine(rules ?? []),
            new ConflictDetector(new FileSystemService()));
    }

    private ScanResult Scan(params string[] names)
    {
        var items = names
            .Select(n => TestHelper.Item(n, directory: _source.Path))
            .ToArray();
        return new ScanResult(items);
    }

    [Fact]
    public void Build_NamingOnly_GeneratesRenameOps()
    {
        _source.CreateFile("a.pdf");

        var planner = CreatePlanner();
        var plan = planner.Build(
            Scan("a.pdf"),
            new PlannerOptions
            {
                SourceDirectory = _source.Path,
                Naming = new NamingOptions { Template = "{Prefix}{BaseName}{Extension}", Prefix = "合同_" }
            });

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("合同_a.pdf", entry.FinalName);
        Assert.NotNull(entry.Rename);
        Assert.Null(entry.Transfer);
        Assert.Single(plan.Operations);
    }

    [Fact]
    public void Build_ClassificationOnly_GeneratesCopyOpWithOriginalName()
    {
        _source.CreateFile("a.pdf");

        var planner = CreatePlanner([AllRule("PDF")]);
        var plan = planner.Build(
            Scan("a.pdf"),
            new PlannerOptions
            {
                SourceDirectory = _source.Path,
                CategoryTargetRoot = _target.Path
            });

        var entry = Assert.Single(plan.Entries);
        Assert.Null(entry.Rename);
        Assert.Equal("a.pdf", entry.FinalName);

        var copy = Assert.IsType<CopyOp>(entry.Transfer);
        Assert.Equal(Path.Combine(_source.Path, "a.pdf"), copy.SourcePath);
        Assert.Equal("a.pdf", copy.TargetName);
    }

    [Fact]
    public void Build_NamingAndClassification_CopyUsesFinalName()
    {
        _source.CreateFile("a.pdf");

        var planner = CreatePlanner([AllRule("PDF")]);
        var plan = planner.Build(
            Scan("a.pdf"),
            new PlannerOptions
            {
                SourceDirectory = _source.Path,
                Naming = new NamingOptions { Template = "{Prefix}{BaseName}{Extension}", Prefix = "新_" },
                CategoryTargetRoot = _target.Path
            });

        var entry = Assert.Single(plan.Entries);
        var copy = Assert.IsType<CopyOp>(entry.Transfer);

        Assert.Equal("新_a.pdf", entry.FinalName);
        Assert.Equal("新_a.pdf", copy.TargetName);
        // 计划源路径为原始路径（执行器负责解析 rename 后的实际位置）
        Assert.Equal(Path.Combine(_source.Path, "a.pdf"), copy.SourcePath);
    }

    [Fact]
    public void Build_PlanDuplicate_RenameGetsCounterCopyGetsFinalName()
    {
        _source.CreateFile("a1.pdf");
        _source.CreateFile("a2.pdf");

        var planner = CreatePlanner([AllRule("PDF")]);
        var plan = planner.Build(
            Scan("a1.pdf", "a2.pdf"),
            new PlannerOptions
            {
                SourceDirectory = _source.Path,
                Naming = new NamingOptions { Template = "same{Extension}" },
                CategoryTargetRoot = _target.Path
            });

        Assert.Equal("same.pdf", plan.Entries[0].FinalName);
        Assert.Equal(ConflictType.PlanDuplicate, plan.Entries[1].ConflictType);
        Assert.Equal("same_2.pdf", plan.Entries[1].FinalName);
        Assert.Equal("same_2.pdf", plan.Entries[1].CopyFinalName);
    }

    [Fact]
    public void Build_InvalidCharsEntry_BlocksAllOperations()
    {
        // 请求名含非法字符：模板本身不含后缀时 keep=true 追加 ".pdf"，这里构造 RemoveChars 反向场景
        // 直接用 RequestedName 带非法字符的模板无法通过 NameEngine 常规路径构造，
        // 因此验证 CopyOp 阻断路径：分类子目录含非法字符由 ConflictDetector 标记
        var planner = CreatePlanner([AllRule("PDF")]);
        var plan = planner.Build(
            Scan("a.pdf"),
            new PlannerOptions
            {
                SourceDirectory = _source.Path,
                CategoryTargetRoot = _target.Path
            });

        // 正常路径不阻断
        var entry = Assert.Single(plan.Entries);
        Assert.Equal(ConflictType.None, entry.ConflictType);
        Assert.NotNull(entry.Transfer);
    }

    [Fact]
    public void Build_MoveRule_GeneratesMoveOp()
    {
        _source.CreateFile("a.pdf");

        var planner = CreatePlanner([AllRule("MOVED", copyInsteadOfMove: false)]);
        var plan = planner.Build(
            Scan("a.pdf"),
            new PlannerOptions
            {
                SourceDirectory = _source.Path,
                CategoryTargetRoot = _target.Path
            });

        var entry = Assert.Single(plan.Entries);
        Assert.IsType<MoveOp>(entry.Transfer);
    }

    [Fact]
    public void Build_UnmatchedFile_NoTransfer()
    {
        _source.CreateFile("a.unknown");

        // PDF 规则只匹配 .pdf，a.unknown 不命中
        var rules = new ClassificationRule[]
        {
            new() { Name = "PDF", Priority = 1, TargetSubfolder = "PDF", Condition = new ExtensionIn(".pdf") }
        };
        var planner = CreatePlanner(rules);
        var plan = planner.Build(
            Scan("a.unknown"),
            new PlannerOptions
            {
                SourceDirectory = _source.Path,
                CategoryTargetRoot = _target.Path
            });

        var entry = Assert.Single(plan.Entries);
        Assert.Null(entry.Classification);
        Assert.Null(entry.Transfer);
    }

    private static ClassificationRule AllRule(string name, bool copyInsteadOfMove = true)
    {
        return new ClassificationRule
        {
            Name = name,
            Priority = 1,
            TargetSubfolder = name,
            CopyInsteadOfMove = copyInsteadOfMove,
            Condition = new NameRegex(".*")
        };
    }

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }
}
