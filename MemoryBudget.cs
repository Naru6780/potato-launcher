using System.Diagnostics;

namespace PotatoLauncher;

internal readonly record struct MemoryClientKey(int Id, DateTime StartUtc);
internal sealed record MemoryClientSample(MemoryClientKey Key, double ResidentMb, double PrivateMb);

internal static class MemoryBudget
{
    internal static bool ShouldTrim(double residentMb, double budgetMb, double totalMb, double availableMb)
    {
        return residentMb > budgetMb || totalMb > 0 && availableMb < Math.Clamp(totalMb * .08, 512, 4096);
    }

    internal static MemoryClientSample? Capture(Process process)
    {
        try
        {
            process.Refresh();
            return new(new(process.Id, process.StartTime.ToUniversalTime()), process.WorkingSet64 / 1048576d,
                process.PrivateMemorySize64 / 1048576d);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
}

// One observation at a time. PID + start time prevents feedback attaching to a reused PID.
internal sealed class MemoryTrimFeedback
{
    private sealed record Observation(MemoryClientKey Key, DateTime Started, double BeforeMb, double InitialSavingMb, double AvailableBeforeMb);
    private Observation? pending;
    private readonly Dictionary<MemoryClientKey, DateTime> cooldowns = [];
    private DateTime globalBackoff;
    public bool IsObserving => pending is not null;
    public string Status { get; private set; } = "No memory trim attempted.";

    internal bool CanTrim(MemoryClientKey key, DateTime now) => pending is null && now >= globalBackoff &&
        now >= cooldowns.GetValueOrDefault(key) && now - key.StartUtc >= TimeSpan.FromSeconds(30);

    internal void Record(MemoryClientSample before, double afterMb, double availableBeforeMb, DateTime now)
    {
        pending = new(before.Key, now, before.ResidentMb, Math.Max(0, before.ResidentMb - afterMb), availableBeforeMb);
        cooldowns[before.Key] = now.AddMinutes(2);
        Status = $"Observing PID {before.Key.Id} for 10 seconds after trimming.";
    }

    internal void Failed(MemoryClientKey key, DateTime now)
    {
        cooldowns[key] = now.AddMinutes(5);
        globalBackoff = now.AddSeconds(30);
        Status = $"Could not trim PID {key.Id}; backing off.";
    }

    internal void Observe(IReadOnlyList<MemoryClientSample> clients, double availableMb, DateTime now)
    {
        var live = clients.Select(c => c.Key).ToHashSet();
        foreach (var key in cooldowns.Keys.Where(key => !live.Contains(key)).ToArray()) cooldowns.Remove(key);
        if (pending is not { } sample) return;
        var current = clients.FirstOrDefault(c => c.Key == sample.Key);
        if (current is null)
        {
            pending = null;
            globalBackoff = now.AddSeconds(30);
            Status = "Trim observation ended: client exited or could not be sampled.";
            return;
        }
        if (now - sample.Started < TimeSpan.FromSeconds(10)) return;
        var retainedSaving = sample.BeforeMb - current.ResidentMb;
        var availableGain = availableMb - sample.AvailableBeforeMb;
        // Other clients may be launching simultaneously: available RAM is reported,
        // but cannot by itself attribute success/failure to this client's trim.
        var helpful = retainedSaving >= Math.Max(128, sample.InitialSavingMb * .5);
        cooldowns[sample.Key] = now.AddMinutes(helpful ? 2 : 10);
        globalBackoff = now.AddSeconds(helpful ? 2 : 120);
        pending = null;
        Status = $"PID {sample.Key.Id}: resident reduction after 10s {retainedSaving:0} MB; system available change {availableGain:+0;-0;0} MB. " +
            (helpful ? "Resident savings persisted; client cooldown 2 min." : "Memory rebounded; pause all trims 2 min, this client 10 min.");
    }
}
