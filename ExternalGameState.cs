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

internal sealed record ExternalGameSnapshot(WorldReadiness State, string Detail,
    string CharacterName = "", ulong ContentId = 0, uint TerritoryId = 0, ushort HomeWorld = 0);

/// <summary>Read-only, out-of-process probe. No injection, memory writes, input or plugin calls.</summary>
internal sealed class ExternalGameState
{
    // Deliberately pinned: never apply an old struct layout to a newly patched executable.
    internal const string SupportedSha256 = "5BBC501DD5C7F22FD61A11D08C25356041D878DB7CD83203ADAE393E4DFACC44";
    private static readonly ConcurrentDictionary<string, Lazy<Addresses>> Profiles = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Addresses(int PlayerState, int LocalPlayer, int GameMain, int Conditions);

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
            Find("48 8D 0D ?? ?? ?? ?? 66 2B D8"));
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
