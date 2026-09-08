using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PotatoLauncher.Studio;

internal sealed record BridgeRequest(int Version, Guid RequestId, string Operation, long StartTicks, string Session, string Command = "", int IconId = 0);
internal sealed record BridgeReply(int Version, Guid RequestId, bool Ok, string Message, long StartTicks = 0, string Session = "", string Character = "", bool Armed = false, byte[]? Pixels = null, int Width = 0, int Height = 0);

internal static class BridgeProtocol
{
    public const int Version = 1;
    public const int MaxFrame = 1024 * 1024;
    public static string PipeName(int pid) => $"PotatoLauncher.CommandBridge.v1.{pid}";

    public static string[] Commands(string command)
    {
        if (command is null || command.Length > 32000) throw new InvalidDataException("Command is too long.");
        var lines = command.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length is < 1 or > 16) throw new InvalidDataException("Use 1–16 slash-command lines.");
        foreach (var line in lines)
        {
            if (!line.StartsWith('/') || line.StartsWith("//", StringComparison.Ordinal))
                throw new InvalidDataException("Only slash commands are supported; QoLBar // macro directives are not executable here.");
            if (line.Any(char.IsControl) || line.Any(c => c == '\u2028' || c == '\u2029') || Encoding.UTF8.GetByteCount(line) > 500)
                throw new InvalidDataException("Each line must be at most 500 UTF-8 bytes without control characters.");
            if (line.StartsWith("/wait", StringComparison.OrdinalIgnoreCase) && (line.Length == 5 || char.IsWhiteSpace(line[5])) || line.Contains("<wait.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Macro waits are not supported. Use an existing in-game macro command instead.");
        }
        return lines;
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length is < 1 or > MaxFrame) throw new InvalidDataException("Bridge message exceeds size limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaxFrame) throw new InvalidDataException("Invalid bridge frame length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes, new JsonSerializerOptions { MaxDepth = 16 }) ?? throw new InvalidDataException("Empty bridge message.");
    }
}

internal static class BridgeClient
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);

    // Exactly one request, with no retry: a timeout can occur after a command executed.
    public static async Task<BridgeReply> SendAsync(int pid, BridgeRequest request, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var process = Process.GetProcessById(pid);
        if (process.StartTime.ToUniversalTime().Ticks != request.StartTicks) throw new InvalidOperationException("Client process changed. Refresh clients.");
        await using var pipe = new NamedPipeClientStream(".", BridgeProtocol.PipeName(pid), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(1500, deadline.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != (uint)pid)
            throw new InvalidOperationException("Bridge belongs to a different process.");
        await BridgeProtocol.WriteAsync(pipe, request, deadline.Token);
        var reply = await BridgeProtocol.ReadAsync<BridgeReply>(pipe, deadline.Token);
        if (reply.Version != BridgeProtocol.Version || reply.RequestId != request.RequestId || reply.StartTicks != request.StartTicks)
            throw new InvalidDataException("Bridge response identity did not match.");
        return reply;
    }
}

internal sealed class BridgeServer : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    public BridgeServer(int pid, Func<BridgeRequest, CancellationToken, Task<BridgeReply>> handle, Action<Exception> onError)
    {
        worker = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(BridgeProtocol.PipeName(pid), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    var request = await BridgeProtocol.ReadAsync<BridgeRequest>(pipe, deadline.Token);
                    var reply = await handle(request, deadline.Token);
                    await BridgeProtocol.WriteAsync(pipe, reply, deadline.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    onError(ex);
                    // A duplicate endpoint or persistent I/O error must not spin a CPU core.
                    try { await Task.Delay(250, stop.Token); } catch (OperationCanceledException) { }
                }
            }
        });
    }
    public void Dispose()
    {
        stop.Cancel();
        // Never block the framework/UI thread on a worker awaiting that thread.
        _ = worker.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
