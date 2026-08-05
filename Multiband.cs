using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace PotatoLauncher;

internal static class MultibandProtocol
{
    public const int Version = 1;
    public const int DefaultPort = 47842;
    public const int MaximumMessageLength = 64 * 1024;
}

internal sealed class MultibandSettings
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = Environment.MachineName;
    public bool ListenEnabled { get; set; }
    public int Port { get; set; } = MultibandProtocol.DefaultPort;
    public List<PairedDevice> PairedDevices { get; set; } = [];
    public List<MultibandLaunchPlan> Plans { get; set; } = [];
}

internal sealed class PairedDevice
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = MultibandProtocol.DefaultPort;
    public string CertificateFingerprint { get; set; } = "";
    public string SharedSecret { get; set; } = "";
    public DateTimeOffset LastSeenUtc { get; set; }
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Host : $"{Name} ({Host})";
}

internal sealed class MultibandLaunchPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New launch plan";
    public string LocalBandId { get; set; } = "";
    public string RemoteDeviceId { get; set; } = "";
    public string RemoteBandId { get; set; } = "";
    public override string ToString() => Name;
}

internal sealed record MultibandBandSummary(string Id, string Name, int AccountCount, string LaunchMode)
{
    public override string ToString() => $"{Name} ({AccountCount})";
}

internal sealed record MultibandAccountStatus(string Name, string Status);

internal sealed record MultibandLaunchProgress(string State, string Detail, IReadOnlyList<MultibandAccountStatus> Accounts)
{
    public static MultibandLaunchProgress Scheduled(string detail) => new("Scheduled", detail, []);
}

internal sealed record MultibandReadiness(bool Ready, string Error)
{
    public static MultibandReadiness Success() => new(true, "");
    public static MultibandReadiness Fail(string error) => new(false, error);
}

internal sealed class MultibandOperationSnapshot
{
    public string OperationId { get; set; } = "";
    public string BandId { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public string Detail { get; set; } = "";
    public List<MultibandAccountStatus> Accounts { get; set; } = [];
    public bool IsTerminal => State is "Completed" or "Cancelled" or "Failed";
}

internal sealed class MultibandRequest
{
    public int ProtocolVersion { get; set; } = MultibandProtocol.Version;
    public string Type { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int DevicePort { get; set; } = MultibandProtocol.DefaultPort;
    public string CertificateFingerprint { get; set; } = "";
    public string Token { get; set; } = "";
    public string PairingCode { get; set; } = "";
    public string BandId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public DateTimeOffset StartAtUtc { get; set; }
}

internal sealed class MultibandResponse
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public int ProtocolVersion { get; set; } = MultibandProtocol.Version;
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int DevicePort { get; set; } = MultibandProtocol.DefaultPort;
    public DateTimeOffset ServerUtc { get; set; }
    public string SharedSecret { get; set; } = "";
    public List<MultibandBandSummary> Bands { get; set; } = [];
    public MultibandOperationSnapshot? Operation { get; set; }

    public static MultibandResponse Ok() => new() { Success = true };
    public static MultibandResponse Fail(string error) => new() { Error = error };
}

internal sealed class MultibandSettingsStore
{
    private readonly string path;
    private readonly object sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MultibandSettingsStore(string path)
    {
        this.path = path;
    }

    public MultibandSettings Load()
    {
        lock (sync)
        {
            try
            {
                if (!File.Exists(path)) return Clean(new MultibandSettings());
                return Clean(JsonSerializer.Deserialize<MultibandSettings>(File.ReadAllText(path)) ?? new MultibandSettings());
            }
            catch
            {
                return Clean(new MultibandSettings());
            }
        }
    }

