using System.Windows;
using FileManage.App.Services;

namespace FileManage.App.Views;

/// <summary>
/// "关于"窗口：版本徽标/构建信息与状态栏版本标签同源（VersionInfo），
/// 版权取程序集 AssemblyCopyright，许可/主页/问题反馈为外链，更新日志内嵌查看。
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);

        VersionBadge.Text = Services.VersionInfo.VersionText;
        VersionLine.Text = $"{Services.VersionInfo.VersionText}（{Services.VersionInfo.BuildDate}）";
        CopyrightLine.Text = Services.VersionInfo.Copyright;
    }

    private void OpenChangelog_Click(object sender, RoutedEventArgs e)
    {
        new ChangelogWindow { Owner = this }.ShowDialog();
    }

    private void OpenLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
