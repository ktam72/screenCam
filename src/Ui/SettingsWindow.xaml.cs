// REQ-007: View の code-behind は DataContext 設定のみ。ロジックは ViewModel に置く
namespace ScreenCam.Ui;

using System.Windows;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
