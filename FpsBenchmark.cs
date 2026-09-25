using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace PotatoLauncher;

internal sealed record FpsMeasurement(int ProcessId, int Frames, double Seconds, double AverageFps, double P95FrameMs);

internal static class FpsBenchmark
{
    // PresentMon is optional and runs externally through ETW, without a game plugin.
    public static async Task<(string Path, IReadOnlyList<FpsMeasurement> Results)> CaptureAsync(string executable, CancellationToken token)
    {
        var folder = Path.Combine(MainForm.PersistentDataRoot(), "benchmarks");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"ffxiv-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.csv");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "--process_name", "ffxiv_dx11.exe", "--output_file", path,
            "--timed", "30", "--terminate_after_timed", "--no_console_stats", "--v1_metrics",
            "--session_name", $"Potato-{Guid.NewGuid():N}" }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("PresentMon could not be started.");
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(50));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            throw;
        }
        var detail = await errors;
        await output;
        if (process.ExitCode != 0 || !File.Exists(path))
            throw new IOException($"PresentMon did not produce a capture (exit {process.ExitCode}). {detail}".Trim());
        var results = await Task.Run(() => Read(path), token);
        return (path, results);
    }

    internal static IReadOnlyList<FpsMeasurement> Read(string path)
    {
        using var reader = File.OpenText(path);
        return Read(reader);
    }

    internal static IReadOnlyList<FpsMeasurement> Read(TextReader reader)
    {
        using var parser = new TextFieldParser(reader);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        var header = parser.ReadFields() ?? throw new IOException("The capture is empty.");
        var pidColumn = Array.FindIndex(header, name => name.Equals("ProcessID", StringComparison.OrdinalIgnoreCase));
        var intervalColumn = Array.FindIndex(header, name => name.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
        var chainColumn = Array.FindIndex(header, name => name.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase));
        if (pidColumn < 0 || intervalColumn < 0 || chainColumn < 0)
            throw new IOException("Use PresentMon console with --v1_metrics for this capture.");
        var samples = new Dictionary<(int Pid, string Chain), List<double>>();
        var count = 0;
        while (!parser.EndOfData)
        {
            if (++count > 2_000_000) throw new IOException("Capture is too large; use a 30-second capture.");
            var row = parser.ReadFields();
            if (row is null || row.Length <= Math.Max(chainColumn, Math.Max(pidColumn, intervalColumn))) continue;
            if (!int.TryParse(row[pidColumn], out var pid) ||
                !double.TryParse(row[intervalColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) ||
                !double.IsFinite(ms) || ms <= 0) continue;
            var key = (pid, row[chainColumn]);
            if (!samples.TryGetValue(key, out var frames)) samples[key] = frames = [];
            frames.Add(ms);
        }
        // Do not add together unrelated swap chains and inflate a client's FPS.
        return samples.GroupBy(pair => pair.Key.Pid).Select(group =>
        {
            var primary = group.OrderByDescending(pair => pair.Value.Count).First().Value;
            var sorted = primary.Order().ToArray();
            var duration = primary.Sum();
            return new FpsMeasurement(group.Key, primary.Count, duration / 1000,
                primary.Count * 1000d / duration, sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * .95) - 1)]);
        }).OrderBy(result => result.ProcessId).ToArray();
    }
}