    public void Save(MultibandSettings settings)
    {
        lock (sync)
        {
            settings = Clean(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
    }

    internal static MultibandSettings Clean(MultibandSettings settings)
    {
        if (!Guid.TryParse(settings.DeviceId, out var deviceId)) deviceId = Guid.NewGuid();
        settings.DeviceId = deviceId.ToString("N");
        settings.DeviceName = string.IsNullOrWhiteSpace(settings.DeviceName) ? Environment.MachineName : settings.DeviceName.Trim();
        settings.Port = settings.Port is >= 1024 and <= 65535 ? settings.Port : MultibandProtocol.DefaultPort;
        settings.PairedDevices ??= [];
        settings.Plans ??= [];

        settings.PairedDevices = settings.PairedDevices
            .Where(peer => peer is not null && Guid.TryParse(peer.DeviceId, out _) && !string.IsNullOrWhiteSpace(peer.SharedSecret))
            .GroupBy(peer => Guid.Parse(peer.DeviceId).ToString("N"), StringComparer.OrdinalIgnoreCase)
            .Select(group => CleanPeer(group.First()))
            .ToList();

        var peerIds = settings.PairedDevices.Select(peer => peer.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var planIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in settings.Plans)
        {
            if (!Guid.TryParse(plan.Id, out var planId) || !planIds.Add(planId.ToString("N")))
            {
                planId = Guid.NewGuid();
                planIds.Add(planId.ToString("N"));
            }
            plan.Id = planId.ToString("N");
            plan.Name = string.IsNullOrWhiteSpace(plan.Name) ? "New launch plan" : plan.Name.Trim();
            plan.LocalBandId = NormalizeGuid(plan.LocalBandId);
            plan.RemoteDeviceId = NormalizeGuid(plan.RemoteDeviceId);
            plan.RemoteBandId = NormalizeGuid(plan.RemoteBandId);
            if (!peerIds.Contains(plan.RemoteDeviceId)) plan.RemoteDeviceId = "";
        }
        return settings;
    }

    private static PairedDevice CleanPeer(PairedDevice peer)
    {
        peer.DeviceId = NormalizeGuid(peer.DeviceId);
        peer.Name = string.IsNullOrWhiteSpace(peer.Name) ? peer.Host.Trim() : peer.Name.Trim();
        peer.Host = peer.Host?.Trim() ?? "";
        peer.Port = peer.Port is >= 1024 and <= 65535 ? peer.Port : MultibandProtocol.DefaultPort;
        peer.CertificateFingerprint = NormalizeFingerprint(peer.CertificateFingerprint);
        peer.SharedSecret = peer.SharedSecret?.Trim() ?? "";
        return peer;
    }

    internal static string NormalizeFingerprint(string? fingerprint)
    {
        return new string((fingerprint ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
    }

    private static string NormalizeGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed.ToString("N") : "";
}

internal sealed class MultibandCertificateStore
{
    private readonly string path;

    public MultibandCertificateStore(string path)
    {
        this.path = path;
    }

    public X509Certificate2 LoadOrCreate(string deviceName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) return Load(File.ReadAllBytes(path));

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Potato Launcher {SanitizeCommonName(deviceName)}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var bytes = created.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, bytes);
        return Load(bytes);
    }

    private static X509Certificate2 Load(byte[] bytes)
    {
        return new X509Certificate2(bytes, (string?)null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }

    private static string SanitizeCommonName(string value)
    {
        var cleaned = new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').Take(40).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Device" : cleaned;
    }
}

internal sealed class MultibandOperation
{
    private readonly object sync = new();
    public string OperationId { get; }
    public string BandId { get; }
    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;
    public CancellationTokenSource Cancellation { get; } = new();
    private MultibandOperationSnapshot snapshot;

    public MultibandOperation(string operationId, string bandId)
    {
        OperationId = operationId;
        BandId = bandId;
        snapshot = new MultibandOperationSnapshot { OperationId = operationId, BandId = bandId, State = "Prepared", Detail = "Ready to launch." };
    }

    public void Update(MultibandLaunchProgress progress)
    {
        lock (sync)
        {
            snapshot = new MultibandOperationSnapshot
            {
                OperationId = OperationId,
                BandId = BandId,
                State = progress.State,
                Detail = progress.Detail,
                Accounts = progress.Accounts.ToList()
            };
        }
    }

    public MultibandOperationSnapshot Snapshot()
    {
        lock (sync)
        {
            return new MultibandOperationSnapshot
            {
                OperationId = snapshot.OperationId,
                BandId = snapshot.BandId,
                State = snapshot.State,
                Detail = snapshot.Detail,
                Accounts = snapshot.Accounts.ToList()
            };
        }
    }
}

internal sealed class MultibandServer : IAsyncDisposable
{
    private readonly MultibandSettings settings;
    private readonly MultibandSettingsStore settingsStore;
    private readonly X509Certificate2 certificate;
    private readonly Func<Task<IReadOnlyList<MultibandBandSummary>>> getBandsAsync;
    private readonly Func<string, Task<MultibandReadiness>> canLaunchAsync;
    private readonly Func<string, DateTimeOffset, Action<MultibandLaunchProgress>, CancellationToken, Task> launchAsync;
    private readonly ConcurrentDictionary<string, MultibandOperation> operations = new(StringComparer.OrdinalIgnoreCase);
    private readonly object pairingSync = new();
    private TcpListener? listener;
    private CancellationTokenSource? serverCancellation;
    private string pairingCode = "";
    private DateTimeOffset pairingExpiresUtc;
    private int pairingFailedAttempts;

    public bool IsRunning => listener is not null;
    public string CertificateFingerprint => certificate.GetCertHashString(HashAlgorithmName.SHA256);
    internal string LastError { get; private set; } = "";

    public MultibandServer(
        MultibandSettings settings,
        MultibandSettingsStore settingsStore,
        X509Certificate2 certificate,
        Func<Task<IReadOnlyList<MultibandBandSummary>>> getBandsAsync,
        Func<string, Task<MultibandReadiness>> canLaunchAsync,
        Func<string, DateTimeOffset, Action<MultibandLaunchProgress>, CancellationToken, Task> launchAsync)
    {
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.certificate = certificate;
        this.getBandsAsync = getBandsAsync;
        this.canLaunchAsync = canLaunchAsync;
        this.launchAsync = launchAsync;
    }

    public Task StartAsync()
    {
        if (listener is not null) return Task.CompletedTask;
        serverCancellation = new CancellationTokenSource();
        listener = new TcpListener(IPAddress.Any, settings.Port);
        listener.Start();
        _ = AcceptLoopAsync(listener, serverCancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cancellation = serverCancellation;
        serverCancellation = null;
        cancellation?.Cancel();
        listener?.Stop();
        listener = null;
        if (cancellation is not null)
        {
            await Task.Yield();
            cancellation.Dispose();
        }
    }

    public string CreatePairingCode()
    {
        lock (pairingSync)
        {
            pairingCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            pairingExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5);
            pairingFailedAttempts = 0;
            return pairingCode;
        }
    }

    public static IReadOnlyList<string> LocalIpv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task AcceptLoopAsync(TcpListener activeListener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await activeListener.AcceptTcpClientAsync(token);
                _ = HandleClientSafelyAsync(client, token);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                LastError = ex.ToString();
                await Task.Delay(500, token);
            }
        }
    }

    private async Task HandleClientSafelyAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                await using var ssl = new SslStream(client.GetStream(), false);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, token);
                using var reader = new StreamReader(ssl, Encoding.UTF8, false, 4096, true);
                await using var writer = new StreamWriter(ssl, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(token);
                MultibandResponse response;
                if (string.IsNullOrWhiteSpace(line) || line.Length > MultibandProtocol.MaximumMessageLength)
                {
                    response = MultibandResponse.Fail("Invalid request.");
                }
                else
                {
                    var request = JsonSerializer.Deserialize<MultibandRequest>(line);
                    response = request is null
                        ? MultibandResponse.Fail("Invalid request.")
                        : await HandleRequestAsync(request, client.Client.RemoteEndPoint as IPEndPoint);
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LastError = ex.ToString(); }
        }
    }

