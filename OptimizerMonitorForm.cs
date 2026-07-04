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
        Size = new Size(1080, 700);
        MinimumSize = new Size(900, 560);
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
        grid.RowHeadersDefaultCellStyle.BackColor = NativeGridColor(palette.Card);
        grid.RowHeadersDefaultCellStyle.ForeColor = palette.Text;
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));
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
        var toggles = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        toggles.Controls.Add(trimEnabled);
        toggles.Controls.Add(cpuPriorityEnabled);
        toggles.Controls.Add(optimizerEnabled);
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
        grid.CellValueChanged += GridCellValueChanged;
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Main", HeaderText = "Main", Width = 54, FillWeight = 42 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Client", HeaderText = "Client", ReadOnly = true, FillWeight = 175 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pid", HeaderText = "PID", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cpu", HeaderText = "CPU", ReadOnly = true, FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Gpu", HeaderText = "GPU", ReadOnly = true, FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ram", HeaderText = "RAM", ReadOnly = true, FillWeight = 86 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Private", HeaderText = "Private", ReadOnly = true, FillWeight = 86 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Threads", HeaderText = "Threads", ReadOnly = true, FillWeight = 72 });
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

        applyButton.Text = "Apply now";
        applyButton.Click += (_, _) => { optimizer.ApplyNow(); RefreshView(); };
        restoreButton.Text = "Restore clients";
        restoreButton.Click += (_, _) => { optimizer.RestoreClients(); RefreshView(); };
        trimButton.Text = "Trim now";
        trimButton.Click += (_, _) => { optimizer.TrimNow(); RefreshView(); };
        var buttonRow = ButtonRow();
        controlGrid.Controls.Add(buttonRow, 2, 1);
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
            assignmentMode.SelectedItem = settings.CpuAssignmentMode;
            mainProcessors.SelectedItem = settings.MainLogicalProcessors;
            followerProcessors.SelectedItem = settings.FollowerLogicalProcessors;
            followerPriority.SelectedItem = settings.FollowerPriorityClass;
            reservedProcessors.Value = Math.Clamp(settings.SystemReservedLogicalProcessors, (int)reservedProcessors.Minimum, (int)reservedProcessors.Maximum);
            trimTrigger.Value = Math.Clamp(settings.TrimTriggerMBPerClient, (int)trimTrigger.Minimum, (int)trimTrigger.Maximum);

            var snapshots = optimizer.GetSnapshots();
            UpdateGrid(snapshots);
            var totalRam = snapshots.Sum(snapshot => snapshot.WorkingSetBytes) / 1024d / 1024d;
            var totalCpu = snapshots.Sum(snapshot => snapshot.CpuPercent);
            var gpuValues = snapshots.Where(snapshot => snapshot.GpuPercent.HasValue).Select(snapshot => snapshot.GpuPercent!.Value).ToList();
            var gpuText = gpuValues.Count == 0 ? "GPU N/A" : $"GPU {gpuValues.Sum():0.0}%";
            summaryLabel.Text = $"{snapshots.Count} client(s)  |  CPU {totalCpu:0.0}%  |  {gpuText}  |  RAM {totalRam:0} MB";
            gpuStatusLabel.Text = optimizer.GpuStatusText;
        }
        finally
        {
            refreshing = false;
        }
    }

    private void UpdateGrid(IReadOnlyList<OptimizerClientSnapshot> snapshots)
    {
        grid.Rows.Clear();
        var priorityColumn = (DataGridViewComboBoxColumn)grid.Columns["Priority"];
        priorityColumn.Items.Clear();
        priorityColumn.Items.Add("Default");
        foreach (var priority in OptimizerSettings.AllowedClientPriorityOverrides)
        {
            priorityColumn.Items.Add(priority.ToString());
        }

        foreach (var snapshot in snapshots)
        {
            var defaultPriority = snapshot.IsMain ? ProcessPriorityClass.AboveNormal : optimizer.Settings.FollowerPriorityClass;
            var overridePriority = optimizer.Settings.GetClientPriorityOverride(snapshot.ClientName);
            var rowIndex = grid.Rows.Add(
                snapshot.IsMain,
                snapshot.ClientName,
                snapshot.ProcessId,
                $"{snapshot.CpuPercent:0.0}%",
                snapshot.GpuPercent.HasValue ? $"{snapshot.GpuPercent.Value:0.0}%" : "N/A",
                $"{snapshot.WorkingSetBytes / 1024d / 1024d:0} MB",
                $"{snapshot.PrivateBytes / 1024d / 1024d:0} MB",
                snapshot.ThreadCount,
                overridePriority?.ToString() ?? "Default",
                snapshot.AffinityMask.HasValue ? ProcessorAffinity.FormatMask(snapshot.AffinityMask.Value) : "N/A",
                snapshot.LastTrimUtc.HasValue ? snapshot.LastTrimUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "-");
            var row = grid.Rows[rowIndex];
            row.Tag = snapshot;
            row.Cells["Priority"].ToolTipText = overridePriority is null ? $"Default ({defaultPriority})" : "Priority override";
        }
    }

    private void GridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (refreshing || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
        if (grid.Rows[e.RowIndex].Tag is not OptimizerClientSnapshot snapshot) return;
        var columnName = grid.Columns[e.ColumnIndex].Name;
        if (columnName == "Main")
        {
            optimizer.SetMainClient(snapshot.ProcessId, grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value is true);
            return;
        }

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
}
