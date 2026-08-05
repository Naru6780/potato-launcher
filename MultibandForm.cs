using System.Drawing;

namespace PotatoLauncher;

internal sealed class MultibandForm : Form
{
    private readonly MultibandSettings settings;
    private readonly MultibandSettingsStore settingsStore;
    private readonly MultibandServer server;
    private readonly MultibandClient client;
    private readonly Func<IReadOnlyList<MultibandBandSummary>> getLocalBands;
    private readonly Func<string, Task<MultibandReadiness>> canLaunchLocalAsync;
    private readonly Func<string, DateTimeOffset, Action<MultibandLaunchProgress>, CancellationToken, Task> launchLocalAsync;
    private readonly CheckBox allowConnections = new();
    private readonly Label addressLabel = new();
    private readonly Label pairingCodeLabel = new();
    private readonly TextBox hostInput = new();
    private readonly NumericUpDown portInput = new();
    private readonly TextBox codeInput = new();
    private readonly ListBox pairedDevicesList = new();
    private readonly ComboBox planInput = new();
    private readonly TextBox planNameInput = new();
    private readonly ComboBox localBandInput = new();
    private readonly ComboBox remoteDeviceInput = new();
    private readonly ComboBox remoteBandInput = new();
    private readonly TextBox progressText = new();
    private readonly Button launchButton = new();
    private readonly Button cancelButton = new();
    private CancellationTokenSource? launchCancellation;
    private string activeOperationId = "";
    private PairedDevice? activeRemoteDevice;
    private bool updatingControls;

