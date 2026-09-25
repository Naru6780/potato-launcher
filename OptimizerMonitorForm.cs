using System.Drawing.Drawing2D;

namespace PotatoLauncher;

internal sealed class OptimizerMonitorForm : Form
{
    private readonly IntegratedOptimizerService optimizer;
    private readonly Func<bool> notificationsEnabled;
    private ThemePalette palette;
    private readonly DataGridView grid = new();
    private readonly Label summaryLabel = new();
    private readonly Label gpuStatusLabel = new();
    private readonly CheckBox optimizerEnabled = new();
    private readonly CheckBox trimEnabled = new();
    private readonly ToolTip toolTip = new();
    private readonly ComboBox trimMode = new();
    private readonly ComboBox cpuOperationMode = new();
    private readonly ComboBox assignmentMode = new();
    private readonly ComboBox mainProcessors = new();
    private readonly ComboBox followerProcessors = new();
    private readonly ComboBox roleClientInput = new();
    private readonly ComboBox roleInput = new();
    private readonly Label mainClientsLabel = new();
    private readonly Label cpuPolicyLabel = new();
    private readonly NumericUpDown reservedProcessors = new();
    private readonly NumericUpDown mainPriority = new();
    private readonly NumericUpDown trimTrigger = new();
    private readonly Button applyButton = new NewsPillButton();
    private readonly Button saveButton = new NewsPillButton();
    private readonly Button restoreButton = new NewsPillButton();
    private readonly Button trimButton = new NewsPillButton();
    private readonly Button presetButton = new NewsPillButton();
    private readonly Button fpsButton = new NewsPillButton();
    private bool refreshing;
    private bool refreshQueued;
    private bool moveOrResizeActive;
    private bool closing;
    private CpuAssignmentMode? displayedMode;
    private DateTime lastPeriodicRefreshUtc = DateTime.MinValue;
    private readonly CancellationTokenSource benchmarkStop = new();

