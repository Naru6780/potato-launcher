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
        // GDI+ reports most bitmap/LockBits failures as ArgumentException ("Parameter is not valid").
        catch (Exception ex) when (ex is OutOfMemoryException or ExternalException or Win32Exception or ArgumentException)
        {
            Failed = true;
            return false;
        }
    }
}
