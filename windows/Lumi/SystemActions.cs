using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Lumi;

/// A system action shown as a row in the bar; it runs only on Enter or a click.
public sealed record ActionRow(string Icon, string Title, string Subtitle, Action Run, bool Background = true);

/// System commands on Windows, through public Windows APIs: Core Audio for the volume, the Personalize
/// registry values for the theme (what Settings writes), LockWorkStation, and EnumWindows for windows.
/// Wi-Fi isn't offered: switching the radio needs admin rights or the WinRT Radio API.
public static class SystemActions
{
    /// The rows for what was typed: none when it isn't a command or Windows can't do it here.
    public static List<ActionRow> For(string text)
    {
        var rows = new List<ActionRow>();
        if (SystemCommands.Parse(text) is not { } command) return rows;
        switch (command.Kind)
        {
            case SystemActionKind.Volume:
                var level = command.Volume;
                rows.Add(new ActionRow("🔊", S.ActionVolume(level.ToString()), S.ActionNote, () => Audio.Set(level / 100f, false)));
                break;
            case SystemActionKind.Mute:
                rows.Add(new ActionRow("🔇", S.ActionMute, S.ActionNote, () => Audio.Set(null, true)));
                break;
            case SystemActionKind.Unmute:
                rows.Add(new ActionRow("🔈", S.ActionUnmute, S.ActionNote, () => Audio.Set(null, false)));
                break;
            case SystemActionKind.DarkTheme:
                rows.Add(new ActionRow("🌙", S.ActionDark, S.ActionNote, () => SetDark(true)));
                break;
            case SystemActionKind.LightTheme:
                rows.Add(new ActionRow("☀️", S.ActionLight, S.ActionNote, () => SetDark(false)));
                break;
            case SystemActionKind.Lock:
                rows.Add(new ActionRow("🔒", S.ActionLock, S.ActionNote, () => LockWorkStation()));
                break;
            case SystemActionKind.SwitchWindow:
                foreach (var (handle, title) in OpenWindows().Where(w => SystemCommands.TitleMatches(w.Title, command.Window)).Take(5))
                    rows.Add(new ActionRow("🪟", title, S.ActionWindow, () => Raise(handle), Background: false));
                break;
        }
        return rows;
    }

    /// Runs an action: raising a window must happen on the UI thread while Lumi still has the focus,
    /// everything else runs in the background so the bar closes at once.
    public static void Start(ActionRow row)
    {
        void Go()
        {
            try { row.Run(); }
            catch { /* a failed call leaves things as they were */ }
        }
        if (row.Background) Task.Run(Go);
        else Go();
    }

    // MARK: theme

    private static void SetDark(bool dark)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
        {
            key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        }
        // Tells Explorer and open apps to re-read the colours, as Settings does.
        SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 1000, out _);
    }

    // MARK: windows

    private static List<(IntPtr Handle, string Title)> OpenWindows()
    {
        var list = new List<(IntPtr, string)>();
        var own = System.Diagnostics.Process.GetCurrentProcess().Id;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            if ((GetWindowLongPtr(h, -20 /* GWL_EXSTYLE */).ToInt64() & 0x80 /* WS_EX_TOOLWINDOW */) != 0) return true;
            if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == own) return true;
            var length = GetWindowTextLength(h);
            if (length == 0) return true;
            var buffer = new StringBuilder(length + 1);
            GetWindowText(h, buffer, buffer.Capacity);
            var title = buffer.ToString().Trim();
            if (title.Length > 0 && title != "Program Manager") list.Add((h, title));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static void Raise(IntPtr handle)
    {
        if (IsIconic(handle)) ShowWindow(handle, 9 /* SW_RESTORE */);
        SetForegroundWindow(handle);
    }

    // MARK: Win32

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}

/// The default output device's volume through Core Audio (IAudioEndpointVolume).
internal static class Audio
{
    /// Sets the level (0–1) when given, and the mute state.
    public static void Set(float? level, bool mute)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device));
            try
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var o));
                var volume = (IAudioEndpointVolume)o;
                try
                {
                    var context = Guid.Empty;
                    if (level is float l) volume.SetMasterVolumeLevelScalar(Math.Clamp(l, 0f, 1f), ref context);
                    volume.SetMute(mute, ref context);
                }
                finally { Marshal.ReleaseComObject(volume); }
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, ref Guid context);
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
