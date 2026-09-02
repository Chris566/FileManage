using System.Windows;
using FileManage.App.Services;

namespace FileManage.App.Views;

/// <summary>更新日志查看器：内容来自程序集内嵌资源 CHANGELOG.md（每次 CI 打包前由 git tag 重建，含新版本）。</summary>
public partial class ChangelogWindow : Window
{
    public ChangelogWindow()
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        ContentBox.Text = ChangelogLoader.LoadFull();
    }
}
