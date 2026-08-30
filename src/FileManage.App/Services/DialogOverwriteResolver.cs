using System.Windows;
using FileManage.App.Views;
using FileManage.Core.Execution;

namespace FileManage.App.Services;

/// <summary>
/// 覆盖询问解析器：在 UI 线程弹出询问对话框。
/// 执行器运行于后台线程，故经 Dispatcher.Invoke 回到 UI 线程。
/// </summary>
public sealed class DialogOverwriteResolver : IOverwriteResolver
{
    public OverwriteDecision Resolve(string targetFile)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new OverwriteDialog(targetFile)
            {
                Owner = Application.Current.MainWindow
            };

            dialog.ShowDialog();
            return dialog.Decision;
        });
    }
}
