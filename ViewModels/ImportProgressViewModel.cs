using CommunityToolkit.Mvvm.ComponentModel;

namespace ShowCuePlayer.ViewModels;

public sealed partial class ImportProgressViewModel : ObservableObject
{
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _processed;
    [ObservableProperty] private int _skipped;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private double _progressPercent;

    public void Update(Services.ImportProgress p)
    {
        Total = p.Total;
        Processed = p.Processed;
        Skipped = p.Skipped;
        CurrentFile = System.IO.Path.GetFileName(p.CurrentFile);
        IsComplete = p.IsComplete;
        ProgressPercent = p.Total > 0 ? p.Processed * 100.0 / p.Total : 0;
    }
}
