using System.Windows;
using FileManage.App.Services;
using FileManage.App.ViewModels;

namespace FileManage.App.Views;

/// <summary>
/// 替换链编辑窗口：确定时把链回传给调用方（MainViewModel）。
/// </summary>
public partial class ReplaceChainWindow : Window
{
    public ReplaceChainWindow(IEnumerable<FileManage.Core.Naming.ReplaceStep> steps)
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);
        ViewModel = new ReplaceChainViewModel(steps);
        DataContext = ViewModel;
    }

    public ReplaceChainViewModel ViewModel { get; }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
