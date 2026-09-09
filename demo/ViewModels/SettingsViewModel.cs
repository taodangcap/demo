using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShowCuePlayer.AudioEngine;
using ShowCuePlayer.Helpers;
using ShowCuePlayer.Models;
using ShowCuePlayer.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ShowCuePlayer.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IAudioEngine _audio;

    [ObservableProperty] private double _masterVolume;
    [ObservableProperty] private double _crossfadeSeconds;
    [ObservableProperty] private bool _autoSaveEnabled;
    [ObservableProperty] private bool _preloadNextCue;
    [ObservableProperty] private bool _stopAllOnEscape;
    [ObservableProperty] private string _accentColorHex = "#6C63FF";
    [ObservableProperty] private string _defaultImportFolder = string.Empty;
    [ObservableProperty] private string _emergencySafeSceneImagePath = string.Empty;
    [ObservableProperty] private AudioDeviceInfo? _selectedDevice;
    [ObservableProperty] private HotkeyBindingItem? _capturingItem;

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<HotkeyBindingItem> HotkeyBindings { get; } = new();
    public double[] CrossfadeOptions { get; } = new double[] { 0, 0.5, 1, 2, 3, 5, 10 };

    public SettingsViewModel(ISettingsService settings, IAudioEngine audio)
    {
        _settings = settings;
        _audio = audio;
        LoadFromSettings();
        LoadDevices();
        LoadHotkeys();
    }

    private void LoadFromSettings()
    {
        var s = _settings.Current;
        s.EnsureHotkeys();
        MasterVolume = s.MasterVolume;
        CrossfadeSeconds = s.CrossfadeSeconds;
        AutoSaveEnabled = s.AutoSaveEnabled;
        PreloadNextCue = s.PreloadNextCue;
        StopAllOnEscape = s.StopAllOnEscape;
        AccentColorHex = s.AccentColorHex;
        DefaultImportFolder = s.DefaultImportFolder;
        EmergencySafeSceneImagePath = s.EmergencySafeSceneImagePath;
    }

    private void LoadDevices()
    {
        OutputDevices.Clear();
        foreach (var d in _audio.GetOutputDevices())
            OutputDevices.Add(d);
        SelectedDevice = OutputDevices.FirstOrDefault(d => d.IsDefault)
                         ?? OutputDevices.FirstOrDefault();
    }

    private void LoadHotkeys()
    {
        var s = _settings.Current;
        s.EnsureHotkeys();
        HotkeyBindings.Clear();
        CapturingItem = null;

        foreach (var (id, category, name) in HotkeyActions.Catalog)
        {
            s.Hotkeys.TryGetValue(id, out var text);
            var item = new HotkeyBindingItem(id, category, name, text ?? string.Empty);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HotkeyBindingItem.IsCapturing) && item.IsCapturing)
                {
                    // Only one capture field at a time
                    foreach (var other in HotkeyBindings)
                        if (!ReferenceEquals(other, item))
                            other.CancelCapture();
                    CapturingItem = item;
                }
                else if (e.PropertyName == nameof(HotkeyBindingItem.IsCapturing) && !item.IsCapturing
                         && ReferenceEquals(CapturingItem, item))
                {
                    CapturingItem = null;
                }
            };
            HotkeyBindings.Add(item);
        }
    }

    /// <summary>Called from Settings window PreviewKeyDown while capturing.</summary>
    public bool TryCaptureKey(Key key, ModifierKeys modifiers)
    {
        if (CapturingItem is null) return false;

        // Ignore pure modifiers
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
            return true; // swallow but wait for real key

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            CapturingItem.CancelCapture();
            CapturingItem = null;
            return true;
        }

        var text = HotkeyUtil.Format(key, modifiers);
        if (string.IsNullOrEmpty(text)) return true;

        // Conflict check among settings list
        var conflict = HotkeyBindings.FirstOrDefault(h =>
            !ReferenceEquals(h, CapturingItem)
            && !string.IsNullOrEmpty(h.HotkeyText)
            && string.Equals(h.HotkeyText, text, StringComparison.OrdinalIgnoreCase));

        if (conflict is not null)
        {
            System.Windows.MessageBox.Show(
                $"Phím '{text}' đã gán cho:\n{conflict.DisplayName}",
                "Trùng hotkey",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return true;
        }

        CapturingItem.ApplyCapture(text);
        CapturingItem = null;
        return true;
    }

    [RelayCommand]
    private void ResetHotkeys()
    {
        var defaults = HotkeyActions.CreateDefaults();
        foreach (var item in HotkeyBindings)
        {
            defaults.TryGetValue(item.ActionId, out var text);
            item.HotkeyText = text ?? string.Empty;
            item.CancelCapture();
        }
        CapturingItem = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _settings.Current;
        s.MasterVolume = MasterVolume;
        s.CrossfadeSeconds = CrossfadeSeconds;
        s.AutoSaveEnabled = AutoSaveEnabled;
        s.PreloadNextCue = PreloadNextCue;
        s.StopAllOnEscape = StopAllOnEscape;
        s.AccentColorHex = AccentColorHex;
        s.DefaultImportFolder = DefaultImportFolder;
        s.EmergencySafeSceneImagePath = EmergencySafeSceneImagePath?.Trim() ?? string.Empty;
        if (SelectedDevice is not null)
        {
            s.AudioDeviceIndex = SelectedDevice.Index;
            s.AudioDeviceName = SelectedDevice.Name;
        }

        s.EnsureHotkeys();
        foreach (var item in HotkeyBindings)
            s.Hotkeys[item.ActionId] = item.HotkeyText ?? string.Empty;

        await _settings.SaveAsync();
    }

    [RelayCommand]
    private void BrowseImportFolder()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Default Import Folder",
            UseDescriptionForTitle = true,
            SelectedPath = DefaultImportFolder
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DefaultImportFolder = dlg.SelectedPath;
    }

    [RelayCommand]
    private void BrowseEmergencySafeSceneImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Emergency Safe Scene Image",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*",
            FileName = EmergencySafeSceneImagePath
        };
        if (dlg.ShowDialog() == true)
            EmergencySafeSceneImagePath = dlg.FileName;
    }

    [RelayCommand]
    private void ClearEmergencySafeSceneImage()
        => EmergencySafeSceneImagePath = string.Empty;
}
