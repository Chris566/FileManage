using System.Windows;
using FileManage.App.Services;
using FileManage.App.ViewModels;

namespace FileManage.App.Views;

/// <summary>
/// 规则管理窗口：列表编辑 + 条件编辑 + 导入导出。
/// </summary>
public partial class RuleEditorWindow : Window
{
    public RuleEditorWindow()
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);
        DataContext = new RuleEditorViewModel(AppServices.LoadRules());
    }
}
