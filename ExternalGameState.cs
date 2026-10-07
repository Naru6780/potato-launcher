using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PotatoLauncher;

internal enum WorldReadiness { Unknown, NotInWorld, Loading, InWorld }

internal sealed record FrameSample(uint FrameCounter, float? GameFrameRate, DateTime TakenUtc);

// Turns two frame-counter samples into a real FPS (frames actually completed over wall time). The game's own
// FrameRate is smoothed, so it is only a fallback until a second sample exists.
internal sealed class ClientFpsTracker
{
    private readonly Dictionary<int, FrameSample> previous = [];
    private readonly Dictionary<int, double> latest = [];

    public double? Update(int processId, FrameSample? sample)
    {
        if (sample is null) { previous.Remove(processId); latest.Remove(processId); return null; }
        if (previous.TryGetValue(processId, out var last))
        {
            var seconds = (sample.TakenUtc - last.TakenUtc).TotalSeconds;
            if (seconds >= 0.5)
            {
                var frames = unchecked(sample.FrameCounter - last.FrameCounter);
                if (frames < 100_000) latest[processId] = Math.Round(frames / seconds, 1);
                previous[processId] = sample;
            }
        }
        else
        {
            previous[processId] = sample;
            if (sample.GameFrameRate is float rate) latest[processId] = Math.Round(rate, 1);
        }
        return latest.TryGetValue(processId, out var fps) ? fps : null;
    }

    public void Forget(IEnumerable<int> aliveProcessIds)
    {
        var alive = aliveProcessIds.ToHashSet();
        foreach (var id in previous.Keys.Where(id => !alive.Contains(id)).ToList()) { previous.Remove(id); latest.Remove(id); }
    }
}

internal sealed record ExternalGameSnapshot(WorldReadiness State, string Detail,
    string CharacterName = "", ulong ContentId = 0, uint TerritoryId = 0, ushort HomeWorld = 0);

/// <summary>Read-only, out-of-process probe. No injection, memory writes, input or plugin calls.</summary>
internal sealed class ExternalGameState
{
    // Deliberately pinned: never apply an old struct layout to a newly patched executable.
    internal const string SupportedSha256 = "5BBC501DD5C7F22FD61A11D08C25356041D878DB7CD83203ADAE393E4DFACC44";
    private static readonly ConcurrentDictionary<string, Lazy<Addresses>> Profiles = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Addresses(int PlayerState, int LocalPlayer, int GameMain, int Conditions, int FrameworkPointer);
    private readonly ConcurrentDictionary<int, (DateTime Start, long ModuleBase, Addresses Addresses)> frameTargets = new();

    // Framework fields (FFXIVClientStructs, verified on this build): FrameCounter @0x16D0 (uint), FrameRate @0x17CC (float).
    private const int FrameworkBlockOffset = 0x16C0;
    private const int FrameworkBlockSize = 0x11C;

