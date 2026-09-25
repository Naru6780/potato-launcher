using System.Diagnostics;

namespace PotatoLauncher;

internal interface IAffinityTarget : IDisposable
{
    int Id { get; }
    DateTime StartUtc { get; }
    long Mask { get; set; }
}

internal sealed class ProcessAffinityTarget(Process process, bool ownsProcess = false) : IAffinityTarget
{
    public int Id => process.Id;
    public DateTime StartUtc => process.StartTime.ToUniversalTime();
    public long Mask { get => process.ProcessorAffinity.ToInt64(); set => process.ProcessorAffinity = new IntPtr(value); }
    public void Dispose() { if (ownsProcess) process.Dispose(); }
}

/// <summary>Own only changes made by this optimizer session, including their original values.</summary>
internal sealed class AffinitySession(Func<int, IAffinityTarget>? open = null)
{
    private sealed record Lease(DateTime Start, long Original, long Applied);
    private readonly Dictionary<int, Lease> leases = [];
    private readonly Func<int, IAffinityTarget> openTarget = open ??
        (id => new ProcessAffinityTarget(Process.GetProcessById(id), true));

    public bool Apply(Process process, long mask, out string error) =>
        Apply(new ProcessAffinityTarget(process), mask, out error);

    internal bool Apply(IAffinityTarget target, long mask, out string error)
    {
        error = "";
        try
        {
            if (mask == 0) throw new ArgumentException("The CPU allocation is empty.");
            var start = target.StartUtc;
            var current = target.Mask;
            var previous = leases.GetValueOrDefault(target.Id);
            if (previous?.Start != start) previous = null;
            if (current != mask)
            {
                target.Mask = mask;
                leases[target.Id] = new(start, previous?.Original ?? current, mask);
            }
            return true;
        }
        catch (Exception ex) { error = $"PID {target.Id}: {ex.Message}"; return false; }
    }

    public string Restore()
    {
        var restored = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var (id, lease) in leases.ToArray())
        {
            try
            {
                using var target = openTarget(id);
                if (target.StartUtc != lease.Start || target.Mask != lease.Applied) skipped++;
                else { target.Mask = lease.Original; restored++; }
                leases.Remove(id);
            }
            catch (ArgumentException) { leases.Remove(id); }
            catch (InvalidOperationException) { leases.Remove(id); }
            catch { failed++; }
        }
        return $"Restored {restored} original CPU allocations; {skipped} changed externally; {failed} failed.";
    }
}