    private async Task<MultibandResponse> HandleRequestAsync(MultibandRequest request, IPEndPoint? remoteEndPoint)
    {
        if (remoteEndPoint is null || !IsPrivateOrLocal(remoteEndPoint.Address)) return MultibandResponse.Fail("Multiband accepts local-network connections only.");
        if (request.ProtocolVersion != MultibandProtocol.Version)
        {
            return MultibandResponse.Fail($"Protocol version mismatch. This PC uses {MultibandProtocol.Version}.");
        }

        if (request.Type.Equals("Pair", StringComparison.OrdinalIgnoreCase))
        {
            return HandlePairRequest(request, remoteEndPoint);
        }

        if (!Authenticate(request)) return MultibandResponse.Fail("This device is not paired or its access key is invalid.");
        TouchPeer(request.DeviceId, remoteEndPoint?.Address.ToString());

        switch (request.Type.ToLowerInvariant())
        {
            case "catalog":
                var bands = await getBandsAsync();
                var catalogResponse = SuccessResponse();
                catalogResponse.Bands = bands.ToList();
                return catalogResponse;
            case "prepare":
                return await PrepareAsync(request);
            case "commit":
                return Commit(request);
            case "status":
                return Status(request.OperationId);
            case "cancel":
                return Cancel(request.OperationId);
            default:
                return MultibandResponse.Fail("Unknown request type.");
        }

    }

