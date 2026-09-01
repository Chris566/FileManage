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

        // 启动即完成规则预设 v1→v2 迁移（现有规则无损转为系统默认预设）
        AppServices.LoadPresetDocument();

        base.OnStartup(e);
    }
}
