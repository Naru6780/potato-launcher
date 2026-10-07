using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PotatoLauncher.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void Recorder_WritesOneJsonLinePerIntervalWithPerClientCpu()
    {
        var root = Path.Combine(Path.GetTempPath(), "potato-diag-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var recorder = new DiagnosticsRecorder(root);
            using var self = Process.GetCurrentProcess();
            var clients = new[] { self };
            DiagnosticsClient Describe(Process p, double cpu) => new("Test@World", p.Id, 58.2, 60, "0-5", cpu, "Normal", false, false, "Main");
            var start = DateTime.UtcNow;
            recorder.Record(start, clients, Describe, new { placement = "ReserveMain" });          // primes counters, writes nothing
            Assert.False(recorder.Due(start.AddSeconds(5)));
            recorder.Record(start.AddSeconds(16), clients, Describe, new { placement = "ReserveMain" });

            var files = Directory.GetFiles(root, "metrics-*.jsonl");
            var lines = File.ReadAllLines(Assert.Single(files));
            using var json = JsonDocument.Parse(Assert.Single(lines));
            var record = json.RootElement;
            Assert.True(record.TryGetProperty("powerPlan", out _));
            Assert.Equal("ReserveMain", record.GetProperty("extra").GetProperty("placement").GetString());
            var client = Assert.Single(record.GetProperty("clients").EnumerateArray().ToList());
            Assert.Equal("Test@World", client.GetProperty("name").GetString());
            Assert.Equal(60, client.GetProperty("limit").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void PowerPlan_IsReadable() => Assert.False(string.IsNullOrWhiteSpace(SystemDiagnostics.PowerPlan()));
}
