using System.Diagnostics;
using System.IO;
using System.Text;

namespace PotatoLauncher.Tests;

// Local measurement only: does nothing unless POTATO_HARNESS is set.
public class LiveMeasurementHarness
{
    private static List<ClientRef> Clients() => Process.GetProcessesByName("ffxiv_dx11")
        .Select(p => { using (p) return new ClientRef(p.Id, p.StartTime.ToUniversalTime()); })
        .OrderBy(c => c.StartUtc).ToList();

    private static string Name(int pid) { try { using var p = Process.GetProcessById(pid); return p.MainWindowTitle; } catch { return "?"; } }

    [Fact]
    public void Measure()
    {
        var mode = Environment.GetEnvironmentVariable("POTATO_HARNESS");
        var output = Environment.GetEnvironmentVariable("POTATO_HARNESS_OUT");
        if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(output)) return;
        var log = new StringBuilder();
        var clients = Clients();
        var frames = new ExternalGameState();
        var topology = CpuTopology.Detect();
        log.AppendLine(topology.Describe());

        if (mode == "status")
        {
            var start = LoadSample.Take(clients, frames);
            Thread.Sleep(10_000);
            var window = LoadSample.Between(start, LoadSample.Take(clients, frames));
            log.AppendLine($"system CPU {window.SystemCpu:0.0}%  clients {window.ClientCpu:0.0}%");
            foreach (var c in clients)
            {
                var limit = frames.ReadFrame(c.ProcessId, c.StartUtc)?.EngineFrameLimit;
                log.AppendLine($"{Name(c.ProcessId),-32} fps {(window.FpsByClient.TryGetValue(c.ProcessId, out var f) ? f.ToString("0.0") : "-"),6}  cpu {window.CpuByClient.GetValueOrDefault(c.ProcessId):0.00}%  limit {limit?.ToString() ?? "?"}");
            }
        }
        else if (mode == "placement")
        {
            // Same test the Optimizer runs. Main = the client named in POTATO_HARNESS_MAIN, else the oldest.
            var mainName = Environment.GetEnvironmentVariable("POTATO_HARNESS_MAIN") ?? "";
            var main = clients.FirstOrDefault(c => Name(c.ProcessId).StartsWith(mainName, StringComparison.OrdinalIgnoreCase)) ?? clients[0];
            var modes = new[] { CpuPlacementMode.Off, CpuPlacementMode.ReserveMain, CpuPlacementMode.Lanes };
            var test = new PlacementTest(modes, rounds: 2, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20), 60, main.ProcessId);
            CpuPlacementMode? applied = null;
            try
            {
                while (!test.Done)
                {
                    if (test.CurrentMode is CpuPlacementMode current && current != applied)
                    {
                        var plan = CpuPlacementPlanner.Plan(topology, current, clients.Select(c => c.ProcessId).ToList(), main.ProcessId);
                        foreach (var (pid, mask) in plan) { using var p = Process.GetProcessById(pid); p.ProcessorAffinity = new IntPtr(mask); }
                        applied = current;
                        log.AppendLine($"{DateTime.Now:HH:mm:ss} {current}: " + string.Join(" ", plan.Select(e => $"{Name(e.Key).Split(' ')[0]}={CpuTopology.FormatMask(e.Value, topology.AllMask)}")));
                    }
                    test.Tick(DateTime.UtcNow, Clients());
                    Thread.Sleep(1000);
                }
            }
            finally
            {
                foreach (var c in clients) { try { using var p = Process.GetProcessById(c.ProcessId); p.ProcessorAffinity = new IntPtr(topology.AllMask); } catch { } }
            }
            log.AppendLine($"main = {Name(main.ProcessId)}");
            log.AppendLine(test.Status);
        }
        File.WriteAllText(output, log.ToString());
    }
}
