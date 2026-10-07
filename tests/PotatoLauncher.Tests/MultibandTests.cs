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
                var code = server.CreatePairingCode();
                var fingerprint = await client.GetServerFingerprintAsync("127.0.0.1", port);
                Assert.Equal(MultibandSettingsStore.NormalizeFingerprint(server.CertificateFingerprint), fingerprint);
                Assert.Equal(server.SecurityCode, MultibandServer.FormatSecurityCode(fingerprint));
                // A different (man-in-the-middle) certificate than the one confirmed is refused before the code is sent.
                await Assert.ThrowsAnyAsync<Exception>(() => client.PairAsync("127.0.0.1", port, code, new string('A', 64)));
                peer = await client.PairAsync("127.0.0.1", port, code, fingerprint);
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

    [Fact]
    public async Task Wire_ReadsBoundedLinesAndRejectsOversizedOnes()
    {
        using var ok = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"a\":1}\r\nrest"));
        Assert.Equal("{\"a\":1}", await MultibandWire.ReadLineAsync(ok, 64, CancellationToken.None));

        using var empty = new MemoryStream();
        Assert.Null(await MultibandWire.ReadLineAsync(empty, 64, CancellationToken.None));

        using var huge = new MemoryStream(new byte[10_000]);
        await Assert.ThrowsAsync<InvalidDataException>(() => MultibandWire.ReadLineAsync(huge, 4096, CancellationToken.None));
    }

    [Fact]
    public void SecurityCode_IsFourGroupsOfTheFingerprint()
    {
        Assert.Equal("ABCD-EF01-2345-6789", MultibandServer.FormatSecurityCode("ab:cd:ef:01:23:45:67:89:ff"));
        Assert.Equal("unknown", MultibandServer.FormatSecurityCode("abc"));
        Assert.True(MultibandClient.FingerprintsMatch("aa:bb", "AABB"));
        Assert.False(MultibandClient.FingerprintsMatch("", ""));
        Assert.False(MultibandClient.FingerprintsMatch("AABB", "AABC"));
    }

    [Fact]
    public async Task Server_LimitsPairingGuessesPerAddressAndSurvivesOversizedRequests()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"PotatoLauncherMultibandTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        var port = GetFreeTcpPort();
        MultibandServer? server = null;
        System.Security.Cryptography.X509Certificates.X509Certificate2? clientCertificate = null;
        try
        {
            var serverSettings = new MultibandSettings { DeviceName = "Agent PC", Port = port, ListenEnabled = true };
            server = new MultibandServer(
                serverSettings,
                new MultibandSettingsStore(Path.Combine(temporaryRoot, "server.json")),
                new MultibandCertificateStore(Path.Combine(temporaryRoot, "server.pfx")).LoadOrCreate(serverSettings.DeviceName),
                () => Task.FromResult<IReadOnlyList<MultibandBandSummary>>([]),
                _ => Task.FromResult(MultibandReadiness.Success()),
                (_, _, _, _) => Task.CompletedTask);
            await server.StartAsync();

            // An oversized line is dropped without taking the server down.
            using (var raw = new TcpClient())
            {
                await raw.ConnectAsync(IPAddress.Loopback, port);
                await using var ssl = new System.Net.Security.SslStream(raw.GetStream(), false, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync("PotatoLauncher");
                try { await ssl.WriteAsync(new byte[MultibandProtocol.MaximumMessageLength + 4096]); } catch (IOException) { }
            }

            var clientSettings = new MultibandSettings { DeviceName = "Main PC", Port = GetFreeTcpPort() };
            clientCertificate = new MultibandCertificateStore(Path.Combine(temporaryRoot, "client.pfx")).LoadOrCreate(clientSettings.DeviceName);
            var client = new MultibandClient(clientSettings, new MultibandSettingsStore(Path.Combine(temporaryRoot, "client.json")),
                clientCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256));
            var code = server.CreatePairingCode();
            var fingerprint = await client.GetServerFingerprintAsync("127.0.0.1", port);
            var wrong = code == "111111" ? "222222" : "111111";
            for (var attempt = 0; attempt < MultibandServer.MaximumPairingFailuresPerAddress; attempt++)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => client.PairAsync("127.0.0.1", port, wrong, fingerprint));
            }
            var lockedOut = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PairAsync("127.0.0.1", port, code, fingerprint));
            Assert.Contains("from this PC", lockedOut.Message);

            // A fresh code lifts the lockout.
            var peer = await client.PairAsync("127.0.0.1", port, server.CreatePairingCode(), fingerprint);
            Assert.False(string.IsNullOrWhiteSpace(peer.SharedSecret));
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