    /// <summary>Frame counter and the game's own (smoothed) frame rate. Read-only; null when unavailable.</summary>
    public FrameSample? ReadFrame(int processId, DateTime expectedStartUtc)
    {
        try
        {
            if (!frameTargets.TryGetValue(processId, out var target) || target.Start != expectedStartUtc)
            {
                using var process = Process.GetProcessById(processId);
                if (process.ProcessName != "ffxiv_dx11" || process.StartTime.ToUniversalTime() != expectedStartUtc || process.HasExited) return null;
                var module = process.MainModule ?? throw new IOException("Game module is unavailable.");
                var file = new FileInfo(module.FileName);
                var key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
                target = (expectedStartUtc, module.BaseAddress.ToInt64(), Profiles.GetOrAdd(key, _ => new(() => Resolve(file.FullName))).Value);
                frameTargets[processId] = target;
            }
            using var handle = OpenProcess(0x0010 | 0x1000, false, processId);
            if (handle.IsInvalid) return null;
            GameProcessIdentity.Verify(handle, expectedStartUtc);
            var framework = BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(handle, target.ModuleBase + target.Addresses.FrameworkPointer, 8));
            if (framework < 0x10000 || framework > 0x00007FFFFFFFFFFF) return null;
            var block = ReadBytes(handle, framework + FrameworkBlockOffset, FrameworkBlockSize);
            var counter = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x16D0 - FrameworkBlockOffset));
            var rate = BinaryPrimitives.ReadSingleLittleEndian(block.AsSpan(0x17CC - FrameworkBlockOffset));
            return new FrameSample(counter, float.IsFinite(rate) && rate is > 0 and <= 1000 ? rate : null, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or
                                   System.ComponentModel.Win32Exception or UnauthorizedAccessException or NotSupportedException)
        {
            frameTargets.TryRemove(processId, out _);
            return null;
        }
    }

    public ExternalGameSnapshot Read(int processId, DateTime expectedStartUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.ProcessName != "ffxiv_dx11" || process.StartTime.ToUniversalTime() != expectedStartUtc || process.HasExited)
                return new(WorldReadiness.Unknown, "Client exited or process identity changed.");
            var module = process.MainModule ?? throw new IOException("Game module is unavailable.");
            // Cache only by file identity metadata; the factory also verifies the complete executable hash.
            var file = new FileInfo(module.FileName);
            var key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            var addresses = Profiles.GetOrAdd(key, _ => new(() => Resolve(file.FullName))).Value;
            using var handle = OpenProcess(0x0010 | 0x1000, false, processId); // VM_READ + QUERY_LIMITED_INFORMATION
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            GameProcessIdentity.Verify(handle, expectedStartUtc);
            var basis = module.BaseAddress.ToInt64();
            var playerState = ReadBytes(handle, basis + addresses.PlayerState, 0x70);
            var local = BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(handle, basis + addresses.LocalPlayer, 8));
            var main = ReadBytes(handle, basis + addresses.GameMain + 0x40F8, 0x14);
            var conditions = ReadBytes(handle, basis + addresses.Conditions, 112);
            if (playerState[0] > 1 || conditions.Any(value => value > 1) || main[6] > 1)
                return new(WorldReadiness.Unknown, "Game-state layout validation failed.");
            var territory = BinaryPrimitives.ReadUInt32LittleEndian(main.AsSpan(16));
            var loadState = BinaryPrimitives.ReadUInt32LittleEndian(main.AsSpan(8));
            if (playerState[0] == 0 || local == 0)
                return new(WorldReadiness.NotInWorld, "No logged-in local player.");
            if (local < 0x10000 || local > 0x00007FFFFFFFFFFF)
                return new(WorldReadiness.Unknown, "Invalid local-player address.");
            var actor = ReadBytes(handle, local, 0x98);
            var identity = ReadBytes(handle, local + 0x2358, 12);
            var name = ReadName(playerState.AsSpan(1, 64));
            var actorName = ReadName(actor.AsSpan(0x30, 64));
            var contentId = BinaryPrimitives.ReadUInt64LittleEndian(playerState.AsSpan(0x68));
            var homeWorld = BinaryPrimitives.ReadUInt16LittleEndian(identity.AsSpan(10));
            if (string.IsNullOrWhiteSpace(name) || name != actorName || actor[0x90] != 1 || contentId == 0 ||
                contentId != BinaryPrimitives.ReadUInt64LittleEndian(identity) || homeWorld == 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(playerState.AsSpan(0x64)) != BinaryPrimitives.ReadUInt32LittleEndian(actor.AsSpan(0x78)))
                return new(WorldReadiness.Unknown, "Player identity cross-check failed.");
            // Reject snapshots that straddle logout/player replacement.
            if (local != BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(handle, basis + addresses.LocalPlayer, 8)) ||
                !playerState.SequenceEqual(ReadBytes(handle, basis + addresses.PlayerState, 0x70)))
                return new(WorldReadiness.Unknown, "Game state changed during sampling.");
            var loaded = IsLoaded(main[6] == 1, loadState, territory, conditions[45] == 1,
                conditions[51] == 1, conditions[53] == 1, conditions[60] == 1);
            return new(loaded ? WorldReadiness.InWorld : WorldReadiness.Loading,
                loaded ? "Local player and loaded territory confirmed." : "Player present; world transition is not complete.",
                name, contentId, territory, homeWorld);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or
                                   System.ComponentModel.Win32Exception or UnauthorizedAccessException or NotSupportedException)
        {
            return new(WorldReadiness.Unknown, ex.Message);
        }
    }

    internal static bool IsLoaded(bool connected, uint loadState, uint territory, bool betweenAreas,
        bool betweenAreas51, bool loggingOut, bool creatingCharacter) =>
        connected && loadState == 2 && territory > 0 && !betweenAreas && !betweenAreas51 && !loggingOut && !creatingCharacter;

    internal static string ReadName(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        if (end <= 0) return "";
        var name = new UTF8Encoding(false, true).GetString(data[..end]);
        return name.Any(char.IsControl) ? "" : name;
    }

    private static Addresses Resolve(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != SupportedSha256)
            throw new NotSupportedException("Unsupported FFXIV build: readiness detection needs a compatibility update.");
        using var pe = new PEReader(new MemoryStream(bytes, false));
        var section = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
        var text = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData).ToArray();
        int Find(string pattern)
        {
            var offset = FindUnique(text, pattern);
            var rva = checked(section.VirtualAddress + offset + 7 + BinaryPrimitives.ReadInt32LittleEndian(text.AsSpan(offset + 3)));
            if (rva <= 0 || rva >= pe.PEHeaders.PEHeader!.SizeOfImage - 0x5000)
                throw new IOException("Signature resolved outside the game image.");
            return rva;
        }
        return new(Find("48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 75 06 F6 43 18 02"),
            Find("48 8B 2D ?? ?? ?? ?? 75"), Find("48 8D 0D ?? ?? ?? ?? 0F 28 F2 48 89 44 24 ??"),
            Find("48 8D 0D ?? ?? ?? ?? 66 2B D8"),
            // mov rbx, [rip+rel32]: the static holds a Framework* (one dereference).
            Find("48 8B 1D ?? ?? ?? ?? 8B 7C 24"));
    }

    internal static int FindUnique(byte[] data, string pattern)
    {
        var bytes = pattern.Split(' ').Select(token => token == "??" ? (byte?)null : Convert.ToByte(token, 16)).ToArray();
        var found = -1;
        for (var i = 0; i <= data.Length - bytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < bytes.Length; j++)
                if (bytes[j] is byte value && data[i + j] != value) { match = false; break; }
            if (!match) continue;
            if (found != -1) throw new IOException("Ambiguous game signature; detection disabled.");
            found = i;
        }
        return found >= 0 ? found : throw new IOException("Game signature not found; detection disabled.");
    }

    private static byte[] ReadBytes(SafeProcessHandle handle, long address, int count)
    {
        var bytes = new byte[count];
        if (!ReadProcessMemory(handle, new IntPtr(address), bytes, (nuint)count, out var read) || read != (nuint)count)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot read FFXIV state; readiness is unknown.");
        return bytes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, nuint size, out nuint read);
}

/// <summary>One reading never completes a launch. Identity and territory must stay stable.</summary>
internal sealed class WorldReadinessGate
{
    private ExternalGameSnapshot? previous;
    private DateTime? since;
    public bool Observe(ExternalGameSnapshot value, DateTime now)
    {
        if (value.State != WorldReadiness.InWorld || value.ContentId == 0 || value.TerritoryId == 0)
        { previous = null; since = null; return false; }
        if (previous is null || previous.ContentId != value.ContentId || previous.TerritoryId != value.TerritoryId)
            since = now;
        previous = value;
        return since is not null && now - since.Value >= TimeSpan.FromSeconds(3);
    }
}
