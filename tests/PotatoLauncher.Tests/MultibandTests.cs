using System.Net;
using System.Net.Sockets;

namespace PotatoLauncher.Tests;

public class MultibandTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.12", true)]
    [InlineData("172.16.4.2", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.1.20", true)]
    [InlineData("169.254.10.2", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    public void IsPrivateOrLocal_RestrictsPeersToLanAddresses(string address, bool expected)
    {
        Assert.Equal(expected, MultibandServer.IsPrivateOrLocal(IPAddress.Parse(address)));
    }

    [Fact]
    public void SettingsCleanup_NormalizesDevicesAndLaunchPlans()
    {
        var peerId = Guid.NewGuid().ToString();
        var settings = new MultibandSettings
        {
            DeviceId = "invalid",
            DeviceName = "  Main PC  ",
            Port = 80,
            PairedDevices =
            [
                new PairedDevice { DeviceId = peerId, Name = "  Agent  ", Host = " 192.168.1.8 ", SharedSecret = "secret", CertificateFingerprint = "aa:bb" }
            ],
            Plans =
            [
                new MultibandLaunchPlan { Id = "invalid", Name = "  Raid  ", RemoteDeviceId = peerId }
            ]
        };

        var cleaned = MultibandSettingsStore.Clean(settings);

        Assert.True(Guid.TryParse(cleaned.DeviceId, out _));
        Assert.Equal("Main PC", cleaned.DeviceName);
        Assert.Equal(MultibandProtocol.DefaultPort, cleaned.Port);
        Assert.Equal("Agent", cleaned.PairedDevices[0].Name);
        Assert.Equal("192.168.1.8", cleaned.PairedDevices[0].Host);
        Assert.Equal("AABB", cleaned.PairedDevices[0].CertificateFingerprint);
        Assert.Equal(Guid.Parse(peerId).ToString("N"), cleaned.Plans[0].RemoteDeviceId);
        Assert.True(Guid.TryParse(cleaned.Plans[0].Id, out _));
    }

    [Fact]
    public async Task EncryptedProtocol_PairsCatalogsAndLaunchesExactlyOnce()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"PotatoLauncherMultibandTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        var port = GetFreeTcpPort();
        var bandId = Guid.NewGuid().ToString("N");
        var launchCount = 0;
        var launchCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MultibandServer? server = null;
        System.Security.Cryptography.X509Certificates.X509Certificate2? clientCertificate = null;

        try
        {
            var serverStore = new MultibandSettingsStore(Path.Combine(temporaryRoot, "server.json"));
            var serverSettings = new MultibandSettings { DeviceName = "Agent PC", Port = port, ListenEnabled = true };
            var serverCertificate = new MultibandCertificateStore(Path.Combine(temporaryRoot, "server.pfx")).LoadOrCreate(serverSettings.DeviceName);
            Assert.True(serverCertificate.HasPrivateKey);
            server = new MultibandServer(
                serverSettings,
                serverStore,
                serverCertificate,
                () => Task.FromResult<IReadOnlyList<MultibandBandSummary>>([new MultibandBandSummary(bandId, "Remote Band", 8, "Shared")]),
                requestedBandId => Task.FromResult(requestedBandId == bandId ? MultibandReadiness.Success() : MultibandReadiness.Fail("Missing band.")),
                async (requestedBandId, startAt, progress, token) =>
                {
                    Interlocked.Increment(ref launchCount);
                    var delay = startAt - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                    progress(new MultibandLaunchProgress("Completed", "Done.", [new MultibandAccountStatus("Character", "Initialized")]));
                    launchCompleted.TrySetResult();
                });
            await server.StartAsync();

            var clientStore = new MultibandSettingsStore(Path.Combine(temporaryRoot, "client.json"));
            var clientSettings = new MultibandSettings { DeviceName = "Main PC", Port = GetFreeTcpPort() };
            clientCertificate = new MultibandCertificateStore(Path.Combine(temporaryRoot, "client.pfx")).LoadOrCreate(clientSettings.DeviceName);
            var client = new MultibandClient(clientSettings, clientStore, clientCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256));

            PairedDevice peer;
            try
            {
                peer = await client.PairAsync("127.0.0.1", port, server.CreatePairingCode());
            }
            catch (Exception ex)
            {
                await Task.Delay(100);
                throw new InvalidOperationException($"Pairing failed. Server error: {server.LastError}", ex);
            }
            var catalog = await client.GetCatalogAsync(peer);
            Assert.Single(catalog);
            Assert.Equal(bandId, catalog[0].Id);

            var operationId = Guid.NewGuid().ToString("N");
            var prepare = await client.PrepareAsync(peer, bandId, operationId);
            Assert.Equal("Prepared", prepare.Operation?.State);
            var startAt = DateTimeOffset.UtcNow.AddMilliseconds(250);
            await client.CommitAsync(peer, bandId, operationId, startAt);
            await client.CommitAsync(peer, bandId, operationId, startAt);
            await launchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var status = await client.GetStatusAsync(peer, operationId);
            Assert.Equal("Completed", status.Operation?.State);
            Assert.Equal(1, launchCount);

            peer.CertificateFingerprint = new string('0', 64);
            await Assert.ThrowsAnyAsync<Exception>(() => client.GetCatalogAsync(peer));
        }
        finally
        {
            if (server is not null) await server.DisposeAsync();
            clientCertificate?.Dispose();
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