    private MultibandResponse HandlePairRequest(MultibandRequest request, IPEndPoint? remoteEndPoint)
    {
        lock (pairingSync)
        {
            if (string.IsNullOrWhiteSpace(pairingCode) || pairingExpiresUtc < DateTimeOffset.UtcNow || !CryptographicEquals(pairingCode, request.PairingCode))
            {
                pairingFailedAttempts++;
                if (pairingFailedAttempts >= 5)
                {
                    pairingCode = "";
                    return MultibandResponse.Fail("Too many incorrect pairing attempts. Generate a new pairing code on the receiving PC.");
                }
                return MultibandResponse.Fail("The pairing code is invalid or expired.");
            }
            if (!Guid.TryParse(request.DeviceId, out var parsedDeviceId)) return MultibandResponse.Fail("The requesting device ID is invalid.");

            var normalizedDeviceId = parsedDeviceId.ToString("N");
            if (normalizedDeviceId.Equals(settings.DeviceId, StringComparison.OrdinalIgnoreCase)) return MultibandResponse.Fail("A PC cannot be paired with itself.");
            var peer = settings.PairedDevices.FirstOrDefault(item => item.DeviceId.Equals(normalizedDeviceId, StringComparison.OrdinalIgnoreCase));
            var sharedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            if (peer is null)
            {
                peer = new PairedDevice { DeviceId = normalizedDeviceId };
                settings.PairedDevices.Add(peer);
            }
            peer.Name = string.IsNullOrWhiteSpace(request.DeviceName) ? remoteEndPoint?.Address.ToString() ?? "Paired PC" : request.DeviceName.Trim();
            peer.Host = remoteEndPoint?.Address.ToString() ?? peer.Host;
            peer.Port = request.DevicePort is >= 1024 and <= 65535 ? request.DevicePort : MultibandProtocol.DefaultPort;
            peer.CertificateFingerprint = MultibandSettingsStore.NormalizeFingerprint(request.CertificateFingerprint);
            peer.SharedSecret = sharedSecret;
            peer.LastSeenUtc = DateTimeOffset.UtcNow;
            settingsStore.Save(settings);
            pairingCode = "";
            pairingFailedAttempts = 0;

            var response = SuccessResponse();
            response.SharedSecret = sharedSecret;
            return response;
        }
    }

