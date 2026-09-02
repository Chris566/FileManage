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
        // 便携版：旧版（单文件版）%AppData% 数据一次性迁入 <exe目录>/Data（目标已存在则跳过）
        Infrastructure.Storage.PortableDataMigrator.Migrate(
            AppPaths.LegacyAppDataRoot, AppPaths.DataRoot);

        // 清理上次更新遗留的备份目录
        TryCleanupUpdateBackup();

        // 按 <exe目录>/Data/settings.json 应用主题与语言
        UIStateService.Initialize(AppPaths.DataRoot);

        // 启动即完成规则预设 v1→v2 迁移（现有规则无损转为系统默认预设）
        AppServices.LoadPresetDocument();

        base.OnStartup(e);

        // 启动后异步检查更新（不阻塞 UI；有新版本时弹出对话框）
        _ = CheckForUpdatesAsync(silent: true);
    }

    /// <summary>清理上次更新遗留的程序备份目录（失败静默，不影响启动）。</summary>
    private static void TryCleanupUpdateBackup()
    {
        try
        {
            if (System.IO.Directory.Exists(AppPaths.UpdateBackupDir))
            {
                System.IO.Directory.Delete(AppPaths.UpdateBackupDir, recursive: true);
            }
        }
        catch
        {
            // 占用或权限问题留待下次启动清理
        }
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
