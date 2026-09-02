using System.Diagnostics;
using System.IO;
using System.Windows;
using FileManage.App.Services;

namespace FileManage.App.Views;

/// <summary>
/// 更新对话框：显示版本对比和更新日志，支持下载安装。
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _updateInfo;
    private readonly string _currentVersion;

    public UpdateWindow(UpdateInfo info, string currentVersion)
    {
        _updateInfo = info;
        _currentVersion = currentVersion;
        InitializeComponent();
        LoadContent();
    }

    private void LoadContent()
    {
        TitleText.Text = Localize.T("S.Update.NewVersionAvailable");
        CurrentVersionText.Text = "v" + _currentVersion;
        LatestVersionText.Text = "v" + _updateInfo.Version;

        // 合并在线 Release 正文 + 内嵌 CHANGELOG 对应版本兜底：
        //   - 无网/API 失败时：嵌入 changelog 段落仍存在（打包时由 tag message 写入的版本说明）
        //   - 有网时：在线正文先显示，嵌入 changelog 作参考
        var onlineBody = (string.IsNullOrWhiteSpace(_updateInfo.Body) || _updateInfo.Body == _updateInfo.Name)
            ? ""
            : _updateInfo.Body.Trim();

        var embeddedSection = ChangelogLoader.LoadVersionSection(_updateInfo.Version).Trim();
        var sections = new List<string>(2);
        if (!string.IsNullOrEmpty(onlineBody))
        {
            sections.Add(onlineBody);
        }
        if (!string.IsNullOrEmpty(embeddedSection) &&
            !string.Equals(onlineBody, embeddedSection, StringComparison.Ordinal))
        {
            sections.Add(embeddedSection);
        }

        ChangelogText.Text = sections.Count == 0
            ? (string.IsNullOrEmpty(_updateInfo.Name) ? "更新日志不可用。" : _updateInfo.Name)
            : string.Join("\n\n", sections);
    }

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        UIStateService.AttachTitleBar(this);
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        var asset = _updateInfo.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip"));

        if (asset is null)
        {
            MessageBox.Show(
                Localize.T("S.Update.NoAsset"),
                Localize.T("S.Update.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            DownloadButton.IsEnabled = true;
            ProgressPanel.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            ProgressText.Text = Localize.T("S.Update.Downloading");

            var progress = new Progress<int>(p =>
            {
                DownloadProgress.Value = p;
                ProgressText.Text = $"{p}%";
            });

            var tempPath = await UpdateInstaller.DownloadAsync(
                asset.BrowserDownloadUrl, progress);

            ProgressText.Text = Localize.T("S.Update.Installing");

            var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                ?? Environment.ProcessPath
                ?? "";

            if (string.IsNullOrEmpty(currentExe))
            {
                MessageBox.Show(
                    Localize.T("S.Update.CannotLocateExe"),
                    Localize.T("S.Update.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // 安装并重启（此调用会关闭当前进程）
            UpdateInstaller.InstallAndRestart(tempPath, currentExe);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(Localize.T("S.Update.DownloadFailed"), ex.Message),
                Localize.T("S.Update.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            DownloadButton.IsEnabled = true;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnOpenBrowser(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_updateInfo.HtmlUrl) { UseShellExecute = true });
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