    private async Task<MultibandResponse> PrepareAsync(MultibandRequest request)
    {
        if (!Guid.TryParse(request.OperationId, out var operationId) || !Guid.TryParse(request.BandId, out var bandId))
        {
            return MultibandResponse.Fail("The launch request is invalid.");
        }
        var readiness = await canLaunchAsync(bandId.ToString("N"));
        if (!readiness.Ready) return MultibandResponse.Fail(readiness.Error);

        PruneOperations();
        var operation = new MultibandOperation(operationId.ToString("N"), bandId.ToString("N"));
        if (!operations.TryAdd(operation.OperationId, operation))
        {
            var existing = operations[operation.OperationId];
            if (!existing.BandId.Equals(operation.BandId, StringComparison.OrdinalIgnoreCase)) return MultibandResponse.Fail("That operation ID is already assigned to another band.");
            operation = existing;
        }
        var response = SuccessResponse();
        response.Operation = operation.Snapshot();
        return response;
    }

    private MultibandResponse Commit(MultibandRequest request)
    {
        var operationId = NormalizeOperationId(request.OperationId);
        if (!operations.TryGetValue(operationId, out var operation)) return MultibandResponse.Fail("The launch was not prepared or has expired.");
        if (!operation.BandId.Equals(NormalizeOperationId(request.BandId), StringComparison.OrdinalIgnoreCase)) return MultibandResponse.Fail("Prepared band does not match the launch request.");
        if (request.StartAtUtc < DateTimeOffset.UtcNow.AddSeconds(-2) || request.StartAtUtc > DateTimeOffset.UtcNow.AddMinutes(1)) return MultibandResponse.Fail("The synchronized start time is invalid.");

        var existingSnapshot = operation.Snapshot();
        if (existingSnapshot.State is not "Prepared")
        {
            var existingResponse = SuccessResponse();
            existingResponse.Operation = existingSnapshot;
            return existingResponse;
        }

        operation.Update(MultibandLaunchProgress.Scheduled($"Scheduled for {request.StartAtUtc.LocalDateTime:T}."));
        _ = RunOperationAsync(operation, request.StartAtUtc);
        var response = SuccessResponse();
        response.Operation = operation.Snapshot();
        return response;
    }

    private async Task RunOperationAsync(MultibandOperation operation, DateTimeOffset startAtUtc)
    {
        try
        {
            await launchAsync(operation.BandId, startAtUtc, operation.Update, operation.Cancellation.Token);
            if (!operation.Snapshot().IsTerminal) operation.Update(new MultibandLaunchProgress("Completed", "Band launch completed.", operation.Snapshot().Accounts));
        }
        catch (OperationCanceledException)
        {
            operation.Update(new MultibandLaunchProgress("Cancelled", "Band launch cancelled.", operation.Snapshot().Accounts));
        }
        catch (Exception ex)
        {
            operation.Update(new MultibandLaunchProgress("Failed", ex.Message, operation.Snapshot().Accounts));
        }
    }

    private MultibandResponse Status(string operationId)
    {
        if (!operations.TryGetValue(NormalizeOperationId(operationId), out var operation)) return MultibandResponse.Fail("Launch operation not found.");
        var response = SuccessResponse();
        response.Operation = operation.Snapshot();
        return response;
    }

    private MultibandResponse Cancel(string operationId)
    {
        if (!operations.TryGetValue(NormalizeOperationId(operationId), out var operation)) return MultibandResponse.Fail("Launch operation not found.");
        operation.Cancellation.Cancel();
        operation.Update(new MultibandLaunchProgress("Cancelled", "Cancellation requested.", operation.Snapshot().Accounts));
        var response = SuccessResponse();
        response.Operation = operation.Snapshot();
        return response;
    }

    private void PruneOperations()
    {
        var expiredBefore = DateTimeOffset.UtcNow.AddHours(-1);
        var expired = operations.Values
            .Where(operation => operation.CreatedUtc < expiredBefore && operation.Snapshot().IsTerminal)
            .Concat(operations.Count <= 128
                ? []
                : operations.Values.Where(operation => operation.Snapshot().IsTerminal).OrderBy(operation => operation.CreatedUtc).Take(operations.Count - 128))
            .Distinct()
            .ToList();
        foreach (var operation in expired)
        {
            if (!operations.TryRemove(operation.OperationId, out var removed)) continue;
            removed.Cancellation.Dispose();
        }
    }

