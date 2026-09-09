using ShowCuePlayer.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace ShowCuePlayer.Views;

public partial class SettingsView : Window
{
    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    public SettingsView(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.InvokeAsync(Close);
    }

    /// <summary>OBS-style: when a hotkey row is capturing, bind key here.</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm?.CapturingItem is null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Vm.TryCaptureKey(key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }
}
