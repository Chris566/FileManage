using System.Windows;
using System.Windows.Input;

namespace FileManage.App.Views;

/// <summary>
/// 轻量文本输入对话框（预设新建 / 复制 / 重命名共用）。
/// </summary>
public partial class PromptDialog : Window
{
    public PromptDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { AnswerBox.Focus(); AnswerBox.SelectAll(); };
        AnswerBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnOk(this, new RoutedEventArgs());
            }
        };
    }

    /// <summary>显示输入对话框；返回输入文本（取消返回 null）。</summary>
    public static string? Show(Window? owner, string title, string label, string initial = "")
    {
        var dialog = new PromptDialog
        {
            Title = title
        };
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        dialog.LabelText.Text = label;
        dialog.AnswerBox.Text = initial;
        return dialog.ShowDialog() == true ? dialog.AnswerBox.Text.Trim() : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
