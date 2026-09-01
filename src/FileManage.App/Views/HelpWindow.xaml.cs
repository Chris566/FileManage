using System.Windows;
using FileManage.App.Services;

namespace FileManage.App.Views;

/// <summary>
/// 帮助窗口：用户指南 / 常见问题两个选项卡，内容为语言字典静态文本（离线可用、随语言即时切换）；
/// 底部提供项目主页与问题反馈外链；initialTab 决定打开时显示的选项卡（F1 默认指南）。
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow(int initialTab = 0)
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);
        Tabs.SelectedIndex = initialTab;
    }

    private void OpenHomepage_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/Chris566/FileManage");

    private void OpenIssues_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/Chris566/FileManage/issues");

    private static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
}
