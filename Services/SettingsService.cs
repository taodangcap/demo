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
    private readonly object _debounceGate = new();
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;
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
        if (_disposed) return;
        CancelPendingDebounce();
        await _saveLock.WaitAsync();
        try
        {
            if (_disposed) return;
            await _repo.SaveAsync(Current);
        }
        finally
        {
            _saveLock.Release();
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task SaveDebouncedAsync(int delayMs = 400)
    {
        CancellationTokenSource cts;
        CancellationToken token;
        lock (_debounceGate)
        {
            if (_disposed) return Task.CompletedTask;
            _debounceCts?.Cancel();
            cts = new CancellationTokenSource();
            _debounceCts = cts;
            token = cts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token);
                if (!token.IsCancellationRequested && !_disposed)
                    await SaveAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when new change supersedes previous debounce
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in debounced save: {ex.Message}");
            }
            finally
            {
                lock (_debounceGate)
                {
                    if (ReferenceEquals(_debounceCts, cts))
                        _debounceCts = null;
                }
                cts.Dispose();
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private void CancelPendingDebounce()
    {
        CancellationTokenSource? prev;
        lock (_debounceGate)
        {
            prev = _debounceCts;
            _debounceCts = null;
        }
        if (prev is null) return;
        try
        {
            prev.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Task already completed and disposed this CTS
        }
    }

    public void Dispose()
    {
        lock (_debounceGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        CancelPendingDebounce();
        _saveLock.Dispose();
    }
}
