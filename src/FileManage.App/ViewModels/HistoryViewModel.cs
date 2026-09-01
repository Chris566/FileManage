using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManage.App.Services;
using FileManage.Core.Undo;

namespace FileManage.App.ViewModels;

/// <summary>
/// 历史撤销窗口 ViewModel：列出全部批次（新→旧），支持撤销指定批次 / 仅删除记录（多级撤销，设计文档 §4.5）。
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<BatchItem> Batches { get; } = [];

    [ObservableProperty]
    private BatchItem? _selectedBatch;

    [ObservableProperty]
    private string _resultText = "";

    public HistoryViewModel()
    {
        Reload();
    }

    [RelayCommand]
    private async Task UndoSelectedAsync()
    {
        if (SelectedBatch is null)
        {
            ResultText = "请先选择一个批次";
            return;
        }

        var batch = SelectedBatch.Batch;
        ResultText = "正在撤销…";

        var result = await Task.Run(() => AppServices.UndoManager.Undo(batch));

        if (result.Aborted)
        {
            // 原子性：关联报表删除失败 → 撤销中止，批次记录保留供重试
            ResultText = $"撤销已中止：关联报表删除失败（{result.Errors[0]}）。文件未被修改，可关闭占用该报表的程序后重试。";
            return;
        }

        // 撤销后删除该批次记录（批次不可重复撤销）
        AppServices.UndoStore.Delete(batch.Id);
        Reload();

        var batchTag = batch.Id.ToString("N")[..8];
        var reportNote = result.ReportsDeleted > 0
            ? $"，同步删除关联报表 {result.ReportsDeleted} 份"
            : "";
        ResultText = result.Success
            ? $"已撤销批次 {batchTag}…：恢复 {result.Reverted} 项{reportNote}"
            : $"撤销完成：{result.Reverted} 项成功，{result.Skipped} 项跳过，{result.Errors.Count} 项失败" +
              (result.Errors.Count > 0 ? $" — {result.Errors[0]}" : "");
    }

    [RelayCommand]
    private void DeleteRecord()
    {
        if (SelectedBatch is null)
        {
            ResultText = "请先选择一个批次";
            return;
        }

        AppServices.UndoStore.Delete(SelectedBatch.Batch.Id);
        Reload();
        ResultText = "已删除该批次记录（不影响已完成的文件操作）";
    }

    private void Reload()
    {
        Batches.Clear();
        SelectedBatch = null;

        foreach (var batch in AppServices.UndoStore.LoadAll().Reverse())
        {
            Batches.Add(new BatchItem(batch));
        }

        if (Batches.Count > 0)
        {
            SelectedBatch = Batches[0];
        }
    }
}

/// <summary>批次列表项。</summary>
public sealed class BatchItem(UndoBatch batch)
{
    public UndoBatch Batch { get; } = batch;

    public DateTime Time => Batch.Time;

    public string Description => Batch.Description;

    public int ActionCount => Batch.Actions.Count;
}
