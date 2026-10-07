using System.Runtime.InteropServices;

namespace PotatoLauncher;

// GPU load as the NVIDIA driver reports it (the same number as nvidia-smi and vendor dashboards).
// Windows' per-engine "3D" counters under-report work such as DLSS / neural rendering, so they are only a fallback.
// nvml.dll ships with every NVIDIA driver; on other GPUs this simply reports null.
internal static class NvidiaGpuUsage
{
    private static readonly object Sync = new();
    private static bool initialized;
    private static bool unavailable;

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    // Busiest NVIDIA GPU, percent (0-100), or null when NVML is not available.
    public static double? GetUtilizationPercent()
    {
        lock (Sync)
        {
            if (unavailable) return null;
            try
            {
                if (!initialized)
                {
                    if (nvmlInit_v2() != 0) { unavailable = true; return null; }
                    initialized = true;
                }
                if (nvmlDeviceGetCount_v2(out var count) != 0 || count == 0) return null;
                double? busiest = null;
                for (uint index = 0; index < count; index++)
                {
                    if (nvmlDeviceGetHandleByIndex_v2(index, out var device) != 0) continue;
                    if (nvmlDeviceGetUtilizationRates(device, out var rates) != 0) continue;
                    busiest = Math.Max(busiest ?? 0, Math.Min(100, rates.Gpu));
                }
                return busiest;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                unavailable = true;
                return null;
            }
        }
    }

    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
}
