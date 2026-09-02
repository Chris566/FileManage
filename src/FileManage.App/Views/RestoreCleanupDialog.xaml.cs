using System.Windows;
using FileManage.App.Services;

namespace FileManage.App.Views;

/// <summary>
/// 文件回覆完成后的清理选项对话框：
/// 可选删除"已分类的文件"（分类目标位置已回覆的源文件）和"分类整理报表"。
/// </summary>
public partial class RestoreCleanupDialog : Window
{
    /// <summary>是否删除已分类的文件。</summary>
    public bool DeleteClassifiedFiles => DeleteFilesCheck.IsChecked == true;

    /// <summary>是否删除分类整理报表。</summary>
    public bool DeleteReport => DeleteReportCheck.IsChecked == true;

    /// <summary>用户是否确认了清理（点击"清理"；"不清理"返回 false）。</summary>
    public bool Confirmed { get; private set; }

    public RestoreCleanupDialog(int restoredCount, string reportFileName)
    {
        InitializeComponent();
        MessageText.Text = Localize.F("S.RestoreCleanup.Message", restoredCount);
        DeleteFilesCheck.Content = Localize.F("S.RestoreCleanup.DeleteFiles", restoredCount);
        Title = $"{Localize.T("S.RestoreCleanup.Title")} - {reportFileName}";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}
