using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PotatoLauncher.Studio;

internal sealed class CommandStudioForm : Form
{
    private readonly string profilePath;
    private StudioProfile profile;
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None };
    private readonly FlowLayoutPanel tiles = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
    private readonly ComboBox targets = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label breadcrumb = new() { AutoSize = true, Margin = new Padding(10) };
    private readonly TextBox label = new() { Dock = DockStyle.Fill, MaxLength = 200 };
    private readonly CheckBox group = new() { Text = "Group (opens children; does not send commands)", AutoSize = true };
    private readonly NumericUpDown iconId = new() { Minimum = -999999, Maximum = 999999, Width = 100 };
    private readonly Label artworkLabel = new() { AutoSize = false, Width = 185, Height = 30, AutoEllipsis = true };
    private readonly ComboBox parent = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView steps = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly NumericUpDown tileSize = new() { Minimum = 64, Maximum = 160, Width = 58, Increment = 8 };
    private readonly CheckBox animate = new() { Text = "Animate", AutoSize = true };
    private readonly Button stopButton;
    private readonly FlowLayoutPanel toolsBar;
    private readonly FlowLayoutPanel editTools;
    private readonly TableLayoutPanel root;
    private readonly TableLayoutPanel body;
    private readonly Panel treePanel;
    private readonly Button editModeButton;
    private readonly Panel editor;
    private readonly ToolTip tips = new();
    private readonly Dictionary<int, Bitmap> gameIcons = [];
    private readonly Dictionary<string, Bitmap?> artworkCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer transition = new() { Interval = 16 };
    private DateTime transitionStart;
    private CancellationTokenSource? sequence;
    private string? groupId;
    private string? selectedId;
    private string chosenArtwork = "";
    private string chosenBackground = "#29243E";
    private string chosenForeground = "#FFFFFF";
    private bool loading;
    private bool refreshing;
    private bool editorDirty;
    private bool profileReadFailed;
    private bool editMode;

    private sealed record Target(RunningGameClient Process, BridgeReply State)
    {
        public override string ToString() => $"{State.Character} · PID {Process.ProcessId} · {(State.Armed ? "Ready" : "OFF")}";
    }
    private sealed record ParentChoice(string? Id, string Name) { public override string ToString() => Name; }

    public CommandStudioForm(string dataRoot, ThemePalette palette, bool connectOnShow = true)
    {
        profilePath = Path.Combine(dataRoot, "Command Studio", "profile.json");
        Text = "Command Studio — Potato Launcher (experimental bridge)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1260, 800);
        MinimumSize = new Size(1080, 700);
        Font = new Font("Segoe UI", 9F);
        BackColor = palette.Back1; ForeColor = palette.Text;
        profile = new StudioProfile();
        try
        {
            if (File.Exists(profilePath)) profile = StudioProfiles.Load(profilePath);
            else if (File.Exists(StudioProfiles.DefaultQoLBarPath)) profile = StudioProfiles.Import(StudioProfiles.DefaultQoLBarPath, "Jobs (Multi)");
        }
        catch (Exception ex) { profileReadFailed = File.Exists(profilePath); status.Text = "Profile not loaded: " + ex.Message; }
        selectedId = profile.Buttons.FirstOrDefault()?.Id;

        root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);
        toolsBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = false };
        toolsBar.Controls.Add(ActionButton("Refresh clients", async () => await RefreshClients()));
        toolsBar.Controls.Add(targets);
        stopButton = ActionButton("Stop sequence", () => sequence?.Cancel());
        stopButton.Enabled = false; toolsBar.Controls.Add(stopButton);
        editModeButton = ActionButton("Edit buttons", () => SetEditorMode(!editMode));
        toolsBar.Controls.Add(editModeButton);
        toolsBar.SetFlowBreak(editModeButton, true);
        editTools = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        editTools.Controls.Add(ActionButton("Import QoLBar…", Import));
        editTools.Controls.Add(new Label { Text = "Tile size", AutoSize = true, Margin = new Padding(8) });
        tileSize.Value = profile.TileSize; editTools.Controls.Add(tileSize);
        animate.Checked = profile.Animate; editTools.Controls.Add(animate);
        editTools.Controls.Add(new Label { Text = "Editor mode · action clicks select, never execute", AutoSize = true, Margin = new Padding(8) });
        toolsBar.Controls.Add(editTools);
        root.Controls.Add(toolsBar, 0, 0);
        body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        root.Controls.Add(body, 0, 1);

        treePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        var treeTools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 122 };
        treeTools.Controls.Add(ActionButton("+ Action", () => Add(false)));
        treeTools.Controls.Add(ActionButton("+ Group", () => Add(true)));
        treeTools.Controls.Add(ActionButton("Duplicate", Duplicate));
        treeTools.Controls.Add(ActionButton("Delete", Delete));
        treeTools.Controls.Add(ActionButton("↑", () => MoveButton(-1)));
        treeTools.Controls.Add(ActionButton("↓", () => MoveButton(1)));
        treePanel.Controls.Add(tree); treePanel.Controls.Add(treeTools); body.Controls.Add(treePanel, 0, 0);
        tree.BackColor = palette.ListBack; tree.ForeColor = palette.Text;
        var stage = new Panel { Dock = DockStyle.Fill, BackColor = palette.Card, Padding = new Padding(6) };
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        navigation.Controls.Add(ActionButton("‹ Back", Back));
        navigation.Controls.Add(ActionButton("Home", () => Navigate(null)));
        navigation.Controls.Add(breadcrumb);
        stage.Controls.Add(tiles); stage.Controls.Add(navigation); body.Controls.Add(stage, 1, 0);

        editor = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 13 };
        int[] heights = [25, 32, 32, 25, 32, 35, 35, 36, 25, 0, 34, 42, 36];
        foreach (var h in heights) fields.RowStyles.Add(new RowStyle(h == 0 ? SizeType.Percent : SizeType.Absolute, h == 0 ? 100 : h));
        fields.Controls.Add(new Label { Text = "BUTTON EDITOR · Select in the tree to edit", AutoSize = true }, 0, 0);
        fields.Controls.Add(label, 0, 1); fields.Controls.Add(group, 0, 2);
        fields.Controls.Add(new Label { Text = "Parent group", AutoSize = true }, 0, 3); fields.Controls.Add(parent, 0, 4);
        var artRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        artRow.Controls.Add(ActionButton("Artwork…", ChooseArtwork)); artRow.Controls.Add(ActionButton("Clear", () => { chosenArtwork = ""; artworkLabel.Text = "Game icon / no artwork"; editorDirty = true; }));
        fields.Controls.Add(artRow, 0, 5);
        var iconRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        iconRow.Controls.Add(iconId); iconRow.Controls.Add(artworkLabel); fields.Controls.Add(iconRow, 0, 6);
        var colorRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        colorRow.Controls.Add(ActionButton("Background…", () => ChooseColor(false))); colorRow.Controls.Add(ActionButton("Text color…", () => ChooseColor(true))); fields.Controls.Add(colorRow, 0, 7);
        fields.Controls.Add(new Label { Text = "Action steps · wait AFTER each line (milliseconds)", AutoSize = true }, 0, 8);
        steps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Command", HeaderText = "Slash command", FillWeight = 75, SortMode = DataGridViewColumnSortMode.NotSortable });
        steps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Delay", HeaderText = "Wait ms", FillWeight = 25, SortMode = DataGridViewColumnSortMode.NotSortable });
        steps.BackgroundColor = palette.ListBack;
        steps.DefaultCellStyle.BackColor = palette.ListBack; steps.DefaultCellStyle.ForeColor = palette.Text;
        steps.DefaultCellStyle.SelectionBackColor = palette.Primary; steps.DefaultCellStyle.SelectionForeColor = palette.Text;
        steps.EnableHeadersVisualStyles = false; steps.ColumnHeadersDefaultCellStyle.BackColor = palette.Card; steps.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        steps.DefaultValuesNeeded += (_, e) => e.Row.Cells[1].Value = 180;
        steps.DataError += (_, e) => { e.ThrowException = false; status.Text = "Invalid command-step value."; };
        fields.Controls.Add(steps, 0, 9);
        var stepTools = new FlowLayoutPanel { Dock = DockStyle.Fill };
        stepTools.Controls.Add(ActionButton("Step ↑", () => MoveStep(-1))); stepTools.Controls.Add(ActionButton("Step ↓", () => MoveStep(1)));
        stepTools.Controls.Add(ActionButton("Remove step", () => { if (steps.CurrentRow is { IsNewRow: false } row) steps.Rows.Remove(row); })); fields.Controls.Add(stepTools, 0, 10);
        fields.Controls.Add(ActionButton("Apply & save button", () => ApplyEditor()), 0, 11);
        fields.Controls.Add(new Label { Text = "Group clicks only navigate. Changes stay local; QoLBar is never overwritten.", Dock = DockStyle.Fill }, 0, 12);
        editor.Controls.Add(fields); body.Controls.Add(editor, 2, 0);
        root.Controls.Add(status, 0, 2);

        label.TextChanged += (_, _) => { if (!loading) editorDirty = true; };
        group.CheckedChanged += (_, _) => { steps.Enabled = !group.Checked; if (!loading) editorDirty = true; };
        parent.SelectedIndexChanged += (_, _) => { if (!loading) editorDirty = true; };
        iconId.ValueChanged += (_, _) => { if (!loading) editorDirty = true; };
        steps.CellValueChanged += (_, _) => { if (!loading) editorDirty = true; };
        steps.CellBeginEdit += (_, _) => { if (!loading) editorDirty = true; };
        steps.RowsRemoved += (_, _) => { if (!loading) editorDirty = true; };
        tileSize.ValueChanged += (_, _) => { if (!loading) { if (ConfirmDiscard()) Change(() => profile.TileSize = (int)tileSize.Value); else { loading = true; tileSize.Value = profile.TileSize; loading = false; } } };
        animate.CheckedChanged += (_, _) => { if (!loading) { if (ConfirmDiscard()) Change(() => profile.Animate = animate.Checked); else { loading = true; animate.Checked = profile.Animate; loading = false; } } };
        tree.BeforeSelect += (_, e) => { if (!loading && !ConfirmDiscard()) e.Cancel = true; };
        tree.AfterSelect += (_, e) => { if (!loading && e.Node?.Tag is StudioButton button) { selectedId = button.Id; LoadEditor(); } };
        tree.NodeMouseDoubleClick += (_, e) => { if (e.Node.Tag is StudioButton button && button.IsGroup) Navigate(button.Id); };
        transition.Tick += (_, _) =>
        {
            var elapsed = (DateTime.UtcNow - transitionStart).TotalMilliseconds;
            var offset = (int)(12 * Math.Pow(Math.Max(0, 1 - elapsed / 150), 3));
            tiles.Padding = new Padding(12 + offset, 12, 12, 12);
            if (elapsed >= 150) transition.Stop();
        };
        FormClosing += (_, e) => { if (!ConfirmDiscard()) { e.Cancel = true; return; } sequence?.Cancel(); lifetime.Cancel(); };
        Shown += async (_, _) =>
        {
            if (!connectOnShow) return; // Offline UI fixtures never discover or contact game clients.
            if (profileReadFailed)
                MessageBox.Show(this, "The saved profile could not be loaded. It has not been overwritten. Close the studio and recover profile.json from profile.json.bak before editing.", Text);
            else if (!File.Exists(profilePath)) TrySave();
            await RefreshClients();
        };
        RebuildTree(); RenderTiles(); LoadEditor(); ApplyModeLayout();
    }

    // Editor state is intentionally session-only: opening the studio always shows the clean button surface.
    internal bool SetEditorMode(bool enabled, Func<DialogResult>? chooseUnsaved = null)
    {
        if (enabled == editMode) return true;
        if (sequence is not null) { status.Text = "Stop the active sequence before changing modes."; return false; }
        steps.EndEdit();
        if (!enabled && editorDirty)
        {
            var choice = chooseUnsaved?.Invoke() ?? MessageBox.Show(this,
                "Save the pending button edits before returning to the button view?\nYes: save · No: discard · Cancel: keep editing",
                Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel || (choice == DialogResult.Yes && !ApplyEditor())) return false;
            if (choice == DialogResult.No) LoadEditor();
        }
        editMode = enabled;
        LoadEditor();
        ApplyModeLayout();
        RenderTiles();
        status.Text = enabled ? "Editor mode: select a button to edit. Actions do not execute here."
            : "Button view: choose an origin client, then run your actions. MoP/chat commands may broadcast.";
        return true;
    }

    private void ApplyModeLayout()
    {
        root.SuspendLayout(); body.SuspendLayout();
        treePanel.Visible = editMode; editor.Visible = editMode; editTools.Visible = editMode;
        body.ColumnStyles[0].Width = editMode ? 205 : 0;
        body.ColumnStyles[2].Width = editMode ? 350 : 0;
        root.RowStyles[0].Height = editMode ? 86 : 50;
        editModeButton.Text = editMode ? "Done editing" : "Edit buttons";
        body.ResumeLayout(true); root.ResumeLayout(true);
    }

    private void SelectForEditing(StudioButton button)
    {
        if (sequence is not null || !ConfirmDiscard()) return;
        if (!SetEditorMode(true)) return;
        selectedId = button.Id; RebuildTree(); LoadEditor();
    }

    private async Task ActivateTile(StudioButton button)
    {
        if (editMode)
        {
            if (!ConfirmDiscard()) return;
            selectedId = button.Id; RebuildTree(); LoadEditor();
            if (button.IsGroup) Navigate(button.Id);
        }
        else if (button.IsGroup) Navigate(button.Id);
        else await Execute(button);
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, Padding = new Padding(5, 2, 5, 2), Margin = new Padding(3), Cursor = Cursors.Hand };
        button.Click += (_, _) => action();
        return button;
    }

    private IEnumerable<StudioButton> All(IEnumerable<StudioButton>? items = null)
    {
        foreach (var item in items ?? profile.Buttons) { yield return item; foreach (var child in All(item.Children)) yield return child; }
    }
    private StudioButton? Find(string? id) => id is null ? null : All().FirstOrDefault(button => button.Id == id);
    private List<StudioButton> Siblings(string id) => profile.Buttons.Any(button => button.Id == id) ? profile.Buttons : All().First(button => button.Children.Any(child => child.Id == id)).Children;
    private string? ParentId(string id) => All().FirstOrDefault(button => button.Children.Any(child => child.Id == id))?.Id;
    private string PathLabel(StudioButton button) => ParentId(button.Id) is { } id && Find(id) is { } ancestor ? PathLabel(ancestor) + " / " + button.Label : button.Label;

    private void RebuildTree()
    {
        loading = true; tree.BeginUpdate(); tree.Nodes.Clear();
        void AddNodes(TreeNodeCollection nodes, IEnumerable<StudioButton> buttons)
        {
            foreach (var button in buttons)
            {
                var node = nodes.Add((button.IsGroup ? "▸ " : "") + button.Label); node.Tag = button;
                AddNodes(node.Nodes, button.Children);
                if (button.Id == selectedId) tree.SelectedNode = node;
            }
        }
        AddNodes(tree.Nodes, profile.Buttons); tree.EndUpdate(); loading = false;
    }

    private void RenderTiles()
    {
        transition.Stop(); tiles.SuspendLayout();
        foreach (Control tile in tiles.Controls.Cast<Control>().ToArray()) tile.Dispose();
        tiles.Controls.Clear();
        var current = Find(groupId);
        if (current is null) groupId = null;
        breadcrumb.Text = current is null ? profile.Name : current.Label;
        tips.SetToolTip(breadcrumb, current is null ? profile.Name : PathLabel(current));
        foreach (var button in current?.Children ?? profile.Buttons)
        {
            var tile = new StudioTile(button, Artwork(button), profile.TileSize, profile.Animate);
            var text = button.IsGroup ? "Open group" : string.Join(Environment.NewLine, StudioProfiles.StepsFor(button).Select(step => $"{step.Command}   [wait {step.DelayAfterMs} ms]"));
            tips.SetToolTip(tile, button.Label + Environment.NewLine + text + (button.ImportWarning.Length > 0 ? Environment.NewLine + button.ImportWarning : "") +
                (editMode ? "\nClick to select for editing; actions will not execute." : "\nRight-click to enter editor mode."));
            tile.Click += async (_, _) => await ActivateTile(button);
            tile.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) SelectForEditing(button); };
            tiles.Controls.Add(tile);
        }
        tiles.ResumeLayout();
    }

    private Bitmap? Artwork(StudioButton button)
    {
        if (!string.IsNullOrWhiteSpace(button.Artwork))
        {
            if (artworkCache.TryGetValue(button.Artwork, out var cached)) return cached;
            Bitmap? bitmap = null;
            try
            {
                // No URLs/UNC paths: importing a profile must not contact remote artwork servers.
                if (button.Artwork.Length < 3 || button.Artwork[1] != ':' || !Path.IsPathFullyQualified(button.Artwork)) throw new InvalidDataException();
                var bytes = StudioProfiles.ReadBounded(button.Artwork);
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream);
                if (image.Width > 2048 || image.Height > 2048) throw new InvalidDataException("Artwork exceeds 2048 pixels.");
                var fit = NewsBandrollControl.FitImageRectangle(image.Size, new Rectangle(0, 0, 96, 96));
                bitmap = new Bitmap(image, new Size(Math.Max(1, (int)Math.Round(fit.Width)), Math.Max(1, (int)Math.Round(fit.Height))));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException or OutOfMemoryException) { }
            artworkCache[button.Artwork] = bitmap;
            return bitmap;
        }
        return gameIcons.GetValueOrDefault(button.IconId);
    }

    private void Navigate(string? id)
    {
        groupId = id; RenderTiles();
        if (profile.Animate && SystemInformation.IsMenuAnimationEnabled) { transitionStart = DateTime.UtcNow; transition.Start(); }
    }
    private void Back() { if (groupId is not null) Navigate(ParentId(groupId)); }

    private void LoadEditor()
    {
        loading = true;
        var button = Find(selectedId); editor.Enabled = editMode && button is not null && sequence is null && !profileReadFailed;
        steps.Rows.Clear(); parent.Items.Clear(); parent.Items.Add(new ParentChoice(null, "Root"));
        if (button is not null)
        {
            var excluded = All([button]).Select(item => item.Id).ToHashSet();
            foreach (var candidate in All().Where(item => item.IsGroup && !excluded.Contains(item.Id))) parent.Items.Add(new ParentChoice(candidate.Id, PathLabel(candidate)));
            var parentId = ParentId(button.Id);
            parent.SelectedItem = parent.Items.Cast<ParentChoice>().FirstOrDefault(choice => choice.Id == parentId);
            label.Text = button.Label; group.Checked = button.IsGroup; iconId.Value = Math.Clamp(button.IconId, -999999, 999999);
            chosenArtwork = button.Artwork; artworkLabel.Text = chosenArtwork.Length > 0 ? Path.GetFileName(chosenArtwork) : "Game icon / no artwork";
            tips.SetToolTip(artworkLabel, chosenArtwork);
            chosenBackground = button.Background; chosenForeground = button.Foreground;
            foreach (var step in StudioProfiles.StepsFor(button)) steps.Rows.Add(step.Command, step.DelayAfterMs);
        }
        loading = false; editorDirty = false;
    }

    private bool ConfirmDiscard() => !editorDirty || MessageBox.Show(this, "Discard the unapplied button edits? Use Apply & save to keep them.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private bool TrySave()
    {
        if (profileReadFailed) { status.Text = "Profile load failed; saving is disabled to preserve it."; return false; }
        try { StudioProfiles.Save(profilePath, profile); return true; }
        catch (Exception ex) { status.Text = "Not saved: " + ex.Message; MessageBox.Show(this, status.Text, Text); return false; }
    }

    private bool Change(Action mutation)
    {
        if (!editMode || sequence is not null || profileReadFailed) return false;
        var original = JsonSerializer.Serialize(profile);
        try { mutation(); StudioProfiles.Validate(profile); if (!TrySave()) { profile = JsonSerializer.Deserialize<StudioProfile>(original)!; return false; } }
        catch (Exception ex) { profile = JsonSerializer.Deserialize<StudioProfile>(original)!; MessageBox.Show(this, ex.Message, Text); return false; }
        loading = true; tileSize.Value = profile.TileSize; animate.Checked = profile.Animate; loading = false;
        RebuildTree(); RenderTiles(); LoadEditor();
        return true;
    }

    private bool ApplyEditor()
    {
        var button = Find(selectedId); if (!editMode || button is null) return false;
        try
        {
            steps.EndEdit();
            var values = new List<StudioStep>();
            foreach (DataGridViewRow row in steps.Rows)
            {
                if (row.IsNewRow) continue;
                var command = row.Cells[0].Value?.ToString() ?? "";
                if (!int.TryParse(row.Cells[1].Value?.ToString(), out var wait)) throw new InvalidDataException("Enter a whole number for each wait (milliseconds).");
                values.Add(new StudioStep(command, wait));
            }
            if (!group.Checked) StudioSequence.Validate(values);
            if (!group.Checked && button.Children.Count > 0) throw new InvalidDataException("Move or delete this group's children before converting it to an action.");
            if (group.Checked && values.Count > 0 && MessageBox.Show(this, "Groups only navigate. Remove these command steps when saving as a group?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return false;
            if (button.ImportWarning.Length > 0 && MessageBox.Show(this, button.ImportWarning + "\nSave as this explicit action/group, without QoLBar special behavior?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return false;
            var newParent = (parent.SelectedItem as ParentChoice)?.Id;
            var oldParent = ParentId(button.Id);
            var newLabel = label.Text; var newGroup = group.Checked; var newIcon = (int)iconId.Value;
            var saved = Change(() =>
            {
                if (oldParent != newParent)
                {
                    Siblings(button.Id).Remove(button);
                    (Find(newParent)?.Children ?? profile.Buttons).Add(button);
                }
                button.Label = newLabel; button.IsGroup = newGroup; button.Steps = newGroup ? [] : values;
                button.Command = newGroup ? "" : string.Join("\n", values.Select(step => step.Command));
                button.IconId = newIcon; button.Artwork = chosenArtwork; button.Background = chosenBackground; button.Foreground = chosenForeground; button.ImportWarning = "";
            });
            if (saved) status.Text = "Button saved locally. QoLBar source is unchanged.";
            return saved;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); return false; }
    }

    private void Add(bool isGroup)
    {
        if (!ConfirmDiscard()) return;
        Change(() =>
        {
            var button = new StudioButton { Label = isGroup ? "New group" : "New action", IsGroup = isGroup };
            if (!isGroup) button.Steps.Add(new StudioStep("/wave", 180));
            (Find(groupId)?.Children ?? profile.Buttons).Add(button); selectedId = button.Id;
        });
    }
    private void Duplicate()
    {
        if (!ConfirmDiscard() || Find(selectedId) is not { } button) return;
        Change(() => { var copy = StudioProfiles.Duplicate(button); copy.Label += " (copy)"; var siblings = Siblings(button.Id); siblings.Insert(siblings.IndexOf(button) + 1, copy); selectedId = copy.Id; });
    }
    private void Delete()
    {
        if (Find(selectedId) is not { } button || MessageBox.Show(this, $"Delete {button.Label} and its children from the launcher profile? QoLBar stays unchanged.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Change(() => { Siblings(button.Id).Remove(button); selectedId = null; });
    }
    private void MoveButton(int direction)
    {
        if (!ConfirmDiscard() || Find(selectedId) is not { } button) return;
        Change(() => { var siblings = Siblings(button.Id); var index = siblings.IndexOf(button); var next = Math.Clamp(index + direction, 0, siblings.Count - 1); siblings.RemoveAt(index); siblings.Insert(next, button); });
    }
    private void MoveStep(int direction)
    {
        if (steps.CurrentRow is not { IsNewRow: false } row) return;
        var index = row.Index; var next = index + direction;
        if (next < 0 || next >= steps.Rows.Count || steps.Rows[next].IsNewRow) return;
        for (var cell = 0; cell < 2; cell++) (steps.Rows[index].Cells[cell].Value, steps.Rows[next].Cells[cell].Value) = (steps.Rows[next].Cells[cell].Value, steps.Rows[index].Cells[cell].Value);
        steps.CurrentCell = steps.Rows[next].Cells[0]; editorDirty = true;
    }
    private void ChooseColor(bool foreground)
    {
        using var dialog = new ColorDialog { Color = ColorTranslator.FromHtml(foreground ? chosenForeground : chosenBackground), FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        if (foreground) chosenForeground = color; else chosenBackground = color;
        editorDirty = true;
    }
    private void ChooseArtwork()
    {
        using var dialog = new OpenFileDialog { Filter = "Artwork|*.png;*.jpg;*.jpeg;*.bmp", Title = "Choose custom button artwork" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = StudioProfiles.ReadBounded(dialog.FileName);
            using var stream = new MemoryStream(bytes); using var image = Image.FromStream(stream);
            if (image.Width > 2048 || image.Height > 2048) throw new InvalidDataException("Use artwork up to 2048 × 2048 pixels.");
            var folder = Path.Combine(Path.GetDirectoryName(profilePath)!, "artwork"); Directory.CreateDirectory(folder);
            var destination = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(bytes)) + Path.GetExtension(dialog.FileName).ToLowerInvariant());
            if (!File.Exists(destination)) File.Copy(dialog.FileName, destination);
            chosenArtwork = destination; artworkLabel.Text = Path.GetFileName(dialog.FileName); editorDirty = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
    }

    private void Import()
    {
        if (!ConfirmDiscard() || sequence is not null || profileReadFailed) return;
        using var file = new OpenFileDialog { Filter = "QoLBar config|*.json", FileName = StudioProfiles.DefaultQoLBarPath };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var picker = new Form { Text = "Import a copy of a QoLBar bar", Size = new Size(400, 420), StartPosition = FormStartPosition.CenterParent };
            var list = new ListBox { Dock = DockStyle.Fill };
            list.Items.AddRange(StudioProfiles.BarNames(file.FileName).Cast<object>().ToArray()); list.SelectedItem = "Jobs (Multi)";
            var accept = new Button { Text = "Add as a new group", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
            picker.Controls.Add(list); picker.Controls.Add(accept); picker.AcceptButton = accept;
            if (picker.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not string name) return;
            var imported = StudioProfiles.Import(file.FileName, name);
            Change(() => { var button = new StudioButton { Label = name, IsGroup = true, Children = imported.Buttons }; profile.Buttons.Add(button); groupId = button.Id; selectedId = button.Id; });
            status.Text = "Imported a local copy, including nested groups. No source config was changed.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
    }

    private async Task RefreshClients()
    {
        if (refreshing || sequence is not null || IsDisposed) return;
        refreshing = true;
        try
        {
            var previousPid = (targets.SelectedItem as Target)?.Process.ProcessId;
            targets.Items.Clear();
            var clients = RunningClientAwareness.Capture().Where(process => process.StartTimeUtc is not null).ToArray();
            var results = await Task.WhenAll(clients.Select(async process =>
            {
                try { var reply = await BridgeClient.SendAsync(process.ProcessId, new(1, Guid.NewGuid(), "status", process.StartTimeUtc!.Value.Ticks, ""), lifetime.Token); return reply.Ok ? new Target(process, reply) : null; }
                catch (Exception) { return null; }
            }));
            if (IsDisposed || lifetime.IsCancellationRequested) return;
            targets.Items.AddRange(results.Where(result => result is not null).Cast<object>().ToArray());
            targets.SelectedItem = targets.Items.Cast<Target>().FirstOrDefault(target => target.Process.ProcessId == previousPid);
            // Never guess the originating client, even if exactly one is connected.
            status.Text = targets.Items.Count == 0 ? "No receivers found. Load PotatoCommandBridge as a developer plugin, /potatobridge on, then refresh." : "Select the origin client explicitly. Arm it with /potatobridge on if OFF.";
            if (targets.Items.Count > 0) await FetchGameIcons((Target)targets.Items[0]!);
        }
        finally { refreshing = false; }
    }

    private async Task FetchGameIcons(Target target)
    {
        var ids = All().Where(button => button.Artwork.Length == 0 && button.IconId > 0 && !gameIcons.ContainsKey(button.IconId)).Select(button => button.IconId).Distinct().Take(128).ToArray();
        foreach (var id in ids)
        {
            try
            {
                var reply = await BridgeClient.SendAsync(target.Process.ProcessId, new(1, Guid.NewGuid(), "icon", target.Process.StartTimeUtc!.Value.Ticks, "", IconId: id), lifetime.Token);
                if (IsDisposed || lifetime.IsCancellationRequested) return;
                if (!reply.Ok || reply.Width is < 1 or > 256 || reply.Height is < 1 or > 256 || reply.Pixels?.Length != reply.Width * reply.Height * 4) continue;
                var bitmap = new Bitmap(reply.Width, reply.Height, PixelFormat.Format32bppArgb);
                var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { for (var row = 0; row < reply.Height; row++) Marshal.Copy(reply.Pixels, row * reply.Width * 4, locked.Scan0 + row * locked.Stride, reply.Width * 4); }
                finally { bitmap.UnlockBits(locked); }
                gameIcons[id] = bitmap;
            }
            catch (Exception) { break; }
        }
        if (!IsDisposed) RenderTiles();
    }

    private async Task Execute(StudioButton button)
    {
        if (editMode || sequence is not null || button.IsGroup) return;
        if (editorDirty) { status.Text = "Apply or discard the editor changes before running a saved action."; return; }
        if (button.ImportWarning.Length > 0) { MessageBox.Show(this, button.ImportWarning, Text); return; }
        if (targets.SelectedItem is not Target target || !target.State.Armed) { status.Text = "Select one armed origin client first. Use /potatobridge on in-game, then Refresh clients."; return; }
        try { StudioSequence.Validate(StudioProfiles.StepsFor(button)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); return; }
        sequence = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        editor.Enabled = false; targets.Enabled = false; stopButton.Enabled = true;
        var accepted = 0;
        try
        {
            await StudioSequence.RunAsync(StudioProfiles.StepsFor(button), async (command, token) =>
            {
                var reply = await BridgeClient.SendAsync(target.Process.ProcessId, new(1, Guid.NewGuid(), "execute", target.Process.StartTimeUtc!.Value.Ticks, target.State.Session, command), token);
                if (!reply.Ok) throw new InvalidOperationException(reply.Message);
                accepted++;
            }, (delay, token) => Task.Delay(delay, token), (step, count) => status.Text = $"{button.Label} → {target.State.Character}: step {step}/{count}. Stop cancels remaining steps.", sequence.Token);
            status.Text = $"Submitted {accepted} step(s) to {target.State.Character}. Check the game for the result.";
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = $"Stopped after {accepted} acknowledged step(s). A step already submitted cannot be undone; nothing will retry."; }
        catch (Exception ex) { if (!IsDisposed) status.Text = $"Sequence stopped: {ex.Message} Delivery may be uncertain; no steps were retried."; }
        finally
        {
            sequence.Dispose(); sequence = null;
            if (!IsDisposed) { targets.Enabled = true; stopButton.Enabled = false; LoadEditor(); }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel(); transition.Dispose(); tips.Dispose();
            foreach (var bitmap in gameIcons.Values) bitmap.Dispose();
            foreach (var bitmap in artworkCache.Values) bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class StudioTile : Button
{
    private readonly StudioButton definition;
    private readonly Image? artwork;
    private readonly bool animate;
    private readonly System.Windows.Forms.Timer hoverTimer = new() { Interval = 16 };
    private float glow;
    private bool hover;
    private bool pressed;
    public StudioTile(StudioButton definition, Image? artwork, int size, bool animate)
    {
        this.definition = definition; this.artwork = artwork; this.animate = animate;
        Text = definition.Label; AccessibleName = definition.Label + (definition.IsGroup ? " group" : " action");
        Size = new Size(size + 16, size + 34); Margin = new Padding(5); Cursor = Cursors.Hand;
        DoubleBuffered = true; FlatStyle = FlatStyle.Flat;
        hoverTimer.Tick += (_, _) => { glow = Math.Clamp(glow + (hover ? .16F : -.16F), 0, 1); Invalidate(); if (glow is 0 or 1) hoverTimer.Stop(); };
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; if (animate) hoverTimer.Start(); else { glow = 1; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; if (animate) hoverTimer.Start(); else { glow = 0; Invalidate(); } }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);
        var rect = new Rectangle(2, pressed ? 4 : 2, Width - 5, Height - 6);
        using var path = new GraphicsPath(); const int radius = 16;
        path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90); path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90); path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90); path.CloseFigure();
        var background = ColorTranslator.FromHtml(definition.Background);
        using var fill = new LinearGradientBrush(rect, ControlPaint.Light(background, .15F + glow * .2F), background, 90F); g.FillPath(fill, path);
        using var border = new Pen(Color.FromArgb(100 + (int)(glow * 155), 206, 170, 250), Focused ? 2 : 1 + glow); g.DrawPath(border, path);
        var iconArea = new Rectangle(18, 12 + (pressed ? 2 : 0), Width - 36, Height - 54);
        if (artwork is not null)
        {
            var fitted = NewsBandrollControl.FitImageRectangle(artwork.Size, iconArea); g.DrawImage(artwork, fitted);
        }
        else TextRenderer.DrawText(g, definition.IsGroup ? "▸" : definition.IconId != 0 ? $"Icon\n{definition.IconId}" : "/", Font, iconArea, ColorTranslator.FromHtml(definition.Foreground), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, definition.Label, Font, new Rectangle(6, Height - 36, Width - 12, 30), ColorTranslator.FromHtml(definition.Foreground), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        if (definition.IsGroup) TextRenderer.DrawText(g, "›", Font, new Point(Width - 18, 8), Color.Gold);
    }
    protected override void Dispose(bool disposing) { if (disposing) hoverTimer.Dispose(); base.Dispose(disposing); }
}
