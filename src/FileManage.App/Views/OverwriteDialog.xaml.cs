using System.ComponentModel;
using System.Windows;
using FileManage.App.Services;
using FileManage.Core.Execution;

namespace FileManage.App.Views;

/// <summary>
/// 覆盖询问对话框（Ask 策略）。Esc/直接关闭视为跳过。
/// </summary>
public partial class OverwriteDialog : Window
{
    public OverwriteDecision Decision { get; private set; } = OverwriteDecision.Skip;

    public OverwriteDialog(string targetFile)
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);
        TargetPathText.Text = targetFile;
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e) => Complete(OverwriteDecision.Overwrite);

    private void OverwriteAll_Click(object sender, RoutedEventArgs e) => Complete(OverwriteDecision.OverwriteAll);

    private void Skip_Click(object sender, RoutedEventArgs e) => Complete(OverwriteDecision.Skip);

    private void SkipAll_Click(object sender, RoutedEventArgs e) => Complete(OverwriteDecision.SkipAll);

    private void Complete(OverwriteDecision decision)
    {
        Decision = decision;
        DialogResult = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 通过按钮关闭时已设置 DialogResult；其他方式保持 Skip
        base.OnClosing(e);
    }
}
