using System.Runtime.InteropServices;

namespace ShowCuePlayer.Helpers;

/// <summary>
/// Prevent Windows sleep / display-off during live shows (weddings, conferences, YEP).
/// </summary>
public static class SleepPreventer
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private static readonly object Gate = new();
    private static int _holdCount;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    /// <summary>Hold awake (ref-counted). Call Release when done.</summary>
    public static void Hold()
    {
        RunOnConsistentThread(() =>
        {
            lock (Gate)
            {
                if (++_holdCount == 1)
                    SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            }
        });
    }

    public static void Release()
    {
        RunOnConsistentThread(() =>
        {
            lock (Gate)
            {
                if (--_holdCount <= 0)
                {
                    _holdCount = 0;
                    SetThreadExecutionState(ES_CONTINUOUS);
                }
            }
        });
    }

    private static void RunOnConsistentThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess() || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    public static void SetActive(bool active)
    {
        if (active) Hold();
        else Release();
    }
}
