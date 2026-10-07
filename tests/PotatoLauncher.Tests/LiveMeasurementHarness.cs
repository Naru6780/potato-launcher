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

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr OpenThread(int access, bool inherit, int id);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [System.Runtime.InteropServices.DllImport("ntdll.dll")] private static extern int NtQueryInformationThread(IntPtr thread, int infoClass, out long info, int length, IntPtr returned);

    private static long StartAddress(int threadId)
    {
        var handle = OpenThread(0x0040 /* QUERY_INFORMATION */, false, threadId);
        if (handle == IntPtr.Zero) return 0;
        try { return NtQueryInformationThread(handle, 9 /* Win32StartAddress */, out var address, 8, IntPtr.Zero) == 0 ? address : 0; }
        finally { CloseHandle(handle); }
    }

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

        if (mode == "nvcap")
        {
            log.AppendLine(NvidiaFrameCap.Describe(NvidiaFrameCap.ForGame()));
        }
        else if (mode == "status")
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
        else if (mode == "watch")
        {
            // Records one client every 2 s while the user toggles its Frame Rate setting; groups samples by the engine's
            // live limit (0 = none, NVIDIA cap) and compares CPU per frame. Samples next to a switch are dropped.
            var name = Environment.GetEnvironmentVariable("POTATO_HARNESS_MAIN") ?? "Artemis";
            var seconds = int.TryParse(Environment.GetEnvironmentVariable("POTATO_HARNESS_SECONDS"), out var s) ? s : 300;
            var target = clients.First(c => Name(c.ProcessId).StartsWith(name, StringComparison.OrdinalIgnoreCase));
            var rows = new List<(DateTime At, short? Limit, double Fps, double Cpu, double System, double Dwm)>();
            static TimeSpan DwmTime() { try { using var d = Process.GetProcessesByName("dwm").First(); return d.TotalProcessorTime; } catch { return TimeSpan.Zero; } }
            var last = LoadSample.Take([target], frames);
            var lastDwm = DwmTime();
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(2000);
                var now = LoadSample.Take([target], frames);
                var dwm = DwmTime();
                var window = LoadSample.Between(last, now);
                var limit = frames.ReadFrame(target.ProcessId, target.StartUtc)?.EngineFrameLimit;
                var dwmPct = (dwm - lastDwm).TotalSeconds / (now.TakenUtc - last.TakenUtc).TotalSeconds / Environment.ProcessorCount * 100;
                rows.Add((DateTime.Now, limit, window.FpsByClient.GetValueOrDefault(target.ProcessId), window.CpuByClient.GetValueOrDefault(target.ProcessId), window.SystemCpu, dwmPct));
                log.AppendLine($"{DateTime.Now:HH:mm:ss} limit={limit?.ToString() ?? "?"} fps={rows[^1].Fps:0.0} cpu={rows[^1].Cpu:0.00}% dwm={dwmPct:0.00}% system={window.SystemCpu:0.0}%");
                last = now;
                lastDwm = dwm;
                File.WriteAllText(output, log.ToString());
            }
            // Keep only samples whose neighbours had the same limit (the switch happened outside them).
            var stable = rows.Where((row, i) => i > 0 && i < rows.Count - 1 && rows[i - 1].Limit == row.Limit && rows[i + 1].Limit == row.Limit).ToList();
            log.AppendLine($"== {Name(target.ProcessId)}: {rows.Count} samples, {stable.Count} away from a switch");
            foreach (var group in stable.GroupBy(row => row.Limit).OrderBy(g => g.Key))
            {
                var fps = group.Average(row => row.Fps);
                var cpu = group.Average(row => row.Cpu);
                var cpuMsPerFrame = cpu / 100 * Environment.ProcessorCount * 1000 / Math.Max(1, fps);
                var sd = Math.Sqrt(group.Average(row => Math.Pow(row.Cpu - cpu, 2)));
                log.AppendLine($"limit {(group.Key == 0 ? "None (NVIDIA cap)" : $"game {group.Key}")}: n={group.Count()} fps {fps:0.0}  cpu {cpu:0.00}% (sd {sd:0.00})  " +
                               $"cpu per frame {cpuMsPerFrame:0.00} ms  dwm {group.Average(row => row.Dwm):0.00}%  system {group.Average(row => row.System):0.0}%");
            }
        }
        else if (mode == "threads")
        {
            // CPU per thread over 10 s, grouped by the module its start address belongs to.
            foreach (var c in clients.Where(c => Name(c.ProcessId).StartsWith("Artemis") || Name(c.ProcessId).StartsWith("Hermes")))
            {
                using var p = Process.GetProcessById(c.ProcessId);
                var modules = p.Modules.Cast<ProcessModule>().Select(m => (Start: (long)m.BaseAddress, End: (long)m.BaseAddress + m.ModuleMemorySize, m.ModuleName)).ToList();
                string ModuleOf(long address) => modules.FirstOrDefault(m => address >= m.Start && address < m.End).ModuleName ?? "(jit/anon)";
                Dictionary<int, (TimeSpan Cpu, string Module)> Snap()
                {
                    var map = new Dictionary<int, (TimeSpan, string)>();
                    p.Refresh();
                    foreach (ProcessThread t in p.Threads)
                    {
                        try { map[t.Id] = (t.TotalProcessorTime, ModuleOf(StartAddress(t.Id))); } catch { }
                    }
                    return map;
                }
                var a = Snap();
                Thread.Sleep(10_000);
                var b = Snap();
                var rows = b.Where(e => a.ContainsKey(e.Key))
                    .Select(e => (e.Key, e.Value.Module, Ms: (e.Value.Cpu - a[e.Key].Cpu).TotalMilliseconds / 10.0))
                    .ToList();
                var total = rows.Sum(r => r.Ms);
                log.AppendLine($"== {Name(c.ProcessId)}: {total / 10.0:0.0}% of one core-second per second ({total / 10.0 / Environment.ProcessorCount:0.00}% of CPU), {rows.Count} threads");
                foreach (var g in rows.GroupBy(r => r.Module).OrderByDescending(g => g.Sum(r => r.Ms)))
                    log.AppendLine($"   {g.Key,-28} {g.Sum(r => r.Ms) / total * 100,5:0.0}%  threads={g.Count()}  busiest={g.Max(r => r.Ms) / 10.0:0.0}% core");
                foreach (var r in rows.OrderByDescending(r => r.Ms).Take(8))
                    log.AppendLine($"     tid {r.Key,-6} {r.Module,-24} {r.Ms / 10.0:0.0}% of a core");
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
