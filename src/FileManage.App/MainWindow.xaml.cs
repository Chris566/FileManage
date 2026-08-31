using System.Windows;
using FileManage.App.Services;
using FileManage.App.ViewModels;

namespace FileManage.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UIStateService.AttachTitleBar(this);

        DataContext = new MainViewModel(
            AppServices.Scanner,
            AppServices.NameEngine,
            AppServices.ConflictDetector,
            AppServices.Executor,
            AppServices.UndoManager,
            AppServices.UndoStore,
            new DialogOverwriteResolver());

        Closed += OnMainWindowClosed;
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SaveSessionState();
        }
    }
}
