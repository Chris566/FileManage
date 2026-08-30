using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManage.Core.Duplicate;
using FileManage.Core.Models;
using FileManage.Core.Scanning;
using FileManage.Infrastructure.FileSystem;

namespace FileManage.App.ViewModels;

/// <summary>
/// 重复检测窗口 ViewModel：扫描 → SHA-256 分组 → 勾选删除（移入回收站）。
/// </summary>
public partial class DuplicateViewModel : ObservableObject
{
    [ObservableProperty]
    private string _directory = "";

    [ObservableProperty]
    private bool _includeSubdirectories;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private DuplicateGroupItem? _selectedGroup;

    public ObservableCollection<DuplicateGroupItem> Groups { get; } = [];

    public DuplicateViewModel(string initialDirectory)
    {
        _directory = initialDirectory;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(Directory) || !System.IO.Directory.Exists(Directory))
        {
            SummaryText = "请选择有效的目录";
            return;
        }

        IsBusy = true;
        SummaryText = "扫描中…";
        Groups.Clear();
        SelectedGroup = null;

        try
        {
            var result = await Task.Run(() =>
            {
                var scan = new FileScanner(new FileSystemService()).Scan(new ScanOptions
                {
                    RootDirectory = Directory,
                    MaxDepth = IncludeSubdirectories ? int.MaxValue : 0
                });

                return new DuplicateDetector(new FileSystemService()).Detect(scan.Items);
            });

            foreach (var group in result.Groups)
            {
                Groups.Add(new DuplicateGroupItem(group));
            }

            SummaryText = result.Groups.Count == 0
                ? $"扫描 {result.ScannedCount} 个文件，未发现重复"
                : $"发现 {result.Groups.Count} 组重复（{result.DuplicateFileCount} 个文件，可释放 {FormatSize(result.WastedBytes)}）";
        }
        catch (Exception ex)
        {
            SummaryText = $"扫描失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>删除所有勾选的文件（移入回收站），返回成功数，供结果提示。</summary>
    [RelayCommand]
    private void DeleteChecked()
    {
        var targets = Groups
            .SelectMany(g => g.Files)
            .Where(f => f.IsChecked)
            .Select(f => f.FullPath)
            .ToArray();

        if (targets.Length == 0)
        {
            SummaryText = "请先勾选要删除的文件";
            return;
        }

        if (MessageBox.Show($"将把 {targets.Length} 个文件移入回收站（可从回收站恢复）。继续？",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var deleted = 0;
        var failed = 0;

        foreach (var path in targets)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                deleted++;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        SummaryText = $"已移入回收站 {deleted} 个文件{(failed > 0 ? $"，{failed} 个失败" : "")}";
        _ = ScanCommand.ExecuteAsync(null);
    }

    partial void OnSelectedGroupChanged(DuplicateGroupItem? value)
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = ReferenceEquals(group, value);
        }
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
            >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
            _ => $"{bytes} B"
        };
    }
}

/// <summary>重复组（列表显示 + 是否展开组内文件）。</summary>
public partial class DuplicateGroupItem : ObservableObject
{
    public DuplicateGroupItem(DuplicateGroup group)
    {
        Group = group;

        foreach (var file in group.Files)
        {
            Files.Add(new DuplicateFileItem(file));
        }
    }

    public DuplicateGroup Group { get; }

    public string SizeText => $"{FormatSize(Group.SizeBytes)} × {Group.Files.Count}";

    public ObservableCollection<DuplicateFileItem> Files { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
            >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
            _ => $"{bytes} B"
        };
    }
}

/// <summary>组内文件行（IsChecked = 勾选删除）。</summary>
public partial class DuplicateFileItem : ObservableObject
{
    public DuplicateFileItem(FileItem file)
    {
        FullPath = file.FullPath;
        Name = file.Name;
        DirectoryName = Path.GetDirectoryName(file.FullPath) ?? "";
        ModifiedTime = file.ModifiedTime;
    }

    public string FullPath { get; }

    public string Name { get; }

    public string DirectoryName { get; }

    public DateTime ModifiedTime { get; }

    [ObservableProperty]
    private bool _isChecked;
}
