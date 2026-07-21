using CommunityToolkit.Mvvm.ComponentModel;
using ShowCuePlayer.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ShowCuePlayer.ViewModels;

public sealed partial class PlaylistViewModel : ObservableObject
{
    public PlaylistModel Model { get; }
    public ObservableCollection<CueCardViewModel> Cues { get; } = new();

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _hasPlayingCue;

    public PlaylistType Type => Model.Type;
    public bool IsVideo => Type == PlaylistType.Video;
    public bool IsKaraoke => Type == PlaylistType.Karaoke;

    public PlaylistViewModel(PlaylistModel model)
    {
        Model = model;
        _name = model.Name;
        Cues.CollectionChanged += OnCuesCollectionChanged;
    }

    partial void OnNameChanged(string value)
    {
        Model.Name = value;
    }

    private void OnCuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (CueCardViewModel c in e.OldItems)
                c.PropertyChanged -= OnCuePropertyChanged;
        if (e.NewItems != null)
            foreach (CueCardViewModel c in e.NewItems)
                c.PropertyChanged += OnCuePropertyChanged;
        RefreshPlayingFlag();
    }

    private void OnCuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CueCardViewModel.IsPlaying)
            or nameof(CueCardViewModel.IsPaused)
            or nameof(CueCardViewModel.Status))
            RefreshPlayingFlag();
    }

    public void RefreshPlayingFlag()
    {
        HasPlayingCue = Cues.Any(c => c.IsPlaying || c.IsPaused);
    }
}
