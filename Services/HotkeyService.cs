using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace ShowCuePlayer.Services;

/// <summary>Represents a registered global hotkey.</summary>
public record HotkeyRegistration(Guid CueId, string HotkeyText, Key Key, ModifierKeys Modifiers);

/// <summary>Contract for global hotkey management.</summary>
public interface IHotkeyService : IDisposable
{
    bool Register(Guid cueId, string hotkeyText, Key key, ModifierKeys modifiers);
    void Unregister(Guid cueId);
    bool HasConflict(Key key, ModifierKeys modifiers, Guid? excludeCueId = null);
    IReadOnlyList<HotkeyRegistration> GetAll();
    event EventHandler<Guid> HotkeyPressed;
}

/// <summary>
/// Global low-level keyboard hook using WH_KEYBOARD_LL.
/// Works even when the application is minimized or in background.
/// Safely unregisters on dispose.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private readonly ILogger<HotkeyService> _logger;
    private readonly List<HotkeyRegistration> _registrations = new List<HotkeyRegistration>();
    private nint _hookHandle;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;

    public event EventHandler<Guid>? HotkeyPressed;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
        _proc = HookCallback;
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc,
            NativeMethods.GetModuleHandle(null), 0);
        _logger.LogInformation("Global keyboard hook installed: {Handle}", _hookHandle);
    }

    public bool Register(Guid cueId, string hotkeyText, Key key, ModifierKeys modifiers)
    {
        if (HasConflict(key, modifiers, cueId)) return false;
        Unregister(cueId);
        _registrations.Add(new HotkeyRegistration(cueId, hotkeyText, key, modifiers));
        return true;
    }

    public void Unregister(Guid cueId)
        => _registrations.RemoveAll(r => r.CueId == cueId);

    public bool HasConflict(Key key, ModifierKeys modifiers, Guid? excludeCueId = null)
        => _registrations.Any(r =>
            r.Key == key && r.Modifiers == modifiers &&
            (excludeCueId == null || r.CueId != excludeCueId));

    public IReadOnlyList<HotkeyRegistration> GetAll() => _registrations.AsReadOnly();

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && wParam == NativeMethods.WM_KEYDOWN)
        {
            var kbStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var key = KeyInterop.KeyFromVirtualKey((int)kbStruct.vkCode);
            var modifiers = GetCurrentModifiers();

            foreach (var reg in _registrations)
            {
                if (reg.Key == key && reg.Modifiers == modifiers)
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        () => HotkeyPressed?.Invoke(this, reg.CueId));
                    break;
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        var mods = ModifierKeys.None;
        if (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) < 0) mods |= ModifierKeys.Shift;
        if (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) < 0) mods |= ModifierKeys.Control;
        if (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) < 0) mods |= ModifierKeys.Alt;
        if (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) < 0 ||
            NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) < 0) mods |= ModifierKeys.Windows;
        return mods;
    }

    public void Dispose()
    {
        if (_hookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }
    }

    private static class NativeMethods
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;

        public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public nint dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, nint hMod, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(nint hhk);

        [DllImport("user32.dll")]
        public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }
}
