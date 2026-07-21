using ShowCuePlayer.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ShowCuePlayer.Controls;

/// <summary>
/// Code-behind for CueCardControl. Handles context menus, seek interaction,
/// and right-click actions. All logic delegates to CueCardViewModel.
/// </summary>
public partial class CueCardControl : UserControl
{
    public CueCardControl()
    {
        InitializeComponent();
    }

    private CueCardViewModel? VM => DataContext as CueCardViewModel;

    private bool GuardMutation(string action)
    {
        if (Window.GetWindow(this) is not MainWindow mainWindow || mainWindow.ViewModel is null)
            return false;
        return !mainWindow.ViewModel.CanMutateShowConfiguration(action);
    }

    // ─── Progress Slider seek ────────────────────────────────────

    private void ProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => VM?.BeginSeekCommand.Execute(null);

    private void ProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s)
            VM?.EndSeekCommand.Execute(s.Value);
    }

    // ─── Three-dot menu ──────────────────────────────────────────

    private void ThreeDotsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (GuardMutation("mở cài đặt cue")) return;
        
        var settingsVm = new CueSettingsViewModel(VM);
        var settingsWindow = new ShowCuePlayer.Views.CueSettingsWindow
        {
            DataContext = settingsVm,
            Owner = Window.GetWindow(this)
        };
        settingsWindow.ShowDialog();
    }

    // ─── Context menu actions ────────────────────────────────────

    private void SelectCue_Click(object sender, RoutedEventArgs e)
    {
        if (VM is not null && Window.GetWindow(this) is MainWindow mainWindow)
            mainWindow.ViewModel?.SelectCueCommand.Execute(VM);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (GuardMutation("đổi tên cue")) return;
        var dlg = new Dialogs.InputDialog("Rename Cue", "Enter new title:", VM.Title);
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
        {
            VM.Title = dlg.Result;
            VM.Model.Title = dlg.Result;
        }
    }

    private void AssignHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (GuardMutation("gán hotkey cue")) return;
        var dlg = new Dialogs.HotkeyDialog(VM.HotkeyText);
        if (dlg.ShowDialog() == true)
        {
            VM.HotkeyText = dlg.Result;
            VM.Model.HotkeyText = dlg.Result;
        }
    }

    private async void ReplaceAudio_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null || GuardMutation("thay media cue")) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Replace Audio",
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.aac;*.m4a;*.aiff;*.aif;*.ogg;*.opus;*.wma"
        };
        if (dlg.ShowDialog() == true)
        {
            await VM.ReplaceAudioAsync(dlg.FileName);
        }
    }

    private void DuplicateCue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CueCardViewModel vm &&
            Window.GetWindow(this) is MainWindow mw)
        {
            mw.ViewModel?.DuplicateCue(vm);
        }
    }

    private void ChangeColor_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (GuardMutation("đổi màu cue")) return;
        var dlg = new Dialogs.ColorPickerDialog(VM.ColorHex);
        if (dlg.ShowDialog() == true)
            VM.SetColor(dlg.SelectedHex);
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (GuardMutation("mở thuộc tính cue")) return;
        var dlg = new Dialogs.CuePropertiesDialog(VM);
        dlg.ShowDialog();
    }

    private void DeleteCue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CueCardViewModel vm &&
            Window.GetWindow(this) is MainWindow mw)
        {
            mw.ViewModel?.RemoveCue(vm);
        }
    }
}

// ─── Converters ──────────────────────────────────────────────────

/// <summary>Returns Collapsed when bound string is null or empty.</summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Inverts bool → Visibility.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
