using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

// Only used for optional artwork, never to swallow failures in launch or settings logic.
internal sealed class OptionalRenderCircuit
{
    public bool Failed { get; private set; }

    public bool TryRender(Action render)
    {
        if (Failed) return false;
        try { render(); return true; }
        catch (Exception ex) when (ex is OutOfMemoryException or ExternalException or Win32Exception)
        {
            Failed = true;
            return false;
        }
    }
}
