using System.IO;
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

        // 恢复上次窗口状态（M5 记忆）：最大化直接恢复；否则校验还原边界在虚拟屏幕内后按原位还原
        var s = UIStateService.Settings;
        if (s.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        else if (s.WindowWidth is > 100 && s.WindowHeight is > 100 && IsOnScreen(s.WindowX, s.WindowY))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowX!.Value;
            Top = s.WindowY!.Value;
            Width = s.WindowWidth.Value;
            Height = s.WindowHeight.Value;
        }

        DataContext = new MainViewModel(
            AppServices.Scanner,
            AppServices.NameEngine,
            AppServices.ConflictDetector,
            AppServices.Executor,
            AppServices.UndoManager,
            AppServices.UndoStore,
            new DialogOverwriteResolver());

        // Closed 事件已在 XAML 中订阅（OnMainWindowClosed）
    }

    /// <summary>校验还原位置落在当前虚拟屏幕内（多显示器/分辨率变化后仍安全）。</summary>
    private static bool IsOnScreen(int? x, int? y)
    {
        if (x is null || y is null)
        {
            return false;
        }

        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        return x.Value >= left - 10
            && y.Value >= top - 10
            && x.Value <= left + SystemParameters.VirtualScreenWidth - 100
            && y.Value <= top + SystemParameters.VirtualScreenHeight - 50;
    }

    /// <summary>拖放文件夹到窗口：自动设置源目录并触发预览。</summary>
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var path = paths[0];

        // 仅接受目录
        if (Directory.Exists(path) && DataContext is MainViewModel vm)
        {
            vm.SourceDirectory = path;
        }

        e.Handled = true;
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // 最大化时保存还原边界，下次启动先还原尺寸位置再最大化
            var maximized = WindowState == WindowState.Maximized;
            var bounds = maximized ? RestoreBounds : new Rect(Left, Top, ActualWidth, ActualHeight);
            vm.SaveSessionState(
                maximized,
                (int)bounds.Left, (int)bounds.Top,
                (int)bounds.Width, (int)bounds.Height);
        }
    }
}