    private bool Authenticate(MultibandRequest request)
    {
        var peer = settings.PairedDevices.FirstOrDefault(item => item.DeviceId.Equals(request.DeviceId, StringComparison.OrdinalIgnoreCase));
        return peer is not null && CryptographicEquals(peer.SharedSecret, request.Token);
    }

    private void TouchPeer(string deviceId, string? host)
    {
        var peer = settings.PairedDevices.FirstOrDefault(item => item.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        if (peer is null) return;
        var now = DateTimeOffset.UtcNow;
        var hostChanged = !string.IsNullOrWhiteSpace(host) && !peer.Host.Equals(host, StringComparison.OrdinalIgnoreCase);
        if (hostChanged) peer.Host = host!;
        var shouldPersist = hostChanged || now - peer.LastSeenUtc > TimeSpan.FromSeconds(30);
        peer.LastSeenUtc = now;
        if (shouldPersist) settingsStore.Save(settings);
    }

    private MultibandResponse SuccessResponse()
    {
        return new MultibandResponse
        {
            Success = true,
            DeviceId = settings.DeviceId,
            DeviceName = settings.DeviceName,
            DevicePort = settings.Port,
            ServerUtc = DateTimeOffset.UtcNow
        };
    }

    private static bool CryptographicEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left ?? "");
        var rightBytes = Encoding.UTF8.GetBytes(right ?? "");
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string NormalizeOperationId(string value) => Guid.TryParse(value, out var parsed) ? parsed.ToString("N") : "";

    internal static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 127 ||
            bytes[0] == 169 && bytes[1] == 254 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        foreach (var operation in operations.Values) operation.Cancellation.Dispose();
        certificate.Dispose();
    }
}

internal sealed class MultibandClient
{
    private readonly MultibandSettings settings;
    private readonly MultibandSettingsStore settingsStore;
    private readonly string localCertificateFingerprint;

    public MultibandClient(MultibandSettings settings, MultibandSettingsStore settingsStore, string localCertificateFingerprint)
    {
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.localCertificateFingerprint = MultibandSettingsStore.NormalizeFingerprint(localCertificateFingerprint);
    }

