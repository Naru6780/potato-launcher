using System.Runtime.InteropServices;

namespace PotatoLauncher.Tests;

public class OptionalRenderCircuitTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResourceFailureDisablesRenderingWithoutRetry(bool outOfMemory)
    {
        var circuit = new OptionalRenderCircuit();
        Assert.False(circuit.TryRender(() => throw (outOfMemory
            ? new OutOfMemoryException() : new ExternalException())));
        Assert.True(circuit.Failed);
        var attempted = false;
        Assert.False(circuit.TryRender(() => attempted = true));
        Assert.False(attempted);
    }

    [Fact]
    public void SuccessfulFramesContinue()
    {
        var circuit = new OptionalRenderCircuit();
        var frames = 0;
        for (var i = 0; i < 50; i++) Assert.True(circuit.TryRender(() => frames++));
        Assert.Equal(50, frames);
        Assert.False(circuit.Failed);
    }

    [Fact]
    public void ProgrammingErrorsAreNotSilentlySwallowed()
    {
        var circuit = new OptionalRenderCircuit();
        Assert.Throws<InvalidOperationException>(() => circuit.TryRender(() => throw new InvalidOperationException()));
    }
}
