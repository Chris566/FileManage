using System.Windows;
using FileManage.App.Services;

namespace FileManage.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 按 %AppData%/FileManage/settings.json 应用主题与语言
        UIStateService.Initialize(
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileManage"));

        base.OnStartup(e);
    }
}

