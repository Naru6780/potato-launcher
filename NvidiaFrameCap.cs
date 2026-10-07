using System.Runtime.InteropServices;

namespace PotatoLauncher;

/// <summary>
/// Reads (never writes) the NVIDIA driver's "Max Frame Rate" for the game, from the FFXIV application profile or, when
/// that profile does not set it, the global profile. nvapi64.dll ships with every NVIDIA driver; elsewhere this is null.
/// Potato only lets the main run with the in-game limit off when this cap exists, so the main can never run uncapped.
/// </summary>
internal static class NvidiaFrameCap
{
    internal const uint MaxFrameRateSettingId = 0x10835002; // FRL_FPS_ID ("Max Frame Rate")
    private const int Ok = 0;
    private const int IncompatibleStructVersion = -9;
    private const int SettingSize = 12320; // NVDRS_SETTING_V1
    private const int SettingCurrentValueOffset = 8220;
    private static readonly (int Size, int Version)[] ApplicationVersions = [(20492, 4), (16396, 3), (16392, 2), (12296, 1)];

    private static readonly object Sync = new();
    private static (DateTime At, int? Fps) cache = (DateTime.MinValue, null);

    /// <summary>Max Frame Rate applying to ffxiv_dx11.exe: the fps value, 0 when off, null when it cannot be read.</summary>
    public static int? ForGame()
    {
        lock (Sync)
        {
            if (DateTime.UtcNow - cache.At < TimeSpan.FromSeconds(30)) return cache.Fps;
            int? value;
            try { value = Read("ffxiv_dx11.exe"); }
            catch { value = null; }
            cache = (DateTime.UtcNow, value);
            return value;
        }
    }

    public static string Describe(int? fps) => fps switch
    {
        null => "NVIDIA Max Frame Rate: not readable (no NVIDIA driver?)",
        0 => "NVIDIA Max Frame Rate for FFXIV: off",
        var value => $"NVIDIA Max Frame Rate for FFXIV: {value}"
    };

    private static int? Read(string exe)
    {
        if (!NativeLibrary.TryLoad("nvapi64.dll", out var library)) return null;
        var query = Marshal.GetDelegateForFunctionPointer<QueryInterface>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
        T Function<T>(uint id) where T : Delegate
        {
            var pointer = query(id);
            if (pointer == IntPtr.Zero) throw new EntryPointNotFoundException($"nvapi 0x{id:X8}");
            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }

        if (Function<NoArgs>(0x0150E828)() != Ok) return null; // NvAPI_Initialize
        var createSession = Function<OutHandle>(0x0694D52E);
        var destroySession = Function<Handle>(0xDAD9CFF8);
        var loadSettings = Function<Handle>(0x375DBD6B);
        var findApplication = Function<FindApplicationByName>(0xEEE566B2);
        var globalProfile = Function<HandleOutHandle>(0x617BFF9F);
        var getSetting = Function<GetSetting>(0x73BF8338);

        if (createSession(out var session) != Ok) return null;
        var name = Marshal.AllocHGlobal(4096);
        var application = Marshal.AllocHGlobal(ApplicationVersions[0].Size);
        var setting = Marshal.AllocHGlobal(SettingSize);
        try
        {
            if (loadSettings(session) != Ok) return null;
            WriteUnicode(name, exe);

            var profile = IntPtr.Zero;
            foreach (var (size, version) in ApplicationVersions)
            {
                Clear(application, ApplicationVersions[0].Size);
                Marshal.WriteInt32(application, size | (version << 16));
                var status = findApplication(session, name, out profile, application);
                if (status == IncompatibleStructVersion) { profile = IntPtr.Zero; continue; }
                if (status != Ok) profile = IntPtr.Zero;
                break;
            }

            if (profile != IntPtr.Zero && ReadDword(getSetting, session, profile, setting) is uint fromApp) return (int)fromApp;
            if (globalProfile(session, out var global) == Ok && ReadDword(getSetting, session, global, setting) is uint fromGlobal) return (int)fromGlobal;
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(application);
            Marshal.FreeHGlobal(setting);
            destroySession(session);
        }
    }

    private static uint? ReadDword(GetSetting getSetting, IntPtr session, IntPtr profile, IntPtr setting)
    {
        Clear(setting, SettingSize);
        Marshal.WriteInt32(setting, SettingSize | (1 << 16));
        return getSetting(session, profile, MaxFrameRateSettingId, setting) == Ok
            ? (uint)Marshal.ReadInt32(setting, SettingCurrentValueOffset)
            : null;
    }

    private static void WriteUnicode(IntPtr buffer, string text)
    {
        Clear(buffer, 4096);
        var chars = text.ToCharArray();
        Marshal.Copy(chars, 0, buffer, Math.Min(chars.Length, 2047));
    }

    private static void Clear(IntPtr buffer, int size)
    {
        for (var offset = 0; offset < size; offset += 8) Marshal.WriteInt64(buffer, offset, 0);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OutHandle(out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Handle(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleOutHandle(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindApplicationByName(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr application);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSetting(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);
}
