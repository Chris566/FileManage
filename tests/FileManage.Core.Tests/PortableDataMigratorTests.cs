using System.IO;
using FileManage.Infrastructure.Storage;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// 便携版数据迁移：旧版 %AppData% 数据迁入 Data\，目标已存在跳过，旧数据保留。
/// </summary>
public class PortableDataMigratorTests : IDisposable
{
    private readonly TempDir _legacy = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _legacy.Dispose();
        _data.Dispose();
    }

    [Fact]
    public void Migrate_LegacyMissing_DoesNothing()
    {
        var missing = System.IO.Path.Combine(_legacy.Path, "not-exist");

        PortableDataMigrator.Migrate(missing, _data.Path);

        Assert.Empty(Directory.GetFiles(_data.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Migrate_CopiesSettingsAndRulesAndSubDirectories()
    {
        File.WriteAllText(Path.Combine(_legacy.Path, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_legacy.Path, "rules.json"), "[]");
        Directory.CreateDirectory(Path.Combine(_legacy.Path, "undo"));
        File.WriteAllText(Path.Combine(_legacy.Path, "undo", "batch1.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_legacy.Path, "backup"));
        File.WriteAllText(Path.Combine(_legacy.Path, "backup", "f.pdf.bak"), "x");

        PortableDataMigrator.Migrate(_legacy.Path, _data.Path);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(_data.Path, "settings.json")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(_data.Path, "rules.json")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_data.Path, "undo", "batch1.json")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(_data.Path, "backup", "f.pdf.bak")));
    }

    [Fact]
    public void Migrate_TargetExists_SkipsWithoutOverwrite()
    {
        File.WriteAllText(Path.Combine(_legacy.Path, "settings.json"), "{\"new\":true}");
        File.WriteAllText(Path.Combine(_data.Path, "settings.json"), "{\"portable\":true}");

        PortableDataMigrator.Migrate(_legacy.Path, _data.Path);

        Assert.Equal("{\"portable\":true}", File.ReadAllText(Path.Combine(_data.Path, "settings.json")));
    }

    [Fact]
    public void Migrate_LegacyDataPreserved()
    {
        File.WriteAllText(Path.Combine(_legacy.Path, "settings.json"), "{}");

        PortableDataMigrator.Migrate(_legacy.Path, _data.Path);

        // 旧数据保留不删，便于回退旧版本
        Assert.True(File.Exists(Path.Combine(_legacy.Path, "settings.json")));
    }
}
