using System.IO;

namespace PotatoLauncher.Studio;

// Framework-thread-owned authorization, independent of Dalamud for regression testing.
internal sealed class BridgeGate(long startTicks)
{
    private readonly HashSet<Guid> seen = [];
    private readonly Queue<Guid> seenOrder = [];
    private ulong contentId;
    public bool Armed { get; private set; }
    public string Session { get; private set; } = Guid.NewGuid().ToString("N");
    public void Reset() { Armed = false; Session = Guid.NewGuid().ToString("N"); contentId = 0; }
    public void Arm(bool loaded, ulong identity) { Reset(); if (loaded && identity != 0) { Armed = true; contentId = identity; } }
    public void Update(bool loaded, ulong identity) { if (Armed && (!loaded || contentId != identity)) Reset(); }
    public string? HeaderError(BridgeRequest request) => request.Version != BridgeProtocol.Version || request.StartTicks != startTicks || request.RequestId == Guid.Empty ? "Protocol or process identity changed." : null;
    public string? Accept(BridgeRequest request)
    {
        if (HeaderError(request) is { } error) return error;
        if (request.Operation != "execute") return "Not an execute request.";
        if (!Armed || request.Session != Session) return "Client is not armed or session changed. Refresh clients.";
        try { BridgeProtocol.Commands(request.Command); } catch (InvalidDataException ex) { return ex.Message; }
        if (!seen.Add(request.RequestId)) return "Duplicate request refused; it may already have executed.";
        seenOrder.Enqueue(request.RequestId);
        while (seenOrder.Count > 512) seen.Remove(seenOrder.Dequeue());
        return null;
    }
}
