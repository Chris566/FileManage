using System.IO;
using System.Reflection;
using System.Windows;
using FileManage.App.Services;

namespace FileManage.App.Views;

/// <summary>更新日志查看器：内容来自程序集内嵌资源 CHANGELOG.md（LogicalName 固定名）。</summary>
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
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
        {
            ContentBox.Text = Application.Current.TryFindResource("S.Changelog.Unavailable") as string
                ?? "更新日志内容不可用。";
            return;
        }

        using var reader = new StreamReader(stream);
        ContentBox.Text = reader.ReadToEnd();
    }
}
