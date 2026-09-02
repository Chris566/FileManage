using System.IO;
using System.Text.Json;
using FileManage.Infrastructure.Storage;
using Xunit;

namespace FileManage.Core.Tests;

/// <summary>
/// 更新清单对比器：删除清单计算（跨版本残留清理）、Data 排除、SHA256 校验。
/// </summary>
public class UpdateManifestTests : IDisposable
{
    private readonly TempDir _app = new();

    public void Dispose() => _app.Dispose();

    private static UpdateManifest ManifestWithFiles(params string[] paths)
    {
        return new UpdateManifest
        {
            Version = "9.9.9",
            Files = paths.Select(p => new UpdateManifestEntry
            {
                Path = p,
                Sha256 = new string('0', 64),
                Size = 1
            }).ToList()
        };
    }

    [Fact]
    public void ComputeDeleteList_RemovesLegacyFilesNotInManifest()
    {
        // 本地残留：旧版 dll（新版已移除）
        File.WriteAllText(Path.Combine(_app.Path, "OldDependency.dll"), "x");
        Directory.CreateDirectory(Path.Combine(_app.Path, "runtimes", "win-x64", "native"));
        File.WriteAllText(Path.Combine(_app.Path, "runtimes", "win-x64", "native", "old_native.dll"), "x");

        var manifest = ManifestWithFiles("FileManage.exe", "FileManage.dll");

        var deleteList = UpdateManifestComparer.ComputeDeleteList(manifest, _app.Path);

        Assert.Contains("OldDependency.dll", deleteList);
        Assert.Contains("runtimes/win-x64/native/old_native.dll", deleteList);
        Assert.Equal(2, deleteList.Count);
    }

    [Fact]
    public void ComputeDeleteList_ExcludesDataAndBackupDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_app.Path, "Data", "undo"));
        Directory.CreateDirectory(Path.Combine(_app.Path, "_update_backup"));
        File.WriteAllText(Path.Combine(_app.Path, "Data", "rules.json"), "{}");
        File.WriteAllText(Path.Combine(_app.Path, "Data", "undo", "b1.json"), "{}");
        File.WriteAllText(Path.Combine(_app.Path, "_update_backup", "coreclr.dll"), "x");

        var manifest = ManifestWithFiles("FileManage.exe");

        var deleteList = UpdateManifestComparer.ComputeDeleteList(manifest, _app.Path);

        Assert.Empty(deleteList);
    }

    [Fact]
    public void ComputeDeleteList_ManifestFileNeverDeleted()
    {
        File.WriteAllText(Path.Combine(_app.Path, "manifest.json"), "{}");

        var manifest = ManifestWithFiles("FileManage.exe");

        var deleteList = UpdateManifestComparer.ComputeDeleteList(manifest, _app.Path);

        // 清单未声明 manifest.json 自身，但豁免规则保证它永不进入删除清单（幂等）
        Assert.DoesNotContain("manifest.json", deleteList);
        Assert.Empty(deleteList);
    }

    [Fact]
    public void ComputeDeleteList_MissingManifestFilesAreKept()
    {
        // 清单声明了文件但本地不存在 → 不应出现在删除清单
        var manifest = ManifestWithFiles("FileManage.exe", "not-on-disk.dll");

        var deleteList = UpdateManifestComparer.ComputeDeleteList(manifest, _app.Path);

        Assert.Empty(deleteList);
    }

    [Fact]
    public void MatchesManifest_VerifiesSha256AndMissingFiles()
    {
        var filePath = Path.Combine(_app.Path, "FileManage.dll");
        File.WriteAllText(filePath, "portable-content");

        var expectedHash = UpdateManifestComparer.ComputeSha256(filePath);

        Assert.True(UpdateManifestComparer.MatchesManifest(
            new UpdateManifestEntry { Path = "FileManage.dll", Sha256 = expectedHash }, _app.Path));
        Assert.False(UpdateManifestComparer.MatchesManifest(
            new UpdateManifestEntry { Path = "FileManage.dll", Sha256 = new string('f', 64) }, _app.Path));
        Assert.False(UpdateManifestComparer.MatchesManifest(
            new UpdateManifestEntry { Path = "missing.dll", Sha256 = expectedHash }, _app.Path));
    }

    [Fact]
    public void Manifest_JsonRoundTrip_MatchesCiFormat()
    {
        // 与 CI 生成的字段名（version/generatedAt/files[path,sha256,size]）兼容
        const string json = """
{
  "version": "1.8.1",
  "generatedAt": "2026-09-02T00:00:00.0000000Z",
  "files": [
    { "path": "FileManage.exe", "sha256": "abc", "size": 175 },
    { "path": "runtimes/win-x64/native/x.dll", "sha256": "def", "size": 10 }
  ]
}
""";
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal("1.8.1", manifest!.Version);
        Assert.Equal(2, manifest.Files.Count);
        Assert.Equal("runtimes/win-x64/native/x.dll", manifest.Files[1].Path);
        // NormalizePath：反斜杠输入统一为正斜杠
        Assert.Equal("runtimes/win-x64/native/x.dll",
            UpdateManifest.NormalizePath("runtimes\\win-x64\\native\\x.dll"));
    }
}
