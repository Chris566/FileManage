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

        // 启动后异步检查更新（不阻塞 UI；有新版本时弹出对话框）
        _ = CheckForUpdatesAsync(silent: true);
    }

    /// <summary>
    /// 异步检查更新。silent=true 时仅在有新版本时弹窗；false 时无新版本也提示。
    /// </summary>
    public static async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            var info = await UpdateChecker.CheckAsync();

            if (info is null)
            {
                if (!silent)
                {
                    MessageBox.Show(
                        Localize.T("S.Update.CheckFailed"),
                        Localize.T("S.Update.Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            if (!UpdateChecker.IsNewer(info.Version, VersionInfo.Version))
            {
                if (!silent)
                {
                    MessageBox.Show(
                        Localize.F("S.Update.AlwaysLatest", VersionInfo.VersionText),
                        Localize.T("S.Update.Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            // 有新版本，弹出更新对话框
            Current?.Dispatcher.Invoke(() =>
            {
                var window = new Views.UpdateWindow(info, VersionInfo.Version)
                {
                    Owner = Current?.MainWindow
                };
                window.ShowDialog();
            });
        }
        catch
        {
            if (!silent)
            {
                MessageBox.Show(
                    Localize.T("S.Update.CheckFailed"),
                    Localize.T("S.Update.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