    public async Task<PairedDevice> PairAsync(string host, int port, string code, CancellationToken token = default)
    {
        string observedFingerprint = "";
        var request = new MultibandRequest
        {
            Type = "Pair",
            DeviceId = settings.DeviceId,
            DeviceName = settings.DeviceName,
            DevicePort = settings.Port,
            CertificateFingerprint = localCertificateFingerprint,
            PairingCode = code.Trim()
        };
        var response = await SendCoreAsync(host.Trim(), port, request, (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            observedFingerprint = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
            return true;
        }, token);
        EnsureSuccess(response);
        if (!Guid.TryParse(response.DeviceId, out var remoteDeviceId) || string.IsNullOrWhiteSpace(response.SharedSecret)) throw new InvalidOperationException("The remote PC returned invalid pairing information.");

        var normalizedDeviceId = remoteDeviceId.ToString("N");
        if (normalizedDeviceId.Equals(settings.DeviceId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("A PC cannot be paired with itself.");
        var peer = settings.PairedDevices.FirstOrDefault(item => item.DeviceId.Equals(normalizedDeviceId, StringComparison.OrdinalIgnoreCase));
        if (peer is null)
        {
            peer = new PairedDevice { DeviceId = normalizedDeviceId };
            settings.PairedDevices.Add(peer);
        }
        peer.Name = string.IsNullOrWhiteSpace(response.DeviceName) ? host.Trim() : response.DeviceName.Trim();
        peer.Host = host.Trim();
        peer.Port = response.DevicePort is >= 1024 and <= 65535 ? response.DevicePort : port;
        peer.CertificateFingerprint = MultibandSettingsStore.NormalizeFingerprint(observedFingerprint);
        peer.SharedSecret = response.SharedSecret;
        peer.LastSeenUtc = DateTimeOffset.UtcNow;
        settingsStore.Save(settings);
        return peer;
    }

    public async Task<IReadOnlyList<MultibandBandSummary>> GetCatalogAsync(PairedDevice peer, CancellationToken token = default)
    {
        var response = await SendAuthenticatedAsync(peer, "Catalog", token: token);
        return response.Bands;
    }

    public Task<MultibandResponse> PrepareAsync(PairedDevice peer, string bandId, string operationId, CancellationToken token = default)
        => SendAuthenticatedAsync(peer, "Prepare", bandId, operationId, token: token);

    public Task<MultibandResponse> CommitAsync(PairedDevice peer, string bandId, string operationId, DateTimeOffset startAtUtc, CancellationToken token = default)
        => SendAuthenticatedAsync(peer, "Commit", bandId, operationId, startAtUtc, token);

    public Task<MultibandResponse> GetStatusAsync(PairedDevice peer, string operationId, CancellationToken token = default)
        => SendAuthenticatedAsync(peer, "Status", operationId: operationId, token: token);

    public Task<MultibandResponse> CancelAsync(PairedDevice peer, string operationId, CancellationToken token = default)
        => SendAuthenticatedAsync(peer, "Cancel", operationId: operationId, token: token);

    private async Task<MultibandResponse> SendAuthenticatedAsync(PairedDevice peer, string type, string bandId = "", string operationId = "", DateTimeOffset startAtUtc = default, CancellationToken token = default)
    {
        var request = new MultibandRequest
        {
            Type = type,
            DeviceId = settings.DeviceId,
            DeviceName = settings.DeviceName,
            DevicePort = settings.Port,
            Token = peer.SharedSecret,
            BandId = bandId,
            OperationId = operationId,
            StartAtUtc = startAtUtc
        };
        var expectedFingerprint = MultibandSettingsStore.NormalizeFingerprint(peer.CertificateFingerprint);
        var response = await SendCoreAsync(peer.Host, peer.Port, request, (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            var actual = MultibandSettingsStore.NormalizeFingerprint(Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)));
            var expectedBytes = Encoding.ASCII.GetBytes(expectedFingerprint);
            var actualBytes = Encoding.ASCII.GetBytes(actual);
            return expectedBytes.Length > 0 && expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }, token);
        EnsureSuccess(response);
        peer.LastSeenUtc = DateTimeOffset.UtcNow;
        settingsStore.Save(settings);
        return response;
    }

    private static async Task<MultibandResponse> SendCoreAsync(string host, int port, MultibandRequest request, RemoteCertificateValidationCallback certificateValidation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, timeout.Token);
        await using var ssl = new SslStream(client.GetStream(), false, certificateValidation);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = string.IsNullOrWhiteSpace(host) ? "PotatoLauncher" : host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, timeout.Token);
        using var reader = new StreamReader(ssl, Encoding.UTF8, false, 4096, true);
        await using var writer = new StreamWriter(ssl, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        var line = await reader.ReadLineAsync(timeout.Token);
        if (string.IsNullOrWhiteSpace(line) || line.Length > MultibandProtocol.MaximumMessageLength) throw new IOException("The remote PC returned an invalid response.");
        return JsonSerializer.Deserialize<MultibandResponse>(line) ?? throw new IOException("The remote PC returned an invalid response.");
    }

    private static void EnsureSuccess(MultibandResponse response)
    {
        if (!response.Success) throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Error) ? "The remote PC rejected the request." : response.Error);
        if (response.ProtocolVersion != MultibandProtocol.Version) throw new InvalidOperationException("The remote PC uses an incompatible Multiband protocol version.");
    }
}
