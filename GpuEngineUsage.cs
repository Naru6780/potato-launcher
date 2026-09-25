namespace PotatoLauncher;

internal sealed record GpuEngineReading(int ProcessId, string Engine, double Percent);

internal static class GpuEngineUsage
{
    internal static (double Total, IReadOnlyDictionary<int, double> ByProcess) Aggregate(IEnumerable<GpuEngineReading> readings)
    {
        var valid = readings.Where(reading => double.IsFinite(reading.Percent) && reading.Percent >= 0).ToArray();
        // Engine includes adapter LUID and physical engine index. Independent engines
        // operate concurrently: summing them can falsely report GPU saturation.
        var total = valid.GroupBy(reading => reading.Engine, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Sum(reading => reading.Percent)).DefaultIfEmpty(0).Max();
        var byProcess = valid.GroupBy(reading => reading.ProcessId).ToDictionary(group => group.Key,
            group => Math.Round(Math.Min(100, group.GroupBy(reading => reading.Engine, StringComparer.OrdinalIgnoreCase)
                .Max(engine => engine.Sum(reading => reading.Percent))), 1));
        return (Math.Round(Math.Min(100, total), 1), byProcess);
    }
}
