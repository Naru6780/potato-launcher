using System.Drawing;

namespace PotatoLauncher;

internal sealed record Dlss5AccountChoice(string AccountKey, string Label)
{
    public override string ToString() => Label;
}

// Settings → DLSS 5 clients: tick the clients that should start with DLSS 5.
internal sealed class Dlss5ClientsForm : Form
{
    private readonly Dlss5Config config;
    private readonly string sharedProfileFolder;
    private readonly CheckedListBox clientList;
    private readonly Label gameFolderLabel;
    private readonly Label statusLabel;
    private readonly CheckBox reShadeOffInput;

    public Dlss5ClientsForm(IReadOnlyList<Dlss5AccountChoice> choices, Dlss5Config config, string sharedProfileFolder)
    {
        this.config = config;
        this.sharedProfileFolder = sharedProfileFolder;
        Text = "DLSS 5 clients";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 572);

        var intro = new Label
        {
            Text = "Tick the clients that start with DLSS 5. Changes apply the next time a client is launched.",
            Bounds = new Rectangle(14, 12, 432, 54)
        };
        gameFolderLabel = new Label { Bounds = new Rectangle(14, 70, 340, 36), AutoEllipsis = true };
        var changeFolder = new Button { Text = "Change...", Bounds = new Rectangle(360, 72, 86, 28) };
        changeFolder.Click += (_, _) => ChooseGameFolder();
        statusLabel = new Label { Bounds = new Rectangle(14, 108, 432, 36), AutoEllipsis = true };

        clientList = new CheckedListBox { Bounds = new Rectangle(14, 148, 432, 316), CheckOnClick = true, IntegralHeight = false };
        foreach (var choice in choices)
        {
            clientList.Items.Add(choice, Dlss5Clients.IsEnabled(config, choice.AccountKey));
        }

        var selectAll = new Button { Text = "Select all", Bounds = new Rectangle(14, 474, 90, 30) };
        selectAll.Click += (_, _) => SetAll(true);
        var clear = new Button { Text = "Clear", Bounds = new Rectangle(110, 474, 90, 30) };
        clear.Click += (_, _) => SetAll(false);
        reShadeOffInput = new CheckBox
        {
            Text = "Turn ReShade off completely on the other clients",
            Checked = config.ReShadeOffForOtherClients,
            Bounds = new Rectangle(14, 507, 432, 22)
        };
        var reShadeOffHint = new ToolTip();
        reShadeOffHint.SetToolTip(reShadeOffInput, "On: clients without DLSS 5 start with no ReShade at all (no banner, overlay or effects).\nOff: they run ReShade without add-ons.");
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Bounds = new Rectangle(266, 532, 86, 32) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(360, 532, 86, 32) };

        Controls.AddRange([intro, gameFolderLabel, changeFolder, statusLabel, clientList, selectAll, clear, reShadeOffInput, save, cancel]);
        AcceptButton = save;
        CancelButton = cancel;
        RefreshStatus();
    }

    public bool ReShadeOffForOtherClients => reShadeOffInput.Checked;

    public List<string> SelectedAccountKeys() =>
        clientList.CheckedItems.OfType<Dlss5AccountChoice>().Select(choice => choice.AccountKey).ToList();

    private void SetAll(bool value)
    {
        for (var index = 0; index < clientList.Items.Count; index++) clientList.SetItemChecked(index, value);
    }

    private void ChooseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the FFXIV \"game\" folder (the one containing ffxiv_dx11.exe)",
            UseDescriptionForTitle = true,
            SelectedPath = Dlss5Clients.FindGameFolder(config, sharedProfileFolder)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!Dlss5Clients.IsGameFolder(dialog.SelectedPath))
        {
            MessageBox.Show(this, "That folder does not contain ffxiv_dx11.exe.", "Not a game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        config.GameFolderOverride = dialog.SelectedPath;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var gameFolder = Dlss5Clients.FindGameFolder(config, sharedProfileFolder);
        var status = Dlss5Clients.Inspect(gameFolder);
        gameFolderLabel.Text = string.IsNullOrWhiteSpace(gameFolder) ? "Game folder: not found" : $"Game folder: {gameFolder}";
        statusLabel.Text = status.Describe();
        statusLabel.ForeColor = status.Available ? Color.SeaGreen : Color.Firebrick;
    }
}