    public MultibandForm(
        MultibandSettings settings,
        MultibandSettingsStore settingsStore,
        MultibandServer server,
        MultibandClient client,
        Func<IReadOnlyList<MultibandBandSummary>> getLocalBands,
        Func<string, Task<MultibandReadiness>> canLaunchLocalAsync,
        Func<string, DateTimeOffset, Action<MultibandLaunchProgress>, CancellationToken, Task> launchLocalAsync,
        ThemePalette palette)
    {
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.server = server;
        this.client = client;
        this.getLocalBands = getLocalBands;
        this.canLaunchLocalAsync = canLaunchLocalAsync;
        this.launchLocalAsync = launchLocalAsync;

        Text = "Potato Launcher Multiband";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 640);
        ClientSize = new Size(760, 680);
        Font = new Font("Segoe UI", 10F);
        BuildUi();
        ApplyPalette(palette);
        RefreshAllControls();
    }

    public void ApplyPalette(ThemePalette palette)
    {
        BackColor = palette.Back1;
        ForeColor = palette.Text;
        foreach (Control control in Descendants(this))
        {
            control.ForeColor = palette.Text;
            switch (control)
            {
                case Button button:
                    button.BackColor = button == launchButton ? palette.Primary : button == cancelButton ? palette.Danger : palette.Secondary;
                    button.ForeColor = Color.White;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    break;
                case TextBox textBox:
                    textBox.BackColor = palette.ListBack;
                    break;
                case ComboBox comboBox:
                    comboBox.BackColor = palette.ListBack;
                    break;
                case ListBox listBox:
                    listBox.BackColor = palette.ListBack;
                    break;
                case NumericUpDown numeric:
                    numeric.BackColor = palette.ListBack;
                    break;
                case TabPage page:
                    page.BackColor = palette.Card;
                    break;
                case CheckBox checkBox:
                    checkBox.BackColor = Color.Transparent;
                    break;
                case Label label:
                    label.BackColor = Color.Transparent;
                    break;
            }
        }
    }

    private void BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 7) };
        var connectionPage = new TabPage("Connection");
        var plansPage = new TabPage("Launch plans");
        var progressPage = new TabPage("Progress");
        tabs.TabPages.AddRange([connectionPage, plansPage, progressPage]);
        Controls.Add(tabs);

        BuildConnectionPage(connectionPage);
        BuildPlansPage(plansPage, progressPage, tabs);
        BuildProgressPage(progressPage);
    }

    private void BuildConnectionPage(Control page)
    {
        page.Controls.Add(Heading("Connect Potato Launcher PCs", 24, 22, 500));
        page.Controls.Add(TextLabel("Enable this on the PC that will receive launch commands. Windows may ask to allow private-network access.", 24, 66, 680, 46));

        allowConnections.Text = "Allow trusted PCs to connect";
        allowConnections.Bounds = new Rectangle(24, 118, 300, 28);
        allowConnections.CheckedChanged += async (_, _) => await SetListeningAsync();
        page.Controls.Add(allowConnections);

        addressLabel.Bounds = new Rectangle(24, 154, 690, 52);
        page.Controls.Add(addressLabel);
        var generateButton = ActionButton("Generate pairing code", 24, 214, 190);
        generateButton.Click += async (_, _) => await GeneratePairingCodeAsync();
        page.Controls.Add(generateButton);
        pairingCodeLabel.Text = "Pairing code: not generated";
        pairingCodeLabel.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        pairingCodeLabel.Bounds = new Rectangle(232, 216, 430, 34);
        page.Controls.Add(pairingCodeLabel);

        page.Controls.Add(Heading("Pair with another PC", 24, 282, 420));
        page.Controls.Add(TextLabel("IP address or computer name", 24, 324, 230, 24));
        hostInput.Bounds = new Rectangle(24, 350, 260, 29);
        page.Controls.Add(hostInput);
        page.Controls.Add(TextLabel("Port", 300, 324, 80, 24));
        portInput.Minimum = 1024;
        portInput.Maximum = 65535;
        portInput.Value = settings.Port;
        portInput.Bounds = new Rectangle(300, 350, 96, 29);
        page.Controls.Add(portInput);
        page.Controls.Add(TextLabel("Pairing code", 412, 324, 140, 24));
        codeInput.Bounds = new Rectangle(412, 350, 130, 29);
        codeInput.MaxLength = 6;
        page.Controls.Add(codeInput);
        var pairButton = ActionButton("Pair", 558, 347, 110);
        pairButton.Click += async (_, _) => await PairAsync(pairButton);
        page.Controls.Add(pairButton);

        page.Controls.Add(Heading("Trusted PCs", 24, 414, 300));
        pairedDevicesList.Bounds = new Rectangle(24, 454, 518, 118);
        page.Controls.Add(pairedDevicesList);
        var forgetButton = ActionButton("Forget selected", 558, 454, 130);
        forgetButton.Click += (_, _) => ForgetSelectedDevice();
        page.Controls.Add(forgetButton);
    }

    private void BuildPlansPage(Control page, TabPage progressPage, TabControl tabs)
    {
        page.Controls.Add(Heading("Distributed launch plan", 24, 22, 500));
        page.Controls.Add(TextLabel("Each PC launches its own local band. Both queues begin from the same synchronized countdown.", 24, 64, 680, 42));

        page.Controls.Add(TextLabel("Saved plan", 24, 118, 180, 24));
        planInput.DropDownStyle = ComboBoxStyle.DropDownList;
        planInput.Bounds = new Rectangle(24, 146, 350, 29);
        planInput.SelectedIndexChanged += async (_, _) => await LoadSelectedPlanAsync();
        page.Controls.Add(planInput);
        var newButton = ActionButton("New", 390, 143, 86);
        newButton.Click += (_, _) => ClearPlanEditor();
        page.Controls.Add(newButton);
        var deleteButton = ActionButton("Delete", 488, 143, 86);
        deleteButton.Click += (_, _) => DeleteSelectedPlan();
        page.Controls.Add(deleteButton);

        page.Controls.Add(TextLabel("Plan name", 24, 198, 180, 24));
        planNameInput.Bounds = new Rectangle(24, 226, 350, 29);
        page.Controls.Add(planNameInput);

        page.Controls.Add(Heading("Main PC", 24, 282, 260));
        page.Controls.Add(TextLabel("Local band", 24, 322, 180, 24));
        localBandInput.DropDownStyle = ComboBoxStyle.DropDownList;
        localBandInput.Bounds = new Rectangle(24, 350, 350, 29);
        page.Controls.Add(localBandInput);

        page.Controls.Add(Heading("Second PC", 24, 408, 260));
        page.Controls.Add(TextLabel("Trusted PC", 24, 448, 180, 24));
        remoteDeviceInput.DropDownStyle = ComboBoxStyle.DropDownList;
        remoteDeviceInput.Bounds = new Rectangle(24, 476, 350, 29);
        remoteDeviceInput.SelectedIndexChanged += async (_, _) => await RefreshRemoteBandsAsync();
        page.Controls.Add(remoteDeviceInput);
        page.Controls.Add(TextLabel("Remote band", 390, 448, 180, 24));
        remoteBandInput.DropDownStyle = ComboBoxStyle.DropDownList;
        remoteBandInput.Bounds = new Rectangle(390, 476, 300, 29);
        page.Controls.Add(remoteBandInput);
        var refreshButton = ActionButton("Refresh remote bands", 390, 516, 180);
        refreshButton.Click += async (_, _) => await RefreshRemoteBandsAsync(showErrors: true);
        page.Controls.Add(refreshButton);

        var saveButton = ActionButton("Save plan", 24, 566, 130);
        saveButton.Click += (_, _) => SavePlan();
        page.Controls.Add(saveButton);
        launchButton.Text = "Launch both bands";
        launchButton.Bounds = new Rectangle(430, 560, 260, 42);
        launchButton.Click += async (_, _) =>
        {
            tabs.SelectedTab = progressPage;
            await LaunchPlanAsync();
        };
        page.Controls.Add(launchButton);
    }

    private void BuildProgressPage(Control page)
    {
        page.Controls.Add(Heading("Multiband progress", 24, 22, 500));
        progressText.Multiline = true;
        progressText.ReadOnly = true;
        progressText.ScrollBars = ScrollBars.Vertical;
        progressText.Font = new Font("Consolas", 10F);
        progressText.Bounds = new Rectangle(24, 72, 680, 476);
        progressText.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        page.Controls.Add(progressText);
        cancelButton.Text = "Cancel remaining launches";
        cancelButton.Bounds = new Rectangle(474, 570, 230, 38);
        cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        cancelButton.Enabled = false;
        cancelButton.Click += async (_, _) => await CancelLaunchAsync();
        page.Controls.Add(cancelButton);
    }

    private async Task SetListeningAsync()
    {
        if (updatingControls) return;
        try
        {
            if (allowConnections.Checked) await server.StartAsync();
            else await server.StopAsync();
            settings.ListenEnabled = allowConnections.Checked;
            settingsStore.Save(settings);
            UpdateAddressLabel();
        }
        catch (Exception ex)
        {
            updatingControls = true;
            allowConnections.Checked = server.IsRunning;
            updatingControls = false;
            MessageBox.Show(this, $"Could not start Multiband connections.\n\n{ex.Message}", "Multiband", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task GeneratePairingCodeAsync()
    {
        try
        {
            if (!server.IsRunning)
            {
                updatingControls = true;
                allowConnections.Checked = true;
                updatingControls = false;
                await server.StartAsync();
                settings.ListenEnabled = true;
                settingsStore.Save(settings);
            }
            pairingCodeLabel.Text = $"Pairing code: {server.CreatePairingCode()}  (5 minutes)";
            UpdateAddressLabel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not start pairing.\n\n{ex.Message}", "Multiband", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task PairAsync(Button pairButton)
    {
        if (string.IsNullOrWhiteSpace(hostInput.Text) || codeInput.Text.Trim().Length != 6)
        {
            MessageBox.Show(this, "Enter the other PC's address and six-digit pairing code.", "Pair PCs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        pairButton.Enabled = false;
        try
        {
            var peer = await client.PairAsync(hostInput.Text, (int)portInput.Value, codeInput.Text);
            if (peer.DeviceId.Equals(settings.DeviceId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("A PC cannot be paired with itself.");
            codeInput.Clear();
            RefreshDeviceControls();
            remoteDeviceInput.SelectedItem = settings.PairedDevices.FirstOrDefault(item => item.DeviceId == peer.DeviceId);
            MessageBox.Show(this, $"Paired with {peer.Name}.", "Pair PCs", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Pairing failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            pairButton.Enabled = true;
        }
    }

    private void ForgetSelectedDevice()
    {
        if (pairedDevicesList.SelectedItem is not PairedDevice peer) return;
        if (MessageBox.Show(this, $"Forget {peer.Name}? Saved plans using this PC will need to be repaired.", "Forget PC", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        settings.PairedDevices.RemoveAll(item => item.DeviceId.Equals(peer.DeviceId, StringComparison.OrdinalIgnoreCase));
        foreach (var plan in settings.Plans.Where(plan => plan.RemoteDeviceId.Equals(peer.DeviceId, StringComparison.OrdinalIgnoreCase))) plan.RemoteDeviceId = "";
        settingsStore.Save(settings);
        RefreshDeviceControls();
    }

    private void RefreshAllControls()
    {
        updatingControls = true;
        allowConnections.Checked = settings.ListenEnabled && server.IsRunning;
        updatingControls = false;
        UpdateAddressLabel();
        localBandInput.Items.Clear();
        localBandInput.Items.AddRange(getLocalBands().Cast<object>().ToArray());
        if (localBandInput.Items.Count > 0) localBandInput.SelectedIndex = 0;
        RefreshDeviceControls();
        RefreshPlanControls();
    }

    private void RefreshDeviceControls()
    {
        var selectedDeviceId = (remoteDeviceInput.SelectedItem as PairedDevice)?.DeviceId;
        pairedDevicesList.Items.Clear();
        pairedDevicesList.Items.AddRange(settings.PairedDevices.Cast<object>().ToArray());
        remoteDeviceInput.Items.Clear();
        remoteDeviceInput.Items.AddRange(settings.PairedDevices.Cast<object>().ToArray());
        remoteDeviceInput.SelectedItem = settings.PairedDevices.FirstOrDefault(peer => peer.DeviceId.Equals(selectedDeviceId, StringComparison.OrdinalIgnoreCase));
        if (remoteDeviceInput.SelectedIndex < 0 && remoteDeviceInput.Items.Count > 0) remoteDeviceInput.SelectedIndex = 0;
    }

    private void RefreshPlanControls(string? selectedPlanId = null)
    {
        selectedPlanId ??= (planInput.SelectedItem as MultibandLaunchPlan)?.Id;
        updatingControls = true;
        planInput.Items.Clear();
        planInput.Items.AddRange(settings.Plans.Cast<object>().ToArray());
        planInput.SelectedItem = settings.Plans.FirstOrDefault(plan => plan.Id.Equals(selectedPlanId, StringComparison.OrdinalIgnoreCase));
        updatingControls = false;
    }

    private async Task RefreshRemoteBandsAsync(bool showErrors = false, string? preferredBandId = null)
    {
        if (remoteDeviceInput.SelectedItem is not PairedDevice peer)
        {
            remoteBandInput.Items.Clear();
            return;
        }
        preferredBandId ??= (remoteBandInput.SelectedItem as MultibandBandSummary)?.Id;
        remoteBandInput.Enabled = false;
        try
        {
            var bands = await client.GetCatalogAsync(peer);
            remoteBandInput.Items.Clear();
            remoteBandInput.Items.AddRange(bands.Cast<object>().ToArray());
            remoteBandInput.SelectedItem = bands.FirstOrDefault(band => band.Id.Equals(preferredBandId, StringComparison.OrdinalIgnoreCase));
            if (remoteBandInput.SelectedIndex < 0 && remoteBandInput.Items.Count > 0) remoteBandInput.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            remoteBandInput.Items.Clear();
            if (showErrors) MessageBox.Show(this, ex.Message, "Remote bands unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            remoteBandInput.Enabled = true;
        }
    }

    private async Task LoadSelectedPlanAsync()
    {
        if (updatingControls || planInput.SelectedItem is not MultibandLaunchPlan plan) return;
        planNameInput.Text = plan.Name;
        localBandInput.SelectedItem = localBandInput.Items.Cast<MultibandBandSummary>().FirstOrDefault(band => band.Id.Equals(plan.LocalBandId, StringComparison.OrdinalIgnoreCase));
        remoteDeviceInput.SelectedItem = settings.PairedDevices.FirstOrDefault(peer => peer.DeviceId.Equals(plan.RemoteDeviceId, StringComparison.OrdinalIgnoreCase));
        await RefreshRemoteBandsAsync(preferredBandId: plan.RemoteBandId);
    }

    private void ClearPlanEditor()
    {
        updatingControls = true;
        planInput.SelectedIndex = -1;
        updatingControls = false;
        planNameInput.Text = "New launch plan";
        if (localBandInput.Items.Count > 0) localBandInput.SelectedIndex = 0;
        if (remoteDeviceInput.Items.Count > 0) remoteDeviceInput.SelectedIndex = 0;
    }

    private void SavePlan()
    {
        if (!TryReadPlan(out var localBand, out var remotePeer, out var remoteBand)) return;
        var plan = planInput.SelectedItem as MultibandLaunchPlan;
        if (plan is null)
        {
            plan = new MultibandLaunchPlan();
            settings.Plans.Add(plan);
        }
        plan.Name = string.IsNullOrWhiteSpace(planNameInput.Text) ? $"{localBand.Name} + {remoteBand.Name}" : planNameInput.Text.Trim();
        plan.LocalBandId = localBand.Id;
        plan.RemoteDeviceId = remotePeer.DeviceId;
        plan.RemoteBandId = remoteBand.Id;
        settingsStore.Save(settings);
        RefreshPlanControls(plan.Id);
        planInput.SelectedItem = plan;
    }

    private void DeleteSelectedPlan()
    {
        if (planInput.SelectedItem is not MultibandLaunchPlan plan) return;
        settings.Plans.Remove(plan);
        settingsStore.Save(settings);
        RefreshPlanControls();
        ClearPlanEditor();
    }

    private bool TryReadPlan(out MultibandBandSummary localBand, out PairedDevice remotePeer, out MultibandBandSummary remoteBand)
    {
        localBand = localBandInput.SelectedItem as MultibandBandSummary ?? new MultibandBandSummary("", "", 0, "");
        remotePeer = remoteDeviceInput.SelectedItem as PairedDevice ?? new PairedDevice();
        remoteBand = remoteBandInput.SelectedItem as MultibandBandSummary ?? new MultibandBandSummary("", "", 0, "");
        if (string.IsNullOrWhiteSpace(localBand.Id) || string.IsNullOrWhiteSpace(remotePeer.DeviceId) || string.IsNullOrWhiteSpace(remoteBand.Id))
        {
            MessageBox.Show(this, "Choose a local band, a trusted PC, and a remote band first.", "Multiband", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        return true;
    }

    private async Task LaunchPlanAsync()
    {
        if (!TryReadPlan(out var localBand, out var remotePeer, out var remoteBand)) return;
        SavePlan();
        launchCancellation?.Dispose();
        launchCancellation = new CancellationTokenSource();
        var token = launchCancellation.Token;
        activeOperationId = Guid.NewGuid().ToString("N");
        activeRemoteDevice = remotePeer;
        launchButton.Enabled = false;
        cancelButton.Enabled = true;
        progressText.Text = "Validating both PCs...";

        try
        {
            var localReadiness = await canLaunchLocalAsync(localBand.Id);
            if (!localReadiness.Ready) throw new InvalidOperationException($"Main PC: {localReadiness.Error}");
            var prepareSentUtc = DateTimeOffset.UtcNow;
            var prepareResponse = await client.PrepareAsync(remotePeer, remoteBand.Id, activeOperationId, token);
            var prepareReceivedUtc = DateTimeOffset.UtcNow;

            var startAt = DateTimeOffset.UtcNow.AddSeconds(3);
            var requestMidpointUtc = prepareSentUtc + TimeSpan.FromTicks((prepareReceivedUtc - prepareSentUtc).Ticks / 2);
            var remoteClockOffset = prepareResponse.ServerUtc == default ? TimeSpan.Zero : prepareResponse.ServerUtc - requestMidpointUtc;
            var remoteStartAt = startAt + remoteClockOffset;
            progressText.Text = $"Both PCs are ready. Starting at {startAt.LocalDateTime:T}...";
            await client.CommitAsync(remotePeer, remoteBand.Id, activeOperationId, remoteStartAt, token);

            MultibandLaunchProgress localProgress = MultibandLaunchProgress.Scheduled("Waiting for synchronized start.");
            MultibandOperationSnapshot? remoteProgress = null;
            void ReportLocal(MultibandLaunchProgress progress)
            {
                localProgress = progress;
                if (!IsDisposed) BeginInvoke(() => RenderProgress(localBand.Name, localProgress, remotePeer.Name, remoteBand.Name, remoteProgress));
            }

            var localTask = launchLocalAsync(localBand.Id, startAt, ReportLocal, token);
            var remoteTask = PollRemoteOperationAsync(remotePeer, snapshot =>
            {
                remoteProgress = snapshot;
                RenderProgress(localBand.Name, localProgress, remotePeer.Name, remoteBand.Name, remoteProgress);
            }, token);
            await Task.WhenAll(localTask, remoteTask);
            RenderProgress(localBand.Name, localProgress, remotePeer.Name, remoteBand.Name, remoteProgress);
        }
        catch (OperationCanceledException)
        {
            progressText.AppendText("\r\n\r\nLaunch cancelled.");
        }
        catch (Exception ex)
        {
            progressText.AppendText($"\r\n\r\nLaunch failed: {ex.Message}");
        }
        finally
        {
            launchButton.Enabled = true;
            cancelButton.Enabled = false;
            activeOperationId = "";
            activeRemoteDevice = null;
        }
    }

    private async Task PollRemoteOperationAsync(PairedDevice peer, Action<MultibandOperationSnapshot> update, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var response = await client.GetStatusAsync(peer, activeOperationId, token);
            var snapshot = response.Operation ?? throw new InvalidOperationException("The remote PC did not return launch status.");
            update(snapshot);
            if (snapshot.IsTerminal) return;
            await Task.Delay(600, token);
        }
    }

    private async Task CancelLaunchAsync()
    {
        launchCancellation?.Cancel();
        if (activeRemoteDevice is null || string.IsNullOrWhiteSpace(activeOperationId)) return;
        try { await client.CancelAsync(activeRemoteDevice, activeOperationId); } catch { }
    }

    private void RenderProgress(string localBandName, MultibandLaunchProgress local, string remoteDeviceName, string remoteBandName, MultibandOperationSnapshot? remote)
    {
        var lines = new List<string>
        {
            $"{settings.DeviceName} — {localBandName}",
            $"  {local.State}: {local.Detail}"
        };
        lines.AddRange(local.Accounts.Select(account => $"  {account.Name} — {account.Status}"));
        lines.Add("");
        lines.Add($"{remoteDeviceName} — {remoteBandName}");
        lines.Add(remote is null ? "  Waiting for status..." : $"  {remote.State}: {remote.Detail}");
        if (remote is not null) lines.AddRange(remote.Accounts.Select(account => $"  {account.Name} — {account.Status}"));
        progressText.Text = string.Join(Environment.NewLine, lines);
    }

    private void UpdateAddressLabel()
    {
        var addresses = MultibandServer.LocalIpv4Addresses();
        addressLabel.Text = server.IsRunning
            ? $"Listening on port {settings.Port}. Address{(addresses.Count == 1 ? "" : "es")}: {(addresses.Count == 0 ? "No private IPv4 address found" : string.Join(", ", addresses))}"
            : "Connections are disabled.";
    }

    private static Label Heading(string text, int x, int y, int width) => new() { Text = text, Font = new Font("Segoe UI", 16F, FontStyle.Bold), Bounds = new Rectangle(x, y, width, 36) };
    private static Label TextLabel(string text, int x, int y, int width, int height) => new() { Text = text, Bounds = new Rectangle(x, y, width, height) };
    private static Button ActionButton(string text, int x, int y, int width) => new() { Text = text, Bounds = new Rectangle(x, y, width, 34), UseVisualStyleBackColor = false };

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
