using ShowCuePlayer.Models;

namespace ShowCuePlayer.Services;

/// <summary>Contract for settings persistence.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync();
    Task SaveAsync();
    Task SaveDebouncedAsync(int delayMs = 400);
    event EventHandler SettingsChanged;
}

public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly Database.Repositories.ISettingsRepository _repo;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;
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
        CancelPendingDebounce();
        await _saveLock.WaitAsync();
        try
        {
            await _repo.SaveAsync(Current);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public Task SaveDebouncedAsync(int delayMs = 400)
    {
        CancelPendingDebounce();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                {
                    await SaveAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when new keystroke cancels previous
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in debounced save: {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_debounceCts, cts))
                {
                    _debounceCts = null;
                }
                cts.Dispose();
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private void CancelPendingDebounce()
    {
        var prev = Interlocked.Exchange(ref _debounceCts, null);
        if (prev != null)
        {
            try
            {
                prev.Cancel();
                prev.Dispose();
            }
            catch
            {
                // Ignore disposal exceptions
            }
        }
    }

    public void Dispose()
    {
        CancelPendingDebounce();
        _saveLock.Dispose();
    }
}
