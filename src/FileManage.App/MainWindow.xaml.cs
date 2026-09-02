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
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            HandleDroppedPaths(paths, setTarget: false);
        }

        e.Handled = true;
    }

    /// <summary>源目录热区拖放悬停：显示高亮反馈。</summary>
    private void OnSourceZoneDragOver(object sender, DragEventArgs e)
        => OnZoneDragOver(e, SourceDropOverlay);

    /// <summary>源目录热区拖放离开：隐藏反馈。</summary>
    private void OnSourceZoneDragLeave(object sender, DragEventArgs e)
        => OnZoneDragLeave(e, SourceDropZone, SourceDropOverlay);

    private void OnSourceZoneDrop(object sender, DragEventArgs e)
        => OnZoneDrop(e, setTarget: false);

    /// <summary>目标目录热区拖放悬停：显示高亮反馈。</summary>
    private void OnTargetZoneDragOver(object sender, DragEventArgs e)
        => OnZoneDragOver(e, TargetDropOverlay);

    /// <summary>目标目录热区拖放离开：隐藏反馈。</summary>
    private void OnTargetZoneDragLeave(object sender, DragEventArgs e)
        => OnZoneDragLeave(e, TargetDropZone, TargetDropOverlay);

    private void OnTargetZoneDrop(object sender, DragEventArgs e)
        => OnZoneDrop(e, setTarget: true);

    private void OnZoneDragOver(DragEventArgs e, System.Windows.Controls.Border overlay)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;

            if (!overlay.Visibility.Equals(Visibility.Visible))
            {
                overlay.Visibility = Visibility.Visible;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    /// <summary>热区拖放离开：隐藏反馈。子元素（TextBox/Button）间移动会触发父级 DragLeave，
    /// 以指针是否真正离开热区边界为准，避免覆盖层闪烁。</summary>
    private void OnZoneDragLeave(DragEventArgs e, System.Windows.FrameworkElement zone, System.Windows.Controls.Border overlay)
    {
        var pos = e.GetPosition(zone);

        if (pos.X < 0 || pos.Y < 0
            || pos.X > zone.ActualWidth || pos.Y > zone.ActualHeight)
        {
            overlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnZoneDrop(DragEventArgs e, bool setTarget)
    {
        if (setTarget)
        {
            TargetDropOverlay.Visibility = Visibility.Collapsed;
        }
        else
        {
            SourceDropOverlay.Visibility = Visibility.Collapsed;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            HandleDroppedPaths(paths, setTarget);
        }

        e.Handled = true;
    }

    /// <summary>
    /// 统一处理拖入路径：仅接受文件夹（多个取第一个），拦截纯文件与不可访问目录，
    /// 通过状态栏/弹窗给出明确提示；设置源/目标目录后由属性变更回调自动刷新预览。
    /// </summary>
    private void HandleDroppedPaths(string[] paths, bool setTarget)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var folders = new List<string>();
        var fileCount = 0;

        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                folders.Add(p);
            }
            else
            {
                fileCount++;
            }
        }

        // 拖入项中没有文件夹：明确提示
        if (folders.Count == 0)
        {
            MessageBox.Show(
                this,
                Localize.F("S.DragDrop.NotFolder", paths[0]),
                Localize.T("S.DragDrop.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var folder = folders[0];

        // 权限/可用性探测：枚举第一项即可暴露 UnauthorizedAccessException/IOException
        try
        {
            _ = Directory.EnumerateFileSystemEntries(folder).FirstOrDefault();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                   or System.Security.SecurityException)
        {
            MessageBox.Show(
                this,
                Localize.F("S.DragDrop.NoAccess", folder),
                Localize.T("S.DragDrop.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // 多文件夹/混合拖入：使用第一个文件夹，其余项目忽略并提示
        if (folders.Count > 1 || fileCount > 0)
        {
            vm.StatusText = Localize.F("S.DragDrop.PartialAccept", folder);
        }

        if (setTarget)
        {
            vm.TargetDirectory = folder;
        }
        else
        {
            vm.SourceDirectory = folder;
        }
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
