using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace PotatoLauncher;

internal sealed class OptimizerMonitorForm : Form
{
    private readonly IntegratedOptimizerService optimizer;
    private ThemePalette palette;
    private readonly DataGridView grid = new();
    private readonly Label summaryLabel = new();
    private readonly Label gpuStatusLabel = new();
    private readonly CheckBox optimizerEnabled = new();
    private readonly CheckBox cpuPriorityEnabled = new();
    private readonly CheckBox trimEnabled = new();
    private readonly ComboBox assignmentMode = new();
    private readonly ComboBox mainProcessors = new();
    private readonly ComboBox followerProcessors = new();
    private readonly ComboBox followerPriority = new();
    private readonly ComboBox roleClientInput = new();
    private readonly ComboBox roleInput = new();
    private readonly Label mainClientsLabel = new();
    private readonly NumericUpDown reservedProcessors = new();
    private readonly NumericUpDown trimTrigger = new();
    private readonly Button applyButton = new();
    private readonly Button restoreButton = new();
    private readonly Button trimButton = new();
    private bool refreshing;

    public OptimizerMonitorForm(IntegratedOptimizerService optimizer, ThemePalette palette)
    {
        this.optimizer = optimizer;
        this.palette = palette;
        Text = "Potato Optimizer";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1120, 740);
        MinimumSize = new Size(940, 640);
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        BuildUi();
        ApplyTheme(palette);
        optimizer.Updated += OptimizerUpdated;
        FormClosed += (_, _) => optimizer.Updated -= OptimizerUpdated;
        RefreshView();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 236));
        Controls.Add(root);

        var header = new OptimizerPanel { Dock = DockStyle.Fill, Radius = 20 };
        root.Controls.Add(header, 0, 0);
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(18, 12, 18, 12), BackColor = Color.Transparent };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
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

        optimizerEnabled.Text = "Enable optimizer";
        optimizerEnabled.Dock = DockStyle.Top;
        optimizerEnabled.Height = 26;
        optimizerEnabled.CheckedChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.SetOptimizerEnabled(optimizerEnabled.Checked);
            RefreshView();
        };
        cpuPriorityEnabled.Text = "Manage CPU / priority";
        cpuPriorityEnabled.Dock = DockStyle.Top;
        cpuPriorityEnabled.Height = 26;
        cpuPriorityEnabled.CheckedChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.CpuPriorityManagementEnabled = cpuPriorityEnabled.Checked;
            optimizer.SaveSettings();
        };
        trimEnabled.Text = "Trim memory";
        trimEnabled.Dock = DockStyle.Top;
        trimEnabled.Height = 26;
        trimEnabled.CheckedChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.WorkingSetTrimEnabled = trimEnabled.Checked;
            optimizer.SaveSettings();
        };
        var toggles = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        toggles.Controls.Add(optimizerEnabled);
        toggles.Controls.Add(cpuPriorityEnabled);
        toggles.Controls.Add(trimEnabled);
        headerLayout.Controls.Add(toggles, 1, 0);

        gpuStatusLabel.Dock = DockStyle.Fill;
        gpuStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        gpuStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        gpuStatusLabel.BackColor = Color.Transparent;
        headerLayout.Controls.Add(gpuStatusLabel, 2, 0);

        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeight = 34;
        grid.Dock = DockStyle.Fill;
        grid.EnableHeadersVisualStyles = false;
        grid.ReadOnly = false;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 32;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.CellValueChanged += GridCellValueChanged;
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Client", HeaderText = "Client", ReadOnly = true, FillWeight = 175 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pid", HeaderText = "PID", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cpu", HeaderText = "CPU", ReadOnly = true, FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Gpu", HeaderText = "GPU", ReadOnly = true, FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ram", HeaderText = "RAM", ReadOnly = true, FillWeight = 86 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Private", HeaderText = "Private", ReadOnly = true, FillWeight = 86 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Threads", HeaderText = "Threads", ReadOnly = true, FillWeight = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "Role", ReadOnly = true, FillWeight = 78 });
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Priority", HeaderText = "Priority", FillWeight = 116, FlatStyle = FlatStyle.Flat });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Affinity", HeaderText = "Affinity", ReadOnly = true, FillWeight = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Trim", HeaderText = "Last trim", ReadOnly = true, FillWeight = 96 });
        root.Controls.Add(grid, 0, 1);

        var controls = new OptimizerPanel { Dock = DockStyle.Fill, Radius = 20 };
        root.Controls.Add(controls, 0, 2);
        var controlGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(18, 14, 18, 14), BackColor = Color.Transparent };
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        controlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        controlGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        controls.Controls.Add(controlGrid);

        assignmentMode.DropDownStyle = ComboBoxStyle.DropDownList;
        assignmentMode.DataSource = Enum.GetValues<CpuAssignmentMode>();
        assignmentMode.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.CpuAssignmentMode = (CpuAssignmentMode)assignmentMode.SelectedItem!;
            optimizer.SaveSettings();
        };
        mainProcessors.DropDownStyle = ComboBoxStyle.DropDownList;
        mainProcessors.DataSource = OptimizerSettings.AllowedMainLogicalProcessors.ToList();
        mainProcessors.SelectedIndexChanged += (_, _) => SaveSelectedProcessorCounts();
        followerProcessors.DropDownStyle = ComboBoxStyle.DropDownList;
        followerProcessors.DataSource = OptimizerSettings.AllowedFollowerLogicalProcessors.ToList();
        followerProcessors.SelectedIndexChanged += (_, _) => SaveSelectedProcessorCounts();
        followerPriority.DropDownStyle = ComboBoxStyle.DropDownList;
        followerPriority.DataSource = OptimizerSettings.AllowedClientPriorityOverrides.ToList();
        followerPriority.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.FollowerPriorityClass = (ProcessPriorityClass)followerPriority.SelectedItem!;
            optimizer.SaveSettings();
        };
        roleClientInput.DropDownStyle = ComboBoxStyle.DropDownList;
        roleClientInput.SelectedIndexChanged += (_, _) => SyncRoleInputFromSelectedClient();
        roleInput.DropDownStyle = ComboBoxStyle.DropDownList;
        roleInput.Items.AddRange(["Main", "Follower"]);
        roleInput.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            if (roleClientInput.SelectedItem is not RoleClientItem item) return;
            optimizer.SetMainClient(item.ProcessId, string.Equals(roleInput.SelectedItem?.ToString(), "Main", StringComparison.OrdinalIgnoreCase));
            RefreshView();
        };
        mainClientsLabel.Dock = DockStyle.Fill;
        mainClientsLabel.AutoEllipsis = true;
        mainClientsLabel.BackColor = Color.Transparent;
        mainClientsLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        ConfigureStepper(reservedProcessors, 0, Math.Max(0, Environment.ProcessorCount - 1));
        ConfigureStepper(trimTrigger, 128, 32768);
        reservedProcessors.ValueChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.SystemReservedLogicalProcessors = (int)reservedProcessors.Value;
            optimizer.SaveSettings();
        };
        trimTrigger.ValueChanged += (_, _) =>
        {
            if (refreshing) return;
            optimizer.Settings.TrimTriggerMBPerClient = (int)trimTrigger.Value;
            optimizer.SaveSettings();
        };

        controlGrid.Controls.Add(Field("CPU lanes", assignmentMode), 0, 0);
        controlGrid.Controls.Add(Field("Main logical processors", mainProcessors), 1, 0);
        controlGrid.Controls.Add(Field("Follower logical processors", followerProcessors), 2, 0);
        controlGrid.Controls.Add(Field("Follower priority", followerPriority), 3, 0);
        controlGrid.Controls.Add(Field("Reserved logical processors", reservedProcessors), 0, 1);
        controlGrid.Controls.Add(Field("Trim trigger MB", trimTrigger), 1, 1);
        controlGrid.Controls.Add(Field("Client", roleClientInput), 2, 1);
        controlGrid.Controls.Add(Field("Selected client role", roleInput), 3, 1);
        controlGrid.Controls.Add(mainClientsLabel, 0, 2);
        controlGrid.SetColumnSpan(mainClientsLabel, 2);

        applyButton.Text = "Apply now";
        applyButton.Click += (_, _) => { optimizer.ApplyNow(); RefreshView(); };
        restoreButton.Text = "Restore clients";
        restoreButton.Click += (_, _) => { optimizer.RestoreClients(); RefreshView(); };
        trimButton.Text = "Trim now";
        trimButton.Click += (_, _) => { optimizer.TrimNow(); RefreshView(); };
        var buttonRow = ButtonRow();
        controlGrid.Controls.Add(buttonRow, 2, 2);
        controlGrid.SetColumnSpan(buttonRow, 2);
    }

    private FlowLayoutPanel ButtonRow()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
        foreach (var button in new[] { applyButton, trimButton, restoreButton })
        {
            button.Width = 130;
            button.Height = 34;
            button.Margin = new Padding(0, 10, 10, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
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
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(RefreshView);
    }

    private void RefreshView()
    {
        if (IsDisposed) return;
        refreshing = true;
        try
        {
            var settings = optimizer.Settings;
            settings.Normalize();
            optimizerEnabled.Checked = settings.OptimizerEnabled;
            cpuPriorityEnabled.Checked = settings.CpuPriorityManagementEnabled;
            trimEnabled.Checked = settings.WorkingSetTrimEnabled;
            SetSelectedItemIfIdle(assignmentMode, settings.CpuAssignmentMode);
            SetSelectedItemIfIdle(mainProcessors, settings.MainLogicalProcessors);
            SetSelectedItemIfIdle(followerProcessors, settings.FollowerLogicalProcessors);
            SetSelectedItemIfIdle(followerPriority, settings.FollowerPriorityClass);
            SetStepperValueIfIdle(reservedProcessors, settings.SystemReservedLogicalProcessors);
            SetStepperValueIfIdle(trimTrigger, settings.TrimTriggerMBPerClient);

            var snapshots = optimizer.GetSnapshots();
            UpdateRoleControls(snapshots);
            UpdateGrid(snapshots);
            var system = optimizer.GetSystemMetrics();
            var clientRam = snapshots.Sum(snapshot => snapshot.WorkingSetBytes) / 1024d / 1024d;
            var clientCpu = snapshots.Sum(snapshot => snapshot.CpuPercent);
            var gpuValues = snapshots.Where(snapshot => snapshot.GpuPercent.HasValue).Select(snapshot => snapshot.GpuPercent!.Value).ToList();
            var clientGpuText = gpuValues.Count == 0 ? "GPU N/A" : $"GPU {gpuValues.Sum():0.0}%";
            var systemGpuText = system.GpuPercent.HasValue ? $"GPU {system.GpuPercent.Value:0.0}%" : "GPU N/A";
            var systemRamPercent = system.TotalMemoryBytes <= 0 ? 0 : system.UsedMemoryBytes / (double)system.TotalMemoryBytes * 100;
            summaryLabel.Text =
                $"Clients: {snapshots.Count} | CPU {clientCpu:0.0}% | {clientGpuText} | RAM {FormatMb(clientRam)}" +
                Environment.NewLine +
                $"System: CPU {system.CpuPercent:0.0}% | {systemGpuText} | RAM {FormatMb(system.UsedMemoryBytes)} / {FormatMb(system.TotalMemoryBytes)} ({systemRamPercent:0}%)";
            gpuStatusLabel.Text = optimizer.GpuStatusText;
        }
        finally
        {
            refreshing = false;
        }
    }

    private void UpdateGrid(IReadOnlyList<OptimizerClientSnapshot> snapshots)
    {
        var priorityColumn = (DataGridViewComboBoxColumn)grid.Columns["Priority"];
        EnsurePriorityItems(priorityColumn);

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
        var defaultPriority = snapshot.IsMain ? ProcessPriorityClass.AboveNormal : optimizer.Settings.FollowerPriorityClass;
        var overridePriority = optimizer.Settings.GetClientPriorityOverride(snapshot.ClientName);
        row.Tag = snapshot;
        SetCell(row, "Main", snapshot.IsMain);
        SetCell(row, "Client", snapshot.ClientName);
        SetCell(row, "Pid", snapshot.ProcessId);
        SetCell(row, "Cpu", $"{snapshot.CpuPercent:0.0}%");
        SetCell(row, "Gpu", snapshot.GpuPercent.HasValue ? $"{snapshot.GpuPercent.Value:0.0}%" : "N/A");
        SetCell(row, "Ram", $"{snapshot.WorkingSetBytes / 1024d / 1024d:0} MB");
        SetCell(row, "Private", $"{snapshot.PrivateBytes / 1024d / 1024d:0} MB");
        SetCell(row, "Threads", snapshot.ThreadCount);
        SetCell(row, "Role", snapshot.IsMain ? "Main" : "Follower");
        SetCell(row, "Priority", overridePriority?.ToString() ?? "Default");
        SetCell(row, "Affinity", snapshot.AffinityMask.HasValue ? ProcessorAffinity.FormatMask(snapshot.AffinityMask.Value) : "N/A");
        SetCell(row, "Trim", snapshot.LastTrimUtc.HasValue ? snapshot.LastTrimUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "-");
        row.Cells["Priority"].ToolTipText = overridePriority is null ? $"Default ({defaultPriority})" : "Priority override";
    }

    private static void EnsurePriorityItems(DataGridViewComboBoxColumn priorityColumn)
    {
        if (priorityColumn.Items.Count > 0) return;
        priorityColumn.Items.Add("Default");
        foreach (var priority in OptimizerSettings.AllowedClientPriorityOverrides)
        {
            priorityColumn.Items.Add(priority.ToString());
        }
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

    private void GridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (refreshing || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
        if (grid.Rows[e.RowIndex].Tag is not OptimizerClientSnapshot snapshot) return;
        var columnName = grid.Columns[e.ColumnIndex].Name;
        if (columnName == "Priority")
        {
            var text = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            if (string.IsNullOrWhiteSpace(text) || text == "Default")
            {
                optimizer.Settings.SetClientPriorityOverride(snapshot.ClientName, null);
            }
            else if (Enum.TryParse<ProcessPriorityClass>(text, out var priority))
            {
                optimizer.Settings.SetClientPriorityOverride(snapshot.ClientName, priority);
            }

            optimizer.SaveSettings();
        }
    }

    private void SaveSelectedProcessorCounts()
    {
        if (refreshing) return;
        if (mainProcessors.SelectedItem is int mainCount) optimizer.Settings.MainLogicalProcessors = mainCount;
        if (followerProcessors.SelectedItem is int followerCount) optimizer.Settings.FollowerLogicalProcessors = followerCount;
        optimizer.SaveSettings();
    }

    private void UpdateRoleControls(IReadOnlyList<OptimizerClientSnapshot> snapshots)
    {
        var selectedProcessId = roleClientInput.SelectedItem is RoleClientItem current ? current.ProcessId : (int?)null;
        if (!roleClientInput.Focused && !roleClientInput.DroppedDown)
        {
            roleClientInput.BeginUpdate();
            try
            {
                roleClientInput.Items.Clear();
                foreach (var item in snapshots.Select(snapshot => new RoleClientItem(snapshot.ProcessId, snapshot.ClientName, snapshot.IsMain)))
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
        var mainNames = snapshots.Where(snapshot => snapshot.IsMain).Select(snapshot => snapshot.ClientName).ToList();
        mainClientsLabel.Text = mainNames.Count == 0
            ? "Main clients: none"
            : $"Main clients: {string.Join(", ", mainNames)}";
    }

    private void SyncRoleInputFromSelectedClient()
    {
        if (refreshing && roleInput.Focused) return;
        if (roleClientInput.SelectedItem is not RoleClientItem item)
        {
            roleInput.SelectedItem = null;
            return;
        }

        var target = item.IsMain ? "Main" : "Follower";
        if (!Equals(roleInput.SelectedItem, target)) roleInput.SelectedItem = target;
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
            case Button button:
                button.BackColor = button == restoreButton ? palette.Danger : palette.Secondary;
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

    private sealed record RoleClientItem(int ProcessId, string ClientName, bool IsMain)
    {
        public override string ToString()
        {
            return ClientName;
        }
    }
}
