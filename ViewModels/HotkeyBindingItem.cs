using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShowCuePlayer.ViewModels;

/// <summary>One OBS-style hotkey row: action name + capture field + clear.</summary>
public sealed partial class HotkeyBindingItem : ObservableObject
{
    public string ActionId { get; }
    public string Category { get; }
    public string DisplayName { get; }

    [ObservableProperty] private string _hotkeyText = string.Empty;
    [ObservableProperty] private bool _isCapturing;

    public string DisplayHotkey => string.IsNullOrWhiteSpace(HotkeyText) ? "Chưa gán" : HotkeyText;

    public HotkeyBindingItem(string actionId, string category, string displayName, string hotkeyText)
    {
        ActionId = actionId;
        Category = category;
        DisplayName = displayName;
        _hotkeyText = hotkeyText ?? string.Empty;
    }

    partial void OnHotkeyTextChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayHotkey));
        IsCapturing = false;
    }

    [RelayCommand]
    private void BeginCapture()
    {
        IsCapturing = true;
    }

    [RelayCommand]
    private void Clear()
    {
        HotkeyText = string.Empty;
        IsCapturing = false;
    }

    public void ApplyCapture(string hotkey)
    {
        HotkeyText = hotkey ?? string.Empty;
        IsCapturing = false;
    }

    public void CancelCapture() => IsCapturing = false;
}