    public OptimizerMonitorForm(IntegratedOptimizerService optimizer, ThemePalette palette, Func<bool>? notificationsEnabled = null)
    {
        this.optimizer = optimizer;
        this.notificationsEnabled = notificationsEnabled ?? (() => true);
        this.palette = palette;
        Text = "Potato Optimizer";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1120, 780);
        MinimumSize = new Size(940, 680);
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        refreshing = true;
        try { BuildUi(); }
        finally { refreshing = false; }
        ApplyTheme(palette);
        optimizer.Updated += OptimizerUpdated;
        ResizeBegin += (_, _) => moveOrResizeActive = true;
        ResizeEnd += (_, _) =>
        {
            moveOrResizeActive = false;
            QueueRefresh(force: true);
        };
        FormClosing += (_, _) => BeginClosing();
        RefreshView();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) BeginClosing();
        base.Dispose(disposing);
    }

    private void BeginClosing()
    {
        if (closing) return;
        closing = true;
        benchmarkStop.Cancel();
        refreshQueued = false;
        optimizer.Updated -= OptimizerUpdated;
    }

    public void ApplyTheme(ThemePalette themePalette)
    {
        palette = themePalette;
        BackColor = palette.Back1;
        foreach (Control control in Controls)
        {
            ApplyThemeRecursive(control);
        }
        grid.BackgroundColor = NativeGridColor(palette.Card);
        grid.GridColor = NativeGridColor(palette.Border);
        grid.DefaultCellStyle.BackColor = NativeGridColor(palette.ListBack);
        grid.DefaultCellStyle.ForeColor = palette.Text;
        grid.DefaultCellStyle.SelectionBackColor = palette.Secondary;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = NativeGridColor(palette.Card);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = NativeGridColor(palette.Card);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
        grid.RowHeadersDefaultCellStyle.BackColor = NativeGridColor(palette.Card);
        grid.RowHeadersDefaultCellStyle.ForeColor = palette.Text;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = NativeGridColor(palette.Card);
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
    }

    private void BuildUi()
    {
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 350));
        Controls.Add(root);

        var header = new OptimizerPanel { Dock = DockStyle.Fill, Radius = 20 };
        root.Controls.Add(header, 0, 0);
        var headerLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 12, 18, 12), BackColor = Color.Transparent };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        header.Controls.Add(headerLayout);

        var title = new Label
        {
            Text = "Optimizer Monitor",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        summaryLabel.Dock = DockStyle.Fill;
        summaryLabel.BackColor = Color.Transparent;
        summaryLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        summaryLabel.AutoEllipsis = true;
        var titleStack = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        titleStack.Controls.Add(summaryLabel);
        titleStack.Controls.Add(title);
        headerLayout.Controls.Add(titleStack, 0, 0);

        optimizerEnabled.Text = "Auto CPU Optimization";
        optimizerEnabled.Dock = DockStyle.None;
        optimizerEnabled.Width = 230;
        optimizerEnabled.Height = 26;
        optimizerEnabled.CheckedChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.SetCpuOptimizationEnabled(optimizerEnabled.Checked);
            RefreshView();
        };
        trimEnabled.Text = "Automatic RAM trimming";
        trimEnabled.Dock = DockStyle.None;
        trimEnabled.Width = 230;
        trimEnabled.Height = 26;
        toolTip.SetToolTip(trimEnabled, "Trim a background follower when its resident RAM reaches the per-client trigger. Main and foreground clients are protected. This is a trigger, not a memory cap; trimming does not free commit.");
        trimEnabled.CheckedChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.WorkingSetTrimEnabled = trimEnabled.Checked;
            optimizer.SaveSettings();
        };
        trimMode.DropDownStyle = ComboBoxStyle.DropDownList;
        trimMode.Items.AddRange(["Pressure-aware", "Auto trim at threshold"]);
        trimMode.Width = 190;
        trimMode.Height = 28;
        toolTip.SetToolTip(trimMode, "Auto trim at threshold uses Trim trigger MB for each client. Pressure-aware additionally waits for system RAM pressure.");
        trimMode.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.MemoryTrimMode = trimMode.SelectedIndex == 1 ? MemoryTrimMode.Threshold : MemoryTrimMode.PressureAware;
            optimizer.SaveSettings();
            RefreshView();
        };
        cpuOperationMode.DropDownStyle = ComboBoxStyle.DropDownList;
        cpuOperationMode.Items.AddRange(["Live optimization — apply CPU affinity", "Planning only — no CPU changes"]);
        cpuOperationMode.Width = 290;
        cpuOperationMode.Height = 28;
        cpuOperationMode.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.CpuPreviewOnly = cpuOperationMode.SelectedIndex == 1;
            optimizer.SaveSettings();
            RefreshView();
        };

        gpuStatusLabel.Dock = DockStyle.Fill;
        gpuStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        gpuStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        gpuStatusLabel.BackColor = Color.Transparent;
        headerLayout.Controls.Add(gpuStatusLabel, 1, 0);

        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeight = 34;
        grid.Dock = DockStyle.Fill;
        grid.EnableHeadersVisualStyles = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 32;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Client", HeaderText = "Client", ReadOnly = true, FillWeight = 175 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pid", HeaderText = "PID", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cpu", HeaderText = "CPU", ReadOnly = true, FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Gpu", HeaderText = "GPU 3D", ReadOnly = true, FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ram", HeaderText = "RAM", ReadOnly = true, FillWeight = 86 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Private", HeaderText = "Private", ReadOnly = true, FillWeight = 86 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Threads", HeaderText = "Threads", ReadOnly = true, FillWeight = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "Role", ReadOnly = true, FillWeight = 78 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Affinity", HeaderText = "Affinity", ReadOnly = true, FillWeight = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Planned", HeaderText = "Planned", ReadOnly = true, FillWeight = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Trim", HeaderText = "Last trim", ReadOnly = true, FillWeight = 96 });
        root.Controls.Add(grid, 0, 1);

        var controls = new OptimizerPanel { Dock = DockStyle.Fill, Radius = 20 };
        root.Controls.Add(controls, 0, 2);
        var controlGrid = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 4, Padding = new Padding(18, 14, 18, 14), BackColor = Color.Transparent };
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        controls.Controls.Add(controlGrid);

        assignmentMode.DropDownStyle = ComboBoxStyle.DropDownList;
        assignmentMode.Items.AddRange(Enum.GetValues<CpuAssignmentMode>().Cast<object>().ToArray());
        toolTip.SetToolTip(assignmentMode, "BalancedShared shares complete cache domains across all clients. SplitLanes and AdaptiveSharedPools reserve CPU capacity for a main client. Compare measured FPS before choosing a restrictive mode.");
        assignmentMode.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.CpuAssignmentMode = (CpuAssignmentMode)assignmentMode.SelectedItem!;
            optimizer.SaveSettings();
            UpdateModeControls();
        };
        mainProcessors.DropDownStyle = ComboBoxStyle.DropDownList;
        var supportedLogicalProcessorCount = ProcessorAffinity.GetSupportedLogicalProcessorCount(Environment.ProcessorCount);
        mainProcessors.Items.AddRange(OptimizerSettings.GetAllowedLogicalProcessorCounts(supportedLogicalProcessorCount).Cast<object>().ToArray());
        mainProcessors.SelectedIndexChanged += (_, _) => SaveSelectedProcessorCounts();
        followerProcessors.DropDownStyle = ComboBoxStyle.DropDownList;
        followerProcessors.Items.AddRange(OptimizerSettings.GetAllowedLogicalProcessorCounts(supportedLogicalProcessorCount).Cast<object>().ToArray());
        followerProcessors.SelectedIndexChanged += (_, _) => SaveSelectedProcessorCounts();
        roleClientInput.DropDownStyle = ComboBoxStyle.DropDownList;
        roleClientInput.SelectedIndexChanged += (_, _) => SyncRoleInputFromSelectedClient();
        roleInput.DropDownStyle = ComboBoxStyle.DropDownList;
        roleInput.Items.AddRange(["Main", "Follower"]);
        roleInput.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            if (roleClientInput.SelectedItem is not RoleClientItem item) return;
            optimizer.SetMainClient(item.ProcessId, item.ClientName, string.Equals(roleInput.SelectedItem?.ToString(), "Main", StringComparison.OrdinalIgnoreCase));
            RefreshView();
        };
        mainClientsLabel.Dock = DockStyle.Fill;
        mainClientsLabel.AutoEllipsis = true;
        mainClientsLabel.BackColor = Color.Transparent;
        mainClientsLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        ConfigureStepper(reservedProcessors, 0, Math.Max(0, supportedLogicalProcessorCount - 1));
        ConfigureStepper(mainPriority, 1, 100);
        ConfigureStepper(trimTrigger, 128, 32768);
        reservedProcessors.ValueChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.SystemReservedLogicalProcessors = (int)reservedProcessors.Value;
            optimizer.SaveSettings();
        };
        mainPriority.ValueChanged += (_, _) =>
        {
            if (refreshing) return;
            if (roleClientInput.SelectedItem is not RoleClientItem item || !item.IsMainCandidate) return;
            optimizer.SetMainPriority(item.ClientName, (int)mainPriority.Value);
            RefreshView();
        };
        trimTrigger.ValueChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.TrimTriggerMBPerClient = (int)trimTrigger.Value;
            optimizer.SaveSettings();
        };

        var autoOptions = AutoOptionsRow();
        controlGrid.Controls.Add(autoOptions, 0, 0);
        controlGrid.SetColumnSpan(autoOptions, 5);
        controlGrid.Controls.Add(Field("CPU lanes", assignmentMode), 0, 1);
        controlGrid.Controls.Add(Field("Main logical CPUs", mainProcessors), 1, 1);
        controlGrid.Controls.Add(Field("Follower logical CPUs", followerProcessors), 2, 1);
        controlGrid.Controls.Add(Field("Reserved logical CPUs", reservedProcessors), 3, 1);
        controlGrid.Controls.Add(Field("Trim trigger MB", trimTrigger), 4, 1);
        cpuPolicyLabel.Dock = DockStyle.Fill;
        cpuPolicyLabel.AutoEllipsis = true;
        cpuPolicyLabel.BackColor = Color.Transparent;
        controlGrid.Controls.Add(cpuPolicyLabel, 1, 1);
        controlGrid.SetColumnSpan(cpuPolicyLabel, 3);
        controlGrid.Controls.Add(Field("Client", roleClientInput), 0, 2);
        controlGrid.Controls.Add(Field("Selected client role", roleInput), 1, 2);
        controlGrid.Controls.Add(Field("Main selection order (1 wins)", mainPriority), 2, 2);
        controlGrid.Controls.Add(mainClientsLabel, 0, 3);
        controlGrid.SetColumnSpan(mainClientsLabel, 2);

        applyButton.Text = "Optimize CPU Now";
        applyButton.Tag = "Secondary";
        applyButton.Click += (_, _) =>
        {
            optimizer.ApplyNow();
            RefreshView();
            ShowFeedback(optimizer.LastAction);
        };
        saveButton.Text = "Save";
        saveButton.Tag = "Secondary";
        saveButton.Click += (_, _) =>
        {
            optimizer.SaveSettings();
            RefreshView();
            ShowFeedback("Optimizer settings saved.");
        };
        trimButton.Text = "Trim one client";
        trimButton.Tag = "Secondary";
        trimButton.Click += (_, _) =>
        {
            optimizer.TrimNow();
            RefreshView();
            ShowFeedback(optimizer.LastAction);
        };
        restoreButton.Text = "Stop / restore";
        restoreButton.Tag = "Danger";
        restoreButton.Click += (_, _) =>
        {
            optimizer.RestoreClients();
            RefreshView();
            ShowFeedback(optimizer.LastAction);
        };
        presetButton.Text = "Balanced preset";
        presetButton.Tag = "Secondary";
        toolTip.SetToolTip(presetButton, "Use balanced shared CPU pools and automatic per-client threshold trimming. Keeps your saved Trim trigger MB value. Does not impose a memory cap or limit FPS.");
        presetButton.Click += (_, _) =>
        {
            optimizer.Settings.CpuAssignmentMode = CpuAssignmentMode.BalancedShared;
            optimizer.Settings.WorkingSetTrimEnabled = true;
            optimizer.Settings.MemoryTrimMode = MemoryTrimMode.Threshold;
            optimizer.Settings.CpuPreviewOnly = false;
            optimizer.SetCpuOptimizationEnabled(true);
            optimizer.ApplyNow();
            RefreshView();
            ShowFeedback(optimizer.LastAction);
        };
        fpsButton.Text = "Measure FPS";
        fpsButton.Tag = "Secondary";
        fpsButton.Click += async (_, _) => await MeasureFpsAsync();
        var buttonRow = ButtonRow();
        controlGrid.Controls.Add(buttonRow, 2, 3);
        controlGrid.SetColumnSpan(buttonRow, 3);
        UpdateModeControls();
    }

    private FlowLayoutPanel AutoOptionsRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 3, 0, 0)
        };
        optimizerEnabled.Margin = new Padding(0, 3, 22, 0);
        trimEnabled.Margin = new Padding(0, 3, 22, 0);
        trimMode.Margin = new Padding(0, 2, 22, 0);
        cpuOperationMode.Margin = new Padding(0, 2, 22, 0);
        panel.Controls.Add(optimizerEnabled);
        panel.Controls.Add(trimEnabled);
        panel.Controls.Add(trimMode);
        panel.Controls.Add(cpuOperationMode);
        return panel;
    }

    private void ShowFeedback(string message)
    {
        if (!notificationsEnabled()) return;
        AppNotification.Show(this, "Potato Optimizer", message);
    }

    private async Task MeasureFpsAsync()
    {
        using var picker = new OpenFileDialog { Title = "Select PresentMon console executable (optional, from GameTechDev/PresentMon)",
            Filter = "PresentMon console (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        fpsButton.Enabled = false;
        fpsButton.Text = "Measuring 30s";
        var identities = optimizer.GetSnapshots().ToDictionary(snapshot => snapshot.ProcessId, snapshot => snapshot.ClientName);
        try
        {
            var (path, results) = await FpsBenchmark.CaptureAsync(picker.FileName, benchmarkStop.Token);
            if (closing) return;
            var lines = results.Where(result => identities.ContainsKey(result.ProcessId)).Select(result =>
                $"{identities[result.ProcessId]}: {result.AverageFps:0.0} FPS average, p95 {result.P95FrameMs:0.0} ms ({result.Seconds:0}s sampled)").ToList();
            MessageBox.Show(this, "Target: 60 FPS per client (16.67 ms per frame).\nThese are application present rates, not a guarantee of displayed FPS.\n\n" +
                (lines.Count == 0 ? "No FFXIV frames were captured. Check PresentMon access and that clients are rendering." : string.Join("\n", lines)) +
                $"\n\nCaptured {lines.Count} of {identities.Count} clients. Keep the same scene and graphics settings when comparing CPU modes.\nCSV: {path}",
                "30-second FPS benchmark", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { if (!closing) ShowFeedback("FPS capture timed out."); }
        catch (Exception ex) { if (!closing) MessageBox.Show(this, ex.Message, "FPS capture failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { if (!closing) { fpsButton.Enabled = true; fpsButton.Text = "Measure FPS"; } }
    }

    private FlowLayoutPanel ButtonRow()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, BackColor = Color.Transparent };
        foreach (var button in new[] { presetButton, fpsButton, applyButton, trimButton, saveButton, restoreButton })
        {
            button.Width = 116;
            button.Height = 36;
            button.Margin = new Padding(0, 10, 10, 0);
            button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            panel.Controls.Add(button);
        }

        return panel;
    }

    private static void ConfigureStepper(NumericUpDown stepper, int min, int max)
    {
        stepper.Minimum = min;
        stepper.Maximum = max;
        stepper.Increment = min == 128 ? 128 : 1;
        stepper.BorderStyle = BorderStyle.FixedSingle;
        stepper.Dock = DockStyle.Fill;
    }

    private Control Field(string labelText, Control input)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 10), BackColor = Color.Transparent };
        var label = new Label { Text = labelText, Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        input.Dock = DockStyle.Top;
        input.Height = 28;
        panel.Controls.Add(input);
        panel.Controls.Add(label);
        return panel;
    }

    private void OptimizerUpdated(object? sender, EventArgs e)
    {
        QueueRefresh();
    }

    private void QueueRefresh(bool force = false)
    {
        if (closing || IsDisposed || Disposing || !IsHandleCreated || refreshQueued) return;
        if (!force && (moveOrResizeActive || WindowState == FormWindowState.Minimized || IsEditorActive())) return;

        var now = DateTime.UtcNow;
        if (!force && now - lastPeriodicRefreshUtc < TimeSpan.FromMilliseconds(1500)) return;

        refreshQueued = true;
        try
        {
            BeginInvoke((Action)(() =>
            {
                refreshQueued = false;
                if (closing || IsDisposed || Disposing || grid.IsDisposed || grid.Disposing ||
                    moveOrResizeActive || (!force && IsEditorActive())) return;
                lastPeriodicRefreshUtc = DateTime.UtcNow;
                RefreshView();
            }));
        }
        catch (InvalidOperationException)
        {
            refreshQueued = false;
        }
    }

    private bool IsEditorActive()
    {
        return assignmentMode.DroppedDown ||
               mainProcessors.DroppedDown ||
               followerProcessors.DroppedDown ||
               roleClientInput.DroppedDown ||
               roleInput.DroppedDown ||
               cpuOperationMode.DroppedDown ||
               trimMode.DroppedDown;
    }

    private void RefreshView()
    {
        if (closing || IsDisposed || Disposing || grid.IsDisposed || grid.Disposing || grid.Columns.Count == 0) return;
        refreshing = true;
        try
        {
            var settings = optimizer.Settings;
            settings.Normalize();
            optimizerEnabled.Checked = settings.OptimizerEnabled && settings.CpuAffinityOptimizationEnabled;
            trimEnabled.Checked = settings.WorkingSetTrimEnabled;
            SetSelectedItemIfIdle(trimMode, settings.MemoryTrimMode == MemoryTrimMode.Threshold ? "Auto trim at threshold" : "Pressure-aware");
            SetSelectedItemIfIdle(cpuOperationMode, settings.CpuPreviewOnly
                ? "Planning only — no CPU changes"
                : "Live optimization — apply CPU affinity");
            applyButton.Text = settings.CpuPreviewOnly ? "Preview CPU Plan" : "Optimize CPU Now";
            SetSelectedItemIfIdle(assignmentMode, settings.CpuAssignmentMode);
            UpdateModeControls();
            SetSelectedItemIfIdle(mainProcessors, settings.MainLogicalProcessors);
            SetSelectedItemIfIdle(followerProcessors, settings.FollowerLogicalProcessors);
            SetStepperValueIfIdle(reservedProcessors, settings.SystemReservedLogicalProcessors);
            SetStepperValueIfIdle(trimTrigger, settings.TrimTriggerMBPerClient);

            var snapshots = optimizer.GetSnapshots();
            UpdateRoleControls(snapshots);
            toolTip.SetToolTip(mainClientsLabel, mainClientsLabel.Text);
            UpdateGrid(snapshots);
            var system = optimizer.GetSystemMetrics();
            var clientRam = snapshots.Sum(snapshot => snapshot.WorkingSetBytes) / 1024d / 1024d;
            var clientCpu = snapshots.Sum(snapshot => snapshot.CpuPercent);
            var gpuValues = snapshots.Where(snapshot => snapshot.GpuPercent.HasValue).Select(snapshot => snapshot.GpuPercent!.Value).ToList();
            var clientGpuText = gpuValues.Count == 0 ? "GPU 3D N/A" : $"busiest client GPU 3D {gpuValues.Max():0.0}%";
            var systemGpuText = system.GpuPercent.HasValue ? $"GPU 3D {system.GpuPercent.Value:0.0}%" : "GPU 3D N/A";
            var systemRamPercent = system.TotalMemoryBytes <= 0 ? 0 : system.UsedMemoryBytes / (double)system.TotalMemoryBytes * 100;
            summaryLabel.Text =
                (settings.CpuPreviewOnly ? "PLANNING ONLY — CPU affinity is not being changed." + Environment.NewLine : "") +
                $"Clients: {snapshots.Count} | Target: 60 FPS each (measure to verify) | CPU {clientCpu:0.0}% | {clientGpuText} | RAM {FormatMb(clientRam)}" +
                Environment.NewLine +
                $"System: {ProcessorAffinity.FormatLogicalProcessorCapacity(Environment.ProcessorCount)} | CPU {system.CpuPercent:0.0}% | {systemGpuText} | RAM {FormatMb(system.UsedMemoryBytes)} / {FormatMb(system.TotalMemoryBytes)} ({systemRamPercent:0}%) | Pressure {(system.MemoryPressureActive ? "ACTIVE" : "healthy")}";
            var commit = SystemCommitStatus.Read();
            gpuStatusLabel.Text = (commit.LimitBytes == 0 ? "Commit unavailable" :
                $"Commit {commit.UsedBytes / 1073741824d:0.0}/{commit.LimitBytes / 1073741824d:0.0} GiB") +
                (commit.IsCritical ? "\nLOW HEADROOM — out-of-memory risk" : "") +
                Environment.NewLine + optimizer.GpuStatusText + Environment.NewLine + optimizer.LastAction;
            toolTip.SetToolTip(summaryLabel, summaryLabel.Text);
            toolTip.SetToolTip(gpuStatusLabel, commit.Summary + Environment.NewLine + gpuStatusLabel.Text);
        }
        finally
        {
            refreshing = false;
        }
    }

    private void UpdateGrid(IReadOnlyList<OptimizerClientSnapshot> snapshots)
    {
        if (closing || grid.IsDisposed || grid.Disposing || grid.Columns.Count == 0) return;
        var selectedProcessId = SelectedProcessId();
        var selectedColumnName = grid.CurrentCell is null ? "" : grid.Columns[grid.CurrentCell.ColumnIndex].Name;
        var firstDisplayedRow = FirstDisplayedRowIndex();
        var needsRebuild = grid.Rows.Count != snapshots.Count;
        if (!needsRebuild)
        {
            for (var index = 0; index < snapshots.Count; index++)
            {
                if (grid.Rows[index].Tag is not OptimizerClientSnapshot rowSnapshot ||
                    rowSnapshot.ProcessId != snapshots[index].ProcessId)
                {
                    needsRebuild = true;
                    break;
                }
            }
        }

        if (needsRebuild)
        {
            grid.Rows.Clear();
            foreach (var snapshot in snapshots)
            {
                var rowIndex = grid.Rows.Add();
                UpdateRow(grid.Rows[rowIndex], snapshot);
            }
        }
        else
        {
            for (var index = 0; index < snapshots.Count; index++)
            {
                UpdateRow(grid.Rows[index], snapshots[index]);
            }
        }

        RestoreGridPosition(selectedProcessId, selectedColumnName, firstDisplayedRow);
    }

    private void UpdateRow(DataGridViewRow row, OptimizerClientSnapshot snapshot)
    {
        row.Tag = snapshot;
        SetCell(row, "Client", snapshot.ClientName);
        SetCell(row, "Pid", snapshot.ProcessId);
        SetCell(row, "Cpu", $"{snapshot.CpuPercent:0.0}%");
        SetCell(row, "Gpu", snapshot.GpuPercent.HasValue ? $"{snapshot.GpuPercent.Value:0.0}%" : "N/A");
        SetCell(row, "Ram", $"{snapshot.WorkingSetBytes / 1024d / 1024d:0} MB");
        SetCell(row, "Private", $"{snapshot.PrivateBytes / 1024d / 1024d:0} MB");
        SetCell(row, "Threads", snapshot.ThreadCount);
        SetCell(row, "Role", snapshot.IsMain ? "Active main" : snapshot.IsMainCandidate ? "Follower (main candidate)" : "Follower");
        SetCell(row, "Affinity", snapshot.AffinityMask.HasValue ? ProcessorAffinity.FormatMask(snapshot.AffinityMask.Value) : "N/A");
        SetCell(row, "Planned", snapshot.PlannedAffinityMask.HasValue ? ProcessorAffinity.FormatMask(snapshot.PlannedAffinityMask.Value) : "N/A");
        SetCell(row, "Trim", snapshot.LastTrimUtc.HasValue ? snapshot.LastTrimUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "-");
    }

    private static void SetCell(DataGridViewRow row, string columnName, object value)
    {
        var cell = row.Cells[columnName];
        if (!Equals(cell.Value, value)) cell.Value = value;
    }

    private int? SelectedProcessId()
    {
        if (grid.CurrentRow?.Tag is OptimizerClientSnapshot currentSnapshot) return currentSnapshot.ProcessId;
        return grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is OptimizerClientSnapshot selectedSnapshot
            ? selectedSnapshot.ProcessId
            : null;
    }

    private int FirstDisplayedRowIndex()
    {
        try
        {
            return grid.Rows.Count == 0 ? -1 : grid.FirstDisplayedScrollingRowIndex;
        }
        catch
        {
            return -1;
        }
    }

    private void RestoreGridPosition(int? selectedProcessId, string selectedColumnName, int firstDisplayedRow)
    {
        if (firstDisplayedRow >= 0 && firstDisplayedRow < grid.Rows.Count)
        {
            try { grid.FirstDisplayedScrollingRowIndex = firstDisplayedRow; } catch { }
        }

        grid.ClearSelection();
        if (!selectedProcessId.HasValue) return;

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not OptimizerClientSnapshot snapshot || snapshot.ProcessId != selectedProcessId.Value) continue;
            var column = grid.Columns.Contains(selectedColumnName) ? grid.Columns[selectedColumnName] : grid.Columns["Client"];
            row.Selected = true;
            if (column is not null) grid.CurrentCell = row.Cells[column.Index];
            return;
        }
    }

    private static void SetSelectedItemIfIdle(ComboBox comboBox, object value)
    {
        if (comboBox.Focused || comboBox.DroppedDown) return;
        if (!Equals(comboBox.SelectedItem, value)) comboBox.SelectedItem = value;
    }

    private static void SetStepperValueIfIdle(NumericUpDown stepper, int value)
    {
        if (stepper.Focused) return;
        var clamped = Math.Clamp(value, (int)stepper.Minimum, (int)stepper.Maximum);
        if (stepper.Value != clamped) stepper.Value = clamped;
    }

    private static string FormatMb(double megabytes)
    {
        return $"{megabytes:0} MB";
    }

    private static string FormatMb(long bytes)
    {
        return $"{bytes / 1024d / 1024d:0} MB";
    }

    private void SaveSelectedProcessorCounts()
    {
        if (refreshing) return;
        if (mainProcessors.SelectedItem is int mainCount) optimizer.Settings.MainLogicalProcessors = mainCount;
        if (followerProcessors.SelectedItem is int followerCount) optimizer.Settings.FollowerLogicalProcessors = followerCount;
        optimizer.SaveSettings();
    }

    private void UpdateModeControls()
    {
        var mode = assignmentMode.SelectedItem is CpuAssignmentMode selectedMode
            ? selectedMode
            : optimizer.Settings.CpuAssignmentMode;
        if (displayedMode == mode) return;
        displayedMode = mode;
        mainProcessors.Enabled = UsesManualMainProcessorCount(mode);
        followerProcessors.Enabled = UsesManualFollowerProcessorCount(mode);
        // These values are irrelevant in shared mode; don't display stale numbers as if applied.
        mainProcessors.Parent!.Visible = mainProcessors.Enabled;
        followerProcessors.Parent!.Visible = followerProcessors.Enabled;
        reservedProcessors.Parent!.Visible = UsesReservedProcessorCount(mode);
        cpuPolicyLabel.Visible = !UsesManualMainProcessorCount(mode);
        cpuPolicyLabel.Text = mode switch
        {
            CpuAssignmentMode.BalancedShared => "No main/follower reservations. Share suitable cache pools; otherwise use all logical CPUs. Check the Affinity column for the actual allocation.",
            CpuAssignmentMode.AllAvailableCores => "All logical CPUs shared by every client. Windows schedules the game threads; no main/follower reservations.",
            _ => "One physical core, including its SMT siblings, per client slot. This restrictive mode needs an FPS comparison."
        };
        reservedProcessors.Enabled = UsesReservedProcessorCount(mode);
    }

    internal static bool UsesManualMainProcessorCount(CpuAssignmentMode mode)
    {
        return mode is CpuAssignmentMode.SplitLanes or CpuAssignmentMode.AdaptiveSharedPools;
    }

    internal static bool UsesManualFollowerProcessorCount(CpuAssignmentMode mode)
    {
        return mode == CpuAssignmentMode.SplitLanes;
    }

    internal static bool UsesReservedProcessorCount(CpuAssignmentMode mode)
    {
        return mode is CpuAssignmentMode.SplitLanes or CpuAssignmentMode.AdaptiveSharedPools;
    }

    private void UpdateRoleControls(IReadOnlyList<OptimizerClientSnapshot> snapshots)
    {
        var selectedProcessId = roleClientInput.SelectedItem is RoleClientItem current ? current.ProcessId : (int?)null;
        var desiredItems = snapshots
            .Select(snapshot => new RoleClientItem(snapshot.ProcessId, snapshot.ClientName, snapshot.IsMainCandidate, snapshot.IsMain))
            .ToList();
        var contentsChanged = roleClientInput.Items.Count != desiredItems.Count;
        if (!contentsChanged)
        {
            for (var index = 0; index < desiredItems.Count; index++)
            {
                if (roleClientInput.Items[index] is not RoleClientItem existing || existing != desiredItems[index])
                {
                    contentsChanged = true;
                    break;
                }
            }
        }

        if (contentsChanged && !roleClientInput.Focused && !roleClientInput.DroppedDown)
        {
            roleClientInput.BeginUpdate();
            try
            {
                roleClientInput.Items.Clear();
                foreach (var item in desiredItems)
                {
                    roleClientInput.Items.Add(item);
                }

                var selectedItem = roleClientInput.Items
                    .OfType<RoleClientItem>()
                    .FirstOrDefault(item => selectedProcessId.HasValue && item.ProcessId == selectedProcessId.Value)
                    ?? roleClientInput.Items.OfType<RoleClientItem>().FirstOrDefault();
                roleClientInput.SelectedItem = selectedItem;
            }
            finally
            {
                roleClientInput.EndUpdate();
            }
        }

        SyncRoleInputFromSelectedClient();
        UpdateMainClientsLabel(snapshots);
    }

    private void SyncRoleInputFromSelectedClient()
    {
        if (refreshing && roleInput.Focused) return;
        if (roleClientInput.SelectedItem is not RoleClientItem item)
        {
            roleInput.SelectedItem = null;
            return;
        }

        var target = item.IsMainCandidate ? "Main" : "Follower";
        if (!Equals(roleInput.SelectedItem, target)) roleInput.SelectedItem = target;
        SetStepperValueIfIdle(mainPriority, Math.Max(1, optimizer.Settings.GetMainPriority(item.ClientName)));
        mainPriority.Enabled = item.IsMainCandidate;
    }

    private void UpdateMainClientsLabel(IReadOnlyList<OptimizerClientSnapshot> snapshots)
    {
        var mainNames = snapshots.Where(snapshot => snapshot.IsMainCandidate).Select(snapshot =>
        {
            var role = snapshot.IsMain ? "active" : "currently follower";
            return $"{optimizer.Settings.GetMainPriority(snapshot.ClientName)}. {snapshot.ClientName} ({role})";
        }).ToList();

        var mainText = mainNames.Count == 0
            ? "Main clients: none"
            : $"Main clients: {string.Join(", ", mainNames)}";
        // Assign the complete text once. Resetting then appending on every sample
        // repaints this transparent label and its parent twice, even when unchanged.
        var text = mainText + Environment.NewLine + optimizer.MemoryStatusText;
        if (mainClientsLabel.Text != text) mainClientsLabel.Text = text;
    }

    private void ApplyThemeRecursive(Control control)
    {
        switch (control)
        {
            case OptimizerPanel panel:
                panel.PanelColor = Color.FromArgb(236, palette.Card);
                panel.BorderColor = palette.Border;
                break;
            case Label label:
                label.ForeColor = label.Font.Bold ? palette.Text : palette.Muted;
                break;
            case CheckBox checkBox:
                checkBox.ForeColor = palette.Text;
                checkBox.BackColor = Color.Transparent;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = palette.ListBack;
                comboBox.ForeColor = palette.Text;
                break;
            case NumericUpDown numeric:
                numeric.BackColor = palette.ListBack;
                numeric.ForeColor = palette.Text;
                break;
            case NewsPillButton pill:
                pill.Palette = palette;
                pill.ForeColor = Color.White;
                break;
            case Button button:
                button.BackColor = ReferenceEquals(button, restoreButton) ? palette.Danger : palette.Secondary;
                button.ForeColor = Color.White;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyThemeRecursive(child);
        }
    }

    internal static Color NativeGridColor(Color color)
    {
        return Color.FromArgb(255, color.R, color.G, color.B);
    }

    private sealed class BufferedTableLayoutPanel : TableLayoutPanel
    {
        public BufferedTableLayoutPanel() => DoubleBuffered = true;
    }

    private sealed class OptimizerPanel : Panel
    {
        public int Radius { get; set; } = 18;
        public Color PanelColor { get; set; } = Color.FromArgb(32, 32, 48);
        public Color BorderColor { get; set; } = Color.FromArgb(80, 80, 110);

        public OptimizerPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(ClientRectangle with { Width = Width - 1, Height = Height - 1 }, Radius);
            using var brush = new SolidBrush(PanelColor);
            using var pen = new Pen(BorderColor, 1);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }

        private static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0) return path;
            radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
            var diameter = radius * 2;
            var rect = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(rect, 180, 90);
            rect.X = bounds.Right - diameter;
            path.AddArc(rect, 270, 90);
            rect.Y = bounds.Bottom - diameter;
            path.AddArc(rect, 0, 90);
            rect.X = bounds.Left;
            path.AddArc(rect, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed record RoleClientItem(int ProcessId, string ClientName, bool IsMainCandidate, bool IsMain)
    {
        public override string ToString()
        {
            return ClientName;
        }
    }
}
