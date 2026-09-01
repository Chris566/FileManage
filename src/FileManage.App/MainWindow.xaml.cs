using System.Windows;
using FileManage.App.Services;
using FileManage.App.ViewModels;

namespace FileManage.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);

        // 响应式初始高度：默认 860 保证左侧"执行选项"完整可见；
        // 小屏幕按工作区高度收窄（左列保留滚动兜底），并限制最大高度不超出屏幕
        var workArea = SystemParameters.WorkArea;
        MaxHeight = workArea.Height;
        if (workArea.Height < Height)
        {
            Height = Math.Max(MinHeight, workArea.Height - 8);
        }

        DataContext = new MainViewModel(
            AppServices.Scanner,
            AppServices.NameEngine,
            AppServices.ConflictDetector,
            AppServices.Executor,
            AppServices.UndoManager,
            AppServices.UndoStore,
            new DialogOverwriteResolver());

        Closed += OnMainWindowClosed;
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SaveSessionState();
        }
    }
}
