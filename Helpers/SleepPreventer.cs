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

    private static int _holdCount;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    /// <summary>Hold awake (ref-counted). Call Release when done.</summary>
    public static void Hold()
    {
        if (Interlocked.Increment(ref _holdCount) == 1)
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
    }

    public static void Release()
    {
        if (Interlocked.Decrement(ref _holdCount) <= 0)
        {
            Interlocked.Exchange(ref _holdCount, 0);
            SetThreadExecutionState(ES_CONTINUOUS);
        }
    }

    public static void SetActive(bool active)
    {
        if (active) Hold();
        else Release();
    }
}
