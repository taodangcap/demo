using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using ShowCuePlayer.Models;

namespace ShowCuePlayer.Services;

/// <summary>
/// Enumerates active Windows render endpoints and selects the endpoint used by Chromium/WebView2.
/// The selected endpoint becomes the Windows default render device for all three audio roles.
/// </summary>
public static class WindowsAudioOutputService
{
    public static IReadOnlyList<AudioOutputEndpoint> GetActiveOutputs()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = defaultDevice.ID;
        }
        catch { /* Windows can temporarily have no default endpoint. */ }

        var outputs = new List<AudioOutputEndpoint>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                outputs.Add(new AudioOutputEndpoint(
                    device.ID,
                    string.IsNullOrWhiteSpace(device.FriendlyName) ? device.ID : device.FriendlyName,
                    string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)));
            }
        }
        return outputs.OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void SetDefaultOutput(string endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
            throw new ArgumentException("Audio endpoint is required.", nameof(endpointId));

        var client = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            SetRole(client, endpointId, ERole.Console);
            SetRole(client, endpointId, ERole.Multimedia);
            SetRole(client, endpointId, ERole.Communications);
        }
        finally
        {
            if (Marshal.IsComObject(client)) Marshal.FinalReleaseComObject(client);
        }
    }

    private static void SetRole(IPolicyConfig client, string endpointId, ERole role)
    {
        var result = client.SetDefaultEndpoint(endpointId, role);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient
    {
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod, IntPtr period, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
