using System.Windows;
using FileManage.App.Services;
using FileManage.App.ViewModels;

namespace FileManage.App.Views;

/// <summary>
/// 重复检测窗口：扫描目录 → 分组展示 → 勾选移入回收站。
/// </summary>
public partial class DuplicateWindow : Window
{
    public DuplicateWindow(string initialDirectory)
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);
        DataContext = new DuplicateViewModel(initialDirectory);
    }
}
