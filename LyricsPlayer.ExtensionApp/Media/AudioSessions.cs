using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LyricsPlayer.ExtensionApp.Media;

/// <summary>One app that has an audio session, like a row in the Windows volume mixer.</summary>
/// <param name="Key">Stable id used in settings: the exe file name in lower case ("cloudmusic.exe").</param>
/// <param name="Aumid">Package AppUserModelId when the process is a packaged app, else null.</param>
/// <param name="IsSounding">The meter shows sound right now.</param>
/// <param name="IsActive">At least one of its sessions is active (playing a stream, maybe silent).</param>
public sealed record AudioApp(string Key, string Name, string? ExePath, string? Aumid, bool IsSounding, bool IsActive);

/// <summary>
/// Enumerates audio sessions with Core Audio (IAudioSessionManager2) on every active output
/// device, the same data the volume mixer shows. Safe to call from any thread; returns plain data.
/// </summary>
public static class AudioSessions
{
    private const float SoundThreshold = 0.001f;

    public static Task<IReadOnlyList<AudioApp>> GetAppsAsync() => Task.Run(GetApps);

    public static IReadOnlyList<AudioApp> GetApps()
    {
        var byKey = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessId;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(0 /* eRender */, 1 /* DEVICE_STATE_ACTIVE */, out var devices);
            devices.GetCount(out var deviceCount);
            for (uint d = 0; d < deviceCount; d++)
            {
                try
                {
                    devices.Item(d, out var device);
                    var iid = typeof(IAudioSessionManager2).GUID;
                    device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var managerObj);
                    var manager = (IAudioSessionManager2)managerObj;
                    manager.GetSessionEnumerator(out var sessions);
                    sessions.GetCount(out var count);
                    for (var i = 0; i < count; i++)
                    {
                        try
                        {
                            sessions.GetSession(i, out var control);
                            if (control is not IAudioSessionControl2 c2) continue;
                            if (c2.IsSystemSoundsSession() == 0) continue; // S_OK = system sounds
                            c2.GetState(out var state);
                            if (state == 2) continue; // expired
                            c2.GetProcessId(out var pid);
                            if (pid == 0 || pid == self) continue;

                            float peak = 0;
                            if (control is IAudioMeterInformation meter)
                            {
                                try { meter.GetPeakValue(out peak); } catch { }
                            }
                            string? display = null;
                            try { c2.GetDisplayName(out display); } catch { }

                            var app = Describe((int)pid, display, peak > SoundThreshold, state == 1);
                            if (app is null) continue;
                            if (byKey.TryGetValue(app.Key, out var prev))
                            {
                                app = prev with
                                {
                                    IsSounding = prev.IsSounding || app.IsSounding,
                                    IsActive = prev.IsActive || app.IsActive,
                                };
                            }
                            byKey[app.Key] = app;
                        }
                        catch { /* a session can vanish while we read it */ }
                    }
                }
                catch { /* device unplugged mid-way */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio session enumeration failed: {ex.Message}");
        }

        return byKey.Values
            .OrderByDescending(a => a.IsSounding)
            .ThenByDescending(a => a.IsActive)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static AudioApp? Describe(int pid, string? sessionName, bool sounding, bool active)
    {
        var path = ImagePath(pid);
        string exe;
        if (path is not null) exe = Path.GetFileName(path);
        else
        {
            try { exe = Process.GetProcessById(pid).ProcessName + ".exe"; }
            catch { return null; }
        }
        var key = exe.ToLowerInvariant();
        var aumid = PackageAumid(pid);

        string? name = null;
        if (!string.IsNullOrWhiteSpace(sessionName) && !sessionName.StartsWith('@')) name = sessionName.Trim();
        name ??= AppNames.TryKnown(aumid ?? "") ?? AppNames.TryKnown(key);
        if (name is null && path is not null)
        {
            try
            {
                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim();
            }
            catch { }
        }
        name ??= Path.GetFileNameWithoutExtension(exe);
        return new AudioApp(key, name, path, aumid, sounding, active);
    }

    // ---------------- process helpers ----------------

    private const uint ProcessQueryLimitedInformation = 0x1000;

    private static string? ImagePath(int pid)
    {
        var h = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    private static string? PackageAumid(int pid)
    {
        var h = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            uint len = 0;
            if (GetApplicationUserModelId(h, ref len, null) != 122 /* ERROR_INSUFFICIENT_BUFFER */ || len == 0)
                return null; // 15703 = not a packaged app
            var sb = new StringBuilder((int)len);
            return GetApplicationUserModelId(h, ref len, sb) == 0 ? sb.ToString() : null;
        }
        catch { return null; }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr h, ref uint length, StringBuilder? aumid);

    // ---------------- Core Audio COM ----------------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IntPtr client);
        void UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        void OpenPropertyStore(int access, out IntPtr properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int state);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager
        void GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        // IAudioSessionManager2
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        void RegisterSessionNotification(IntPtr notification);
        void UnregisterSessionNotification(IntPtr notification);
        void RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
        void UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eventContext);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr eventContext);
        void GetGroupingParam(out Guid grouping);
        void SetGroupingParam(ref Guid grouping, IntPtr eventContext);
        void RegisterAudioSessionNotification(IntPtr client);
        void UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        void SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        void GetPeakValue(out float peak);
        void GetMeteringChannelCount(out uint count);
        void GetChannelsPeakValues(uint count, IntPtr peaks);
        void QueryHardwareSupport(out uint mask);
    }
}
