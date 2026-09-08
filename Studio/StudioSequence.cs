using System.IO;

namespace PotatoLauncher.Studio;

internal sealed record StudioStep(string Command, int DelayAfterMs);

internal static class StudioSequence
{
    public static void Validate(IReadOnlyList<StudioStep> steps)
    {
        if (steps.Count is < 1 or > 64) throw new InvalidDataException("Use 1–64 command steps.");
        foreach (var step in steps)
        {
            if (step.DelayAfterMs is < 0 or > 600000) throw new InvalidDataException("Each wait must be 0–600,000 ms (10 minutes).");
            var lines = BridgeProtocol.Commands(step.Command);
            if (lines.Length != 1 || step.Command.Contains('\n')) throw new InvalidDataException("Put exactly one slash command in each step.");
        }
    }

    public static async Task RunAsync(IReadOnlyList<StudioStep> steps, Func<string, CancellationToken, Task> send,
        Func<int, CancellationToken, Task> delay, Action<int, int> progress, CancellationToken token)
    {
        // Validate the entire sequence before submitting its first command.
        Validate(steps);
        var snapshot = steps.ToArray();
        for (var index = 0; index < snapshot.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            progress(index + 1, snapshot.Length);
            await send(snapshot[index].Command, token);
            if (index + 1 < snapshot.Length && snapshot[index].DelayAfterMs > 0)
                await delay(snapshot[index].DelayAfterMs, token);
        }
    }
}
