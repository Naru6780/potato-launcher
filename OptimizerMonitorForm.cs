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
    private readonly CheckBox trimEnabled = new();
    private readonly CheckBox clientPolicyEnabled = new();
    private readonly NumericUpDown targetFps = new();
    private readonly CheckBox enforceFrameLimit = new();
    private readonly ToolTip toolTip = new();
    private readonly ComboBox trimMode = new();
    private readonly ComboBox roleClientInput = new();
    private readonly ComboBox roleInput = new();
    private readonly Label mainClientsLabel = new();
    private readonly NumericUpDown mainPriority = new();
    private readonly NumericUpDown trimTrigger = new();
    private readonly Button saveButton = new NewsPillButton();
    private readonly Button trimButton = new NewsPillButton();
    private bool refreshing;
    private bool refreshQueued;
    private bool moveOrResizeActive;
    private bool closing;
    private DateTime lastPeriodicRefreshUtc = DateTime.MinValue;

    public OptimizerMonitorForm(IntegratedOptimizerService optimizer, ThemePalette palette, Func<bool>? notificationsEnabled = null)
    {
        this.optimizer = optimizer;
        this.notificationsEnabled = notificationsEnabled ?? (() => true);
        this.palette = palette;
        Text = "Potato Optimizer";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1120, 720);
        MinimumSize = new Size(940, 620);
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        BuildUi();
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 192));
        Controls.Add(root);

        var header = new OptimizerPanel { Dock = DockStyle.Fill, Radius = 20 };
        root.Controls.Add(header, 0, 0);
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 12, 18, 12), BackColor = Color.Transparent };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
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

        trimEnabled.Text = "RAM trimming";
        trimEnabled.Dock = DockStyle.None;
        trimEnabled.Width = 230;
        trimEnabled.Height = 26;
        toolTip.SetToolTip(trimEnabled, "Automatically trim follower working sets when memory pressure is detected.");
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
        toolTip.SetToolTip(trimMode, "Pressure-aware trims only during system memory pressure. Auto trim at threshold trims followers above the configured Trim trigger MB value.");
        trimMode.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.MemoryTrimMode = trimMode.SelectedIndex == 1
                ? MemoryTrimMode.Threshold
                : MemoryTrimMode.PressureAware;
            optimizer.SaveSettings();
            RefreshView();
        };

        gpuStatusLabel.Dock = DockStyle.Fill;
        gpuStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        gpuStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        gpuStatusLabel.BackColor = Color.Transparent;
        clientPolicyEnabled.Text = "Keep every client at its FPS cap";
        clientPolicyEnabled.Dock = DockStyle.Top;
        clientPolicyEnabled.Height = 26;
        clientPolicyEnabled.BackColor = Color.Transparent;
        toolTip.SetToolTip(clientPolicyEnabled,
            "Stops Windows from throttling covered or minimized clients (which drops them below their frame cap),\n" +
            "gives the client you are playing and your main clients Above Normal priority, keeps the others at Normal,\n" +
            "and gives background clients low memory priority so Windows reclaims their RAM first.\nNever lowers any client's frame rate.");
        clientPolicyEnabled.CheckedChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.SetClientPolicyEnabled(clientPolicyEnabled.Checked);
            RefreshView();
        };
        var headerRight = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        headerRight.Controls.Add(gpuStatusLabel);
        headerRight.Controls.Add(clientPolicyEnabled);
        headerLayout.Controls.Add(headerRight, 1, 0);

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
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Fps", HeaderText = "FPS", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cap", HeaderText = "Cap", ReadOnly = true, FillWeight = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "Role", ReadOnly = true, FillWeight = 78 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pid", HeaderText = "PID", ReadOnly = true, FillWeight = 60, Visible = false });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cpu", HeaderText = "CPU", ReadOnly = true, FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Gpu", HeaderText = "GPU", ReadOnly = true, FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ram", HeaderText = "RAM in use", ReadOnly = true, FillWeight = 86 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Private", HeaderText = "RAM committed", ReadOnly = true, FillWeight = 96 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Threads", HeaderText = "Threads", ReadOnly = true, FillWeight = 72, Visible = false });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Trim", HeaderText = "Last trim", ReadOnly = true, FillWeight = 96 });
        root.Controls.Add(grid, 0, 1);

        var controls = new OptimizerPanel { Dock = DockStyle.Fill, Radius = 20 };
        root.Controls.Add(controls, 0, 2);
        var controlGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 3, Padding = new Padding(18, 14, 18, 14), BackColor = Color.Transparent };
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        controls.Controls.Add(controlGrid);

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
        ConfigureStepper(mainPriority, 1, 100);
        ConfigureStepper(trimTrigger, 128, 32768);
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
        controlGrid.Controls.Add(Field("Client", roleClientInput), 0, 1);
        controlGrid.Controls.Add(Field("Selected client role", roleInput), 1, 1);
        controlGrid.Controls.Add(Field("Main selection order (1 wins)", mainPriority), 2, 1);
        targetFps.Minimum = 15;
        targetFps.Maximum = 360;
        targetFps.Value = Math.Clamp(optimizer.Settings.TargetFps, 15, 360);
        toolTip.SetToolTip(targetFps, "The frame cap every client should hold. Used for the FPS colours, \"at cap\" count and capacity estimate.");
        targetFps.ValueChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.TargetFps = (int)targetFps.Value;
            optimizer.SaveSettings();
        };
        controlGrid.Controls.Add(Field("Target FPS (every client)", targetFps), 3, 1);
        enforceFrameLimit.Text = "Set in-game at launch";
        enforceFrameLimit.AutoSize = true;
        enforceFrameLimit.BackColor = Color.Transparent;
        toolTip.SetToolTip(enforceFrameLimit,
            "Before each launch, sets FFXIV's own Frame Rate limit (FFXIV.cfg) to the option matching the target.\n" +
            "The game's limiter is the only cap that holds while a window is covered or minimized.\nRunning clients pick it up when relaunched.");
        enforceFrameLimit.CheckedChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.EnforceInGameFrameLimit = enforceFrameLimit.Checked;
            optimizer.SaveSettings();
        };
        controlGrid.Controls.Add(Field("In-game frame limit", enforceFrameLimit), 4, 1);
        controlGrid.Controls.Add(Field("Trim trigger MB", trimTrigger), 0, 2);
        controlGrid.Controls.Add(mainClientsLabel, 1, 2);
        controlGrid.SetColumnSpan(mainClientsLabel, 2);

        saveButton.Text = "Save";
        saveButton.Tag = "Secondary";
        saveButton.Click += (_, _) =>
        {
            optimizer.SaveSettings();
            RefreshView();
            ShowFeedback("Optimizer settings saved.");
        };
        trimButton.Text = "Optimize RAM Now";
        trimButton.Tag = "Secondary";
        trimButton.Click += (_, _) =>
        {
            optimizer.TrimNow();
            RefreshView();
            ShowFeedback("RAM optimization applied.");
        };
        var buttonRow = ButtonRow();
        controlGrid.Controls.Add(buttonRow, 3, 2);
        controlGrid.SetColumnSpan(buttonRow, 2);
        UpdateModeControls();
    }

    private FlowLayoutPanel AutoOptionsRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 3, 0, 0)
        };
        trimEnabled.Margin = new Padding(0, 3, 22, 0);
        trimMode.Margin = new Padding(0, 2, 22, 0);
        panel.Controls.Add(trimEnabled);
        panel.Controls.Add(trimMode);
        return panel;
    }

    private void ShowFeedback(string message)
    {
        if (!notificationsEnabled()) return;
        AppNotification.Show(this, "Potato Optimizer", message);
    }

    private FlowLayoutPanel ButtonRow()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
        foreach (var button in new[] { trimButton, saveButton })
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
        if (!force && now - lastPeriodicRefreshUtc < TimeSpan.FromMilliseconds(2000)) return;

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
        return roleClientInput.DroppedDown ||
               roleInput.DroppedDown ||
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
            trimEnabled.Checked = settings.WorkingSetTrimEnabled;
            clientPolicyEnabled.Checked = settings.ClientPolicyEnabled;
            SetSelectedItemIfIdle(trimMode, settings.MemoryTrimMode == MemoryTrimMode.Threshold
                ? "Auto trim at threshold"
                : "Pressure-aware");
            UpdateModeControls();
            SetStepperValueIfIdle(trimTrigger, settings.TrimTriggerMBPerClient);
            SetStepperValueIfIdle(targetFps, settings.TargetFps);
            enforceFrameLimit.Checked = settings.EnforceInGameFrameLimit;

            var snapshots = optimizer.GetSnapshots();
            UpdateRoleControls(snapshots);
            UpdateGrid(snapshots);
            var system = optimizer.GetSystemMetrics();
            var systemGpuText = system.GpuPercent.HasValue ? $"GPU {system.GpuPercent.Value:0}%" : "GPU N/A";
            var withFps = snapshots.Where(snapshot => snapshot.Fps.HasValue).ToList();
            var atCap = withFps.Count(snapshot => snapshot.Fps!.Value >= settings.TargetFps - 3);
            var fpsText = withFps.Count == 0 ? "FPS unavailable" : $"{atCap}/{withFps.Count} at {settings.TargetFps} FPS";
            var capacity = optimizer.EstimateCapacity(snapshots, system);
            var findings = OptimizerDiagnostics.Get(snapshots, settings.TargetFps);
            summaryLabel.Text =
                $"{snapshots.Count} client{(snapshots.Count == 1 ? "" : "s")}  ·  {fpsText}  ·  CPU {system.CpuPercent:0}%  ·  {systemGpuText}  ·  RAM {FormatMb(system.UsedMemoryBytes)} / {FormatMb(system.TotalMemoryBytes)}" +
                Environment.NewLine + capacity.Summary +
                (findings.Count > 0 ? Environment.NewLine + "⚠ " + findings[0] + (findings.Count > 1 ? $"  (+{findings.Count - 1}, hover)" : "") : "");
            toolTip.SetToolTip(summaryLabel, findings.Count > 0 ? string.Join(Environment.NewLine + Environment.NewLine, findings) : capacity.Summary);
            if (grid.Columns["Trim"]!.Visible != settings.WorkingSetTrimEnabled) grid.Columns["Trim"]!.Visible = settings.WorkingSetTrimEnabled;
            gpuStatusLabel.Text = optimizer.GpuStatusText;
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
        SetCell(row, "Fps", snapshot.Fps.HasValue ? $"{snapshot.Fps.Value:0}" : "—");
        // The game's own limiter is the only cap that holds while covered or minimized.
        SetCell(row, "Cap", snapshot.EngineFrameLimit switch { null => "—", 0 => "driver", var limit => $"game {limit}" });
        var capCell = row.Cells["Cap"];
        // "driver" is fine while the client is on screen (DLSS 5 clients run that way); orange once it actually runs above target.
        var capColor = snapshot.EngineFrameLimit == 0 && snapshot.Fps is double capFps && capFps > optimizer.Settings.TargetFps + 5
            ? Color.FromArgb(240, 170, 80) : grid.DefaultCellStyle.ForeColor;
        if (capCell.Style.ForeColor != capColor) capCell.Style.ForeColor = capColor;
        var fpsCell = row.Cells["Fps"];
        var fpsColor = snapshot.Fps is double fps
            ? fps < optimizer.Settings.TargetFps - 3 ? Color.FromArgb(232, 84, 104) : Color.FromArgb(76, 200, 130)
            : grid.DefaultCellStyle.ForeColor;
        if (fpsCell.Style.ForeColor != fpsColor) fpsCell.Style.ForeColor = fpsColor;
        SetCell(row, "Pid", snapshot.ProcessId);
        SetCell(row, "Cpu", $"{snapshot.CpuPercent:0.0}%");
        SetCell(row, "Gpu", snapshot.GpuPercent.HasValue ? $"{snapshot.GpuPercent.Value:0.0}%" : "N/A");
        SetCell(row, "Ram", $"{snapshot.WorkingSetBytes / 1024d / 1024d:0} MB");
        SetCell(row, "Private", $"{snapshot.PrivateBytes / 1024d / 1024d:0} MB");
        SetCell(row, "Threads", snapshot.ThreadCount);
        SetCell(row, "Role", string.IsNullOrEmpty(snapshot.Role) ? "Background" : snapshot.Role);
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

    private void UpdateModeControls()
    {
        trimMode.Enabled = optimizer.Settings.WorkingSetTrimEnabled;
        trimTrigger.Enabled = optimizer.Settings.WorkingSetTrimEnabled && optimizer.Settings.MemoryTrimMode == MemoryTrimMode.Threshold;
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
        if (!Equals(roleInput.SelectedItem, target))
        {
            // Only reflect the stored role; the user did not change it, so it must not re-save (and re-prioritize) it.
            var wasRefreshing = refreshing;
            refreshing = true;
            try { roleInput.SelectedItem = target; }
            finally { refreshing = wasRefreshing; }
        }
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

        mainClientsLabel.Text = mainNames.Count == 0
            ? "Main clients: none"
            : $"Main clients: {string.Join(", ", mainNames)}";
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
                button.BackColor = palette.Secondary;
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
