using System.Windows;
using FileManage.App.Services;
using FileManage.App.ViewModels;

namespace FileManage.App.Views;

/// <summary>
/// 历史记录窗口：多级撤销入口。撤销成功后由调用方刷新主窗口预览。
/// </summary>
public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);
        DataContext = new HistoryViewModel();
    }
}
