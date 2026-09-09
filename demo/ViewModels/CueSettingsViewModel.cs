using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShowCuePlayer.Models;
using System.Windows;
using Microsoft.Win32;
using System.IO;

namespace ShowCuePlayer.ViewModels;

public sealed partial class CueSettingsViewModel : ObservableObject
{
    private readonly CueCardViewModel _parent;

    [ObservableProperty]
    private CueModel _tempModel;

    public PlaybackMode[] PlaybackModes { get; } = new[]
    {
        PlaybackMode.OneShot,
        PlaybackMode.Loop,
        PlaybackMode.Repeat,
        PlaybackMode.AutoFollow,
        PlaybackMode.AutoContinue
    };

    public CueSettingsViewModel(CueCardViewModel parent)
    {
        _parent = parent;
        
        // Clone the original model to edit
        _tempModel = new CueModel
        {
            Title = parent.Model.Title,
            ColorHex = parent.Model.ColorHex,
            FilePath = parent.Model.FilePath,
            IsLooping = parent.Model.IsLooping,
            FadeInSeconds = parent.Model.FadeInSeconds,
            FadeOutSeconds = parent.Model.FadeOutSeconds,
            CueInPoint = parent.Model.CueInPoint,
            CueOutPoint = parent.Model.CueOutPoint,
            CrossfadeSeconds = parent.Model.CrossfadeSeconds,
            Pan = parent.Model.Pan,
            DisplayArtwork = parent.Model.DisplayArtwork,
            SyncChannels = parent.Model.SyncChannels,
            Notes = parent.Model.Notes,
            PlaybackMode = parent.Model.PlaybackMode
        };
    }

    [RelayCommand]
    private void Browse()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.aiff;*.flac;*.ogg;*.wma;*.aac",
            Title = "Select Audio File"
        };
        if (ofd.ShowDialog() == true)
        {
            TempModel.FilePath = ofd.FileName;
            OnPropertyChanged(nameof(TempModel));
        }
    }

    [RelayCommand]
    private void Save(Window window)
    {
        // Apply back to parent
        _parent.ApplySettings(TempModel);

        // If file changed, we would need to reload audio, but for now we just close
        if (_parent.Model.FilePath != TempModel.FilePath)
        {
            _ = _parent.ReplaceAudioAsync(TempModel.FilePath);
        }

        window.Close();
    }

    [RelayCommand]
    private void Cancel(Window window)
    {
        window.Close();
    }
}
