using ShowCuePlayer.Models;

namespace ShowCuePlayer.Services;

/// <summary>Contract for settings persistence.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync();
    Task SaveAsync();
    event EventHandler SettingsChanged;
}

public sealed class SettingsService : ISettingsService
{
    private readonly Database.Repositories.ISettingsRepository _repo;
    public AppSettings Current { get; private set; } = new();
    public event EventHandler? SettingsChanged;

    public SettingsService(Database.Repositories.ISettingsRepository repo) => _repo = repo;

    public async Task LoadAsync()
    {
        Current = await _repo.LoadAsync();
        Current.EnsureHotkeys();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync()
    {
        await _repo.SaveAsync(Current);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
