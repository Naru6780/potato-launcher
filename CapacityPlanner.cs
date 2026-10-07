namespace PotatoLauncher;

internal sealed record CapacityClientSample(double CpuPercent, long WorkingSetBytes, long PrivateBytes, double? Fps);

internal sealed record CapacityEstimate(int? AdditionalClients, string LimitingFactor, string Summary);

// Answers the one question that matters when stacking clients: can this PC take another one at full frame rate,
// and if not, what runs out first? Per-client costs are medians of the running clients, so one client loading
// a zone does not swing the estimate.
internal static class CapacityPlanner
{
    // Leave room for the OS, Discord, the launcher and frame-time spikes.
    internal const double CpuBudgetPercent = 90;
    internal const long MemoryReserveBytes = 4L * 1024 * 1024 * 1024;

    public static CapacityEstimate Estimate(IReadOnlyList<CapacityClientSample> clients, double systemCpuPercent,
        long availablePhysicalBytes, long commitAvailableBytes, double targetFps)
    {
        if (clients.Count == 0) return new(null, "", "Launch clients to estimate capacity.");

        var measured = clients.Where(client => client.Fps.HasValue).ToList();
        var belowCap = measured.Count(client => client.Fps!.Value < targetFps - 3);
        if (belowCap > 0)
        {
            return new(0, systemCpuPercent >= CpuBudgetPercent ? "CPU" : "frame rate",
                $"{belowCap} client{(belowCap == 1 ? " is" : "s are")} below {targetFps:0} FPS — no room for more.");
        }

        var cpuPerClient = Median(clients.Select(client => client.CpuPercent));
        var workingSetPerClient = Median(clients.Select(client => (double)client.WorkingSetBytes));
        var commitPerClient = Median(clients.Select(client => (double)client.PrivateBytes));

        var byCpu = cpuPerClient <= 0 ? int.MaxValue : (int)Math.Floor(Math.Max(0, CpuBudgetPercent - systemCpuPercent) / cpuPerClient);
        var byRam = workingSetPerClient <= 0 ? int.MaxValue : (int)Math.Floor(Math.Max(0, availablePhysicalBytes - MemoryReserveBytes) / workingSetPerClient);
        var byCommit = commitPerClient <= 0 || commitAvailableBytes <= 0 ? int.MaxValue : (int)Math.Floor(Math.Max(0, commitAvailableBytes - MemoryReserveBytes) / commitPerClient);

        var (additional, factor) = new[] { (byCpu, "CPU"), (byRam, "RAM"), (byCommit, "RAM + pagefile") }.MinBy(item => item.Item1);
        const double gb = 1024d * 1024 * 1024;
        var cost = factor switch
        {
            "CPU" => $"~{cpuPerClient:0.#}% CPU per client",
            "RAM" => $"~{workingSetPerClient / gb:0.#} GB RAM in use per client",
            _ => $"~{commitPerClient / gb:0.#} GB committed per client"
        };
        var summary = additional switch
        {
            int.MaxValue => "Not enough data yet.",
            0 => $"At capacity: {factor} is the limit ({cost}).",
            _ => $"Room for about {additional} more client{(additional == 1 ? "" : "s")}: {factor} runs out first ({cost})."
        };
        return new(additional == int.MaxValue ? null : additional, factor, summary);
    }

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0) return 0;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
