using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfGrid = System.Windows.Controls.Grid;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfImage = System.Windows.Controls.Image;
using WpfMediaElement = System.Windows.Controls.MediaElement;
using WpfMediaPlayer = System.Windows.Media.MediaPlayer;
using WpfStretch = System.Windows.Media.Stretch;
using WpfThickness = System.Windows.Thickness;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace PotatoLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed record Account(string Name, string BatchFile, int SortOrder, string AccountKey, bool UseSteamServiceAccount = false, bool UseOtp = false)
{
    public override string ToString() => Name;
}

internal sealed class BandConfig
{
    public string Name { get; set; } = "New Band";
    public List<string> BatchFiles { get; set; } = [];
    public override string ToString() => $"{Name} ({BatchFiles.Count})";
}

internal sealed class AppSettings
{
    public int DelaySeconds { get; set; } = 15;
    public string DalamudFolder { get; set; } = "";
    public string LaunchMode { get; set; } = "Instanced";
    public string SharedProfileFolder { get; set; } = "";
    public bool LaunchModeChosen { get; set; }
    public string Theme { get; set; } = "Pink";
    public bool MusicMuted { get; set; }
    public List<BandConfig> Bands { get; set; } = [];
    public List<BandConfig> InstancedBands { get; set; } = [];
    public List<BandConfig> SharedBands { get; set; } = [];

}

internal sealed record ThemePalette(Color Back1, Color Back2, Color Card, Color Border, Color Text, Color Muted, Color Primary, Color Secondary, Color Danger, Color ListBack);
internal readonly record struct LauncherWindow(int ProcessId, IntPtr Handle);
internal readonly record struct LaunchCommand(string FileName, string Arguments, string WorkingDirectory);
internal readonly record struct BatchLaunchInfo(string AccountKey, string RoamingPath);
internal sealed record NewsBanner(string ImageUrl, string LinkUrl);
internal sealed record NewsEntry(string Title, string Url, DateTimeOffset Date, string Tag);

internal sealed class MainForm : Form
{
    private const string GitHubOwner = "Naru6780";
    private const string GitHubRepo = "potato-launcher";
    private const string ReleaseZipName = "PotatoLauncher.zip";

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static readonly Dictionary<string, ThemePalette> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pink"] = new(Color.FromArgb(255,226,242), Color.FromArgb(210,236,255), Color.FromArgb(250,255,250,255), Color.FromArgb(238,182,226), Color.FromArgb(92,48,104), Color.FromArgb(137,82,139), Color.FromArgb(239,111,166), Color.FromArgb(124,150,224), Color.FromArgb(222,104,130), Color.FromArgb(255,250,255)),
        ["Fuchsia"] = new(Color.FromArgb(74,20,86), Color.FromArgb(255,97,187), Color.FromArgb(242,255,241,252), Color.FromArgb(245,160,230), Color.FromArgb(84,27,96), Color.FromArgb(139,63,145), Color.FromArgb(217,42,154), Color.FromArgb(139,88,232), Color.FromArgb(224,76,112), Color.FromArgb(255,247,253)),
        ["Moogle"] = new(Color.FromArgb(255,229,246), Color.FromArgb(226,216,255), Color.FromArgb(248,255,250,255), Color.FromArgb(230,177,234), Color.FromArgb(92,50,111), Color.FromArgb(139,83,151), Color.FromArgb(237,92,164), Color.FromArgb(157,111,223), Color.FromArgb(222,91,128), Color.FromArgb(255,249,255)),
        ["Dark"] = new(Color.FromArgb(18,20,34), Color.FromArgb(45,33,69), Color.FromArgb(238,30,35,54), Color.FromArgb(78,65,105), Color.FromArgb(239,233,255), Color.FromArgb(188,177,214), Color.FromArgb(199,84,154), Color.FromArgb(92,120,220), Color.FromArgb(222,88,112), Color.FromArgb(40,38,55)),
        ["Sky"] = new(Color.FromArgb(216,241,255), Color.FromArgb(246,232,255), Color.FromArgb(245,255,255,255), Color.FromArgb(168,204,234), Color.FromArgb(45,77,112), Color.FromArgb(89,111,145), Color.FromArgb(70,151,229), Color.FromArgb(164,106,222), Color.FromArgb(221,94,122), Color.FromArgb(250,253,255)),
        ["Chocobo"] = new(Color.FromArgb(255,241,177), Color.FromArgb(255,220,114), Color.FromArgb(252,255,249,226), Color.FromArgb(225,178,67), Color.FromArgb(96,68,28), Color.FromArgb(139,98,40), Color.FromArgb(219,180,87), Color.FromArgb(121,155,80), Color.FromArgb(198,91,62), Color.FromArgb(255,252,235)),
        ["Limsa Lominsa"] = new(Color.FromArgb(190,230,238), Color.FromArgb(25,88,118), Color.FromArgb(246,249,252,255), Color.FromArgb(119,178,193), Color.FromArgb(21,64,86), Color.FromArgb(72,112,130), Color.FromArgb(39,139,176), Color.FromArgb(226,236,235), Color.FromArgb(207,92,83), Color.FromArgb(248,253,255)),
        ["Gridania"] = new(Color.FromArgb(204,226,181), Color.FromArgb(71,111,72), Color.FromArgb(246,251,246,236), Color.FromArgb(139,174,113), Color.FromArgb(46,75,39), Color.FromArgb(92,116,70), Color.FromArgb(91,143,74), Color.FromArgb(159,119,75), Color.FromArgb(192,92,64), Color.FromArgb(250,255,244)),
        ["Ul'dah"] = new(Color.FromArgb(238,202,143), Color.FromArgb(143,88,49), Color.FromArgb(248,255,246,232), Color.FromArgb(196,142,75), Color.FromArgb(86,54,32), Color.FromArgb(130,86,52), Color.FromArgb(199,134,54), Color.FromArgb(124,92,142), Color.FromArgb(183,72,61), Color.FromArgb(255,250,235)),
        ["Ishgard"] = new(Color.FromArgb(217,226,235), Color.FromArgb(78,96,125), Color.FromArgb(244,248,251,255), Color.FromArgb(151,166,190), Color.FromArgb(45,55,76), Color.FromArgb(91,101,126), Color.FromArgb(82,111,164), Color.FromArgb(168,177,194), Color.FromArgb(174,76,91), Color.FromArgb(248,251,255)),
        ["A Realm Reborn"] = new(Color.FromArgb(38,68,92), Color.FromArgb(151,114,58), Color.FromArgb(246,250,246,236), Color.FromArgb(182,146,86), Color.FromArgb(49,54,65), Color.FromArgb(91,87,89), Color.FromArgb(186,126,62), Color.FromArgb(73,123,156), Color.FromArgb(185,75,70), Color.FromArgb(252,249,241)),
        ["Heavensward"] = new(Color.FromArgb(202,216,229), Color.FromArgb(57,77,115), Color.FromArgb(246,247,250,255), Color.FromArgb(142,160,190), Color.FromArgb(38,48,73), Color.FromArgb(85,97,124), Color.FromArgb(78,114,175), Color.FromArgb(180,184,194), Color.FromArgb(171,75,91), Color.FromArgb(248,251,255)),
        ["Stormblood"] = new(Color.FromArgb(94,28,34), Color.FromArgb(218,132,62), Color.FromArgb(246,255,246,235), Color.FromArgb(190,91,72), Color.FromArgb(84,36,37), Color.FromArgb(134,72,59), Color.FromArgb(197,64,58), Color.FromArgb(218,147,62), Color.FromArgb(151,50,57), Color.FromArgb(255,249,241)),
        ["Shadowbringers"] = new(Color.FromArgb(29,22,45), Color.FromArgb(92,63,119), Color.FromArgb(240,31,28,47), Color.FromArgb(114,89,143), Color.FromArgb(244,237,255), Color.FromArgb(199,183,220), Color.FromArgb(154,94,211), Color.FromArgb(223,190,112), Color.FromArgb(218,88,111), Color.FromArgb(43,37,56)),
        ["Endwalker"] = new(Color.FromArgb(23,29,55), Color.FromArgb(115,118,142), Color.FromArgb(242,246,247,255), Color.FromArgb(151,158,186), Color.FromArgb(38,43,66), Color.FromArgb(91,97,122), Color.FromArgb(75,105,196), Color.FromArgb(194,185,174), Color.FromArgb(188,76,92), Color.FromArgb(248,250,255)),
        ["Dawntrail"] = new(Color.FromArgb(253,201,99), Color.FromArgb(43,153,178), Color.FromArgb(246,255,250,232), Color.FromArgb(232,168,83), Color.FromArgb(70,57,39), Color.FromArgb(111,91,60), Color.FromArgb(226,139,49), Color.FromArgb(58,164,181), Color.FromArgb(199,82,69), Color.FromArgb(255,252,240))
    };

    private readonly AppSettings settings = LoadSettings();
    private readonly List<Account> accounts = [];
    private CuteBackgroundPanel background = null!;
    private RoundedPanel accountCard = null!;
    private RoundedPanel bandCard = null!;
    private RoundedPanel settingsDrawer = null!;
    private readonly System.Windows.Forms.Timer settingsDrawerTimer = new();
    private ListBox accountList = null!;
    private ListBox bandList = null!;
    private CheckedListBox memberList = null!;
    private TextBox bandName = null!;
    private Label folderLabel = null!;
    private Label sharedProfileLabel = null!;
    private Label cooldownLabel = null!;
    private Label secondsLabel = null!;
    private Label themeLabel = null!;
    private TextBox folderInput = null!;
    private TextBox sharedProfileInput = null!;
    private NumericUpDown delayInput = null!;
    private ComboBox launchModeInput = null!;
    private ComboBox themeInput = null!;
    private Button browseBatButton = null!;
    private Button browseSharedProfileButton = null!;
    private Button updateButton = null!;
    private CheckBox muteMusicInput = null!;
    private Button settingsButton = null!;
    private Button killGameButton = null!;
    private Button whatsNewButton = null!;
    private MascotOverlayForm? mascotOverlay;
    private RoundedPanel statusPill = null!;
    private Label status = null!;
    private Button launchBandButton = null!;
    private Button cancelButton = null!;
    private CuteBackgroundPanel loadingOverlay = null!;
    private CuteBackgroundPanel launchChoiceOverlay = null!;
    private PictureBox loadingPicture = null!;
    private Label loadingTitle = null!;
    private Label loadingStatus = null!;
    private Button loadingCancel = null!;
    private RoundedPanel newsOverlay = null!;
    private PictureBox newsBannerPicture = null!;
    private Label newsBannerTitle = null!;
    private NewsDotsControl newsDots = null!;
    private FlowLayoutPanel newsListPanel = null!;
    private Button newsCloseButton = null!;
    private ElementHost videoHost = null!;
    private WpfMediaElement backgroundVideo = null!;
    private readonly WpfMediaPlayer themeMusic = new();
    private WpfImage wpfMascot = null!;
    private readonly DispatcherTimer mascotTimer = new();
    private readonly List<BitmapSource> mascotFrames = [];
    private readonly List<int> mascotFrameDelays = [];
    private ThemePalette palette = Palettes["Pink"];
    private CancellationTokenSource? queueCancel;
    private bool loadingBand;
    private bool themeHasVideo;
    private bool themeHasImage;
    private bool settingsDrawerOpen;
    private string? currentMusicPath;
    private readonly List<NewsBanner> newsBanners = [];
    private readonly List<NewsEntry> newsEntries = [];
    private int selectedNewsBannerIndex;

    public MainForm()
    {
        EnsureThemeAssetFolders();
        RemoveOldDefaultBands();
        LoadAccounts();
        BuildUi();
        MigrateLegacyBands();
        PopulateLists();
        ApplyTheme(settings.Theme);
        if (!settings.LaunchModeChosen) Shown += (_, _) => ShowLaunchChoiceOverlay();
    }

    private void BuildUi()
    {
        Text = "Potato Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(990, 700);
        Font = new Font("Segoe UI", 10F);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        background = new CuteBackgroundPanel { Dock = DockStyle.Fill };
        Controls.Add(background);
        BuildVideoBackground();
        themeMusic.MediaEnded += (_, _) =>
        {
            if (settings.MusicMuted || string.IsNullOrWhiteSpace(currentMusicPath)) return;
            themeMusic.Position = TimeSpan.Zero;
            themeMusic.Play();
        };

        settingsButton = Button("Settings", 36, 24, 102, 34, "Secondary");
        settingsButton.Click += (_, _) => ToggleSettingsDrawer();
        background.Controls.Add(settingsButton);
        killGameButton = Button("Kill FFXIV", 154, 24, 104, 34, "Danger");
        killGameButton.Click += (_, _) => KillGameInstances();
        background.Controls.Add(killGameButton);
        whatsNewButton = Button("What's new?", 272, 24, 122, 34, "Secondary");
        whatsNewButton.Click += async (_, _) => await ShowNewsOverlayAsync();
        background.Controls.Add(whatsNewButton);
        mascotOverlay = CreateMascotOverlay();
        Shown += (_, _) => UpdateMascotOverlay();
        Move += (_, _) => UpdateMascotOverlay();
        Resize += (_, _) => UpdateMascotOverlay();
        Activated += (_, _) => UpdateMascotOverlay();
        FormClosed += (_, _) =>
        {
            themeMusic.Stop();
            themeMusic.Close();
            mascotOverlay?.Close();
        };
        BuildLauncherTab(background);
        BuildSettingsDrawer();

        statusPill = new RoundedPanel { Bounds = new Rectangle(310, 616, 370, 30), Radius = 15 };
        statusPill.Click += (_, _) => BrowseFolderFromStatus();
        status = new Label { Text = "Ready.", TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Bounds = new Rectangle(10, 4, 350, 20), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        status.Click += (_, _) => BrowseFolderFromStatus();
        statusPill.Controls.Add(status);
        background.Controls.Add(statusPill);
        BuildLoadingOverlay();
        BuildLaunchChoiceOverlay();
        BuildNewsOverlay();
        settingsDrawer.BringToFront();
        ConfigureSettingsDrawerAnimation();
    }

    private void BuildVideoBackground()
    {
        var videoLayer = new WpfGrid { Background = System.Windows.Media.Brushes.Transparent };
        backgroundVideo = new WpfMediaElement
        {
            LoadedBehavior = System.Windows.Controls.MediaState.Manual,
            UnloadedBehavior = System.Windows.Controls.MediaState.Manual,
            Stretch = WpfStretch.UniformToFill,
            IsMuted = true,
            Volume = 0
        };
        videoLayer.Children.Add(backgroundVideo);
        backgroundVideo.MediaEnded += (_, _) =>
        {
            backgroundVideo.Position = TimeSpan.Zero;
            backgroundVideo.Play();
        };

        wpfMascot = new WpfImage
        {
            Width = 126,
            Height = 118,
            Stretch = WpfStretch.Uniform,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = WpfVerticalAlignment.Top,
            Margin = new WpfThickness(0, 16, 32, 0),
            IsHitTestVisible = false
        };
        videoLayer.Children.Add(wpfMascot);
        LoadMascotFrames();
        mascotTimer.Tick += (_, _) => AdvanceMascotFrame();

        videoHost = new ElementHost
        {
            Dock = DockStyle.Fill,
            Child = videoLayer,
            Visible = false
        };
        background.Controls.Add(videoHost);
        videoHost.SendToBack();
    }

    private void ConfigureSettingsDrawerAnimation()
    {
        settingsDrawerTimer.Interval = 15;
        settingsDrawerTimer.Tick += (_, _) =>
        {
            var target = settingsDrawerOpen ? ClientSize.Width - settingsDrawer.Width : ClientSize.Width + 4;
            var distance = target - settingsDrawer.Left;
            if (Math.Abs(distance) <= 2)
            {
                settingsDrawer.Left = target;
                settingsDrawerTimer.Stop();
                return;
            }
            settingsDrawer.Left += Math.Sign(distance) * Math.Max(2, Math.Abs(distance) / 4);
        };
    }

    private void ToggleSettingsDrawer()
    {
        if (settingsDrawerOpen) CloseSettingsDrawer();
        else OpenSettingsDrawer();
    }

    private void OpenSettingsDrawer()
    {
        settingsDrawerOpen = true;
        UpdateMascotOverlay();
        settingsDrawer.BringToFront();
        settingsDrawerTimer.Start();
    }

    private void CloseSettingsDrawer()
    {
        settingsDrawerOpen = false;
        UpdateMascotOverlay();
        settingsDrawerTimer.Start();
    }

    private MascotOverlayForm? CreateMascotOverlay()
    {
        var mascotPath = MascotGifPath();
        if (!File.Exists(mascotPath)) return null;
        try
        {
            return new MascotOverlayForm(mascotPath, new Size(126, 118));
        }
        catch
        {
            return null;
        }
    }

    private void UpdateMascotOverlay()
    {
        if (mascotOverlay is null) return;
        if (!IsHandleCreated || WindowState == FormWindowState.Minimized || settingsDrawerOpen || launchChoiceOverlay is { Visible: true })
        {
            mascotOverlay.Hide();
            return;
        }

        var mascotLocation = PointToScreen(new Point(ClientSize.Width - 158, 16));
        mascotOverlay.Bounds = new Rectangle(mascotLocation, mascotOverlay.Size);
        if (!mascotOverlay.Visible) mascotOverlay.Show(this);
        mascotOverlay.Invalidate();
    }

    private void LoadMascotFrames()
    {
        var mascotPath = MascotGifPath();
        if (!File.Exists(mascotPath)) return;

        using var image = Image.FromFile(mascotPath);
        var dimension = new System.Drawing.Imaging.FrameDimension(image.FrameDimensionsList[0]);
        var frameCount = image.GetFrameCount(dimension);
        var delays = ReadGifFrameDelays(image, frameCount);
        for (var index = 0; index < frameCount; index++)
        {
            image.SelectActiveFrame(dimension, index);
            using var frame = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(frame))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(image, 0, 0, image.Width, image.Height);
            }
            mascotFrames.Add(ToBitmapSource(frame));
            mascotFrameDelays.Add(delays[index]);
        }

        if (mascotFrames.Count > 0)
        {
            wpfMascot.Source = mascotFrames[0];
            mascotTimer.Interval = TimeSpan.FromMilliseconds(mascotFrameDelays[0]);
            mascotTimer.Start();
        }
    }

    private static List<int> ReadGifFrameDelays(Image image, int frameCount)
    {
        var delays = Enumerable.Repeat(90, frameCount).ToList();
        try
        {
            var value = image.GetPropertyItem(0x5100)?.Value;
            if (value is null) return delays;
            for (var index = 0; index < frameCount; index++)
            {
                if (index * 4 + 4 > value.Length) break;
                var raw = BitConverter.ToInt32(value, index * 4);
                delays[index] = Math.Clamp(raw * 10, 40, 500);
            }
        }
        catch
        {
        }
        return delays;
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap(Color.Transparent);
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    private void AdvanceMascotFrame()
    {
        if (mascotFrames.Count == 0) return;
        var next = (mascotFrames.IndexOf((BitmapSource)wpfMascot.Source) + 1) % mascotFrames.Count;
        wpfMascot.Source = mascotFrames[next];
        mascotTimer.Interval = TimeSpan.FromMilliseconds(mascotFrameDelays[next]);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && newsOverlay is { Visible: true })
        {
            HideNewsOverlay();
            return true;
        }
        if (keyData == Keys.Escape && settingsDrawerOpen)
        {
            CloseSettingsDrawer();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildLauncherTab(Control tab)
    {
        accountCard = Card(42, 118, 330, 450);
        accountCard.Controls.Add(Header("Accounts", 18, 12, 180, 32));
        accountList = new ListBox { Bounds = new Rectangle(18, 58, 294, 320) };
        accountList.DoubleClick += (_, _) => LaunchSelectedAccount();
        accountCard.Controls.Add(accountList);
        var launchAccount = Button("Launch selected", 18, 394, 150, 36, "Primary");
        launchAccount.Click += (_, _) => LaunchSelectedAccount();
        accountCard.Controls.Add(launchAccount);

        bandCard = Card(392, 118, 560, 450);
        bandCard.Controls.Add(Header("Band Manager", 18, 12, 180, 32));
        bandList = new ListBox { Bounds = new Rectangle(18, 58, 180, 306) };
        bandList.SelectedIndexChanged += (_, _) => LoadSelectedBand();
        bandCard.Controls.Add(bandList);
        bandName = new TextBox { Bounds = new Rectangle(218, 58, 250, 29), PlaceholderText = "Band name" };
        bandName.Leave += (_, _) => SaveCurrentBand();
        bandName.TextChanged += (_, _) => { if (!loadingBand) SaveCurrentBand(false); };
        bandCard.Controls.Add(bandName);
        memberList = new CheckedListBox { Bounds = new Rectangle(218, 98, 318, 266), CheckOnClick = true };
        memberList.ItemCheck += (_, _) => { if (!loadingBand) BeginInvoke(() => SaveCurrentBand()); };
        bandCard.Controls.Add(memberList);

        var newBand = Button("New band", 18, 384, 104, 36, "Secondary");
        newBand.Click += (_, _) => AddBand();
        var deleteBand = Button("Delete", 132, 384, 76, 36, "Danger");
        deleteBand.Click += (_, _) => DeleteBand();
        launchBandButton = Button("Launch band", 218, 384, 136, 36, "Primary");
        launchBandButton.Click += async (_, _) => await LaunchSelectedBandAsync();
        cancelButton = Button("Cancel", 366, 384, 74, 36, "Danger");
        cancelButton.Visible = false;
        cancelButton.Click += (_, _) => queueCancel?.Cancel();
        bandCard.Controls.AddRange([newBand, deleteBand, launchBandButton, cancelButton]);
        tab.Controls.Add(accountCard);
        tab.Controls.Add(bandCard);
    }

    private void ApplyLauncherLayout()
    {
        if (accountCard is null || bandCard is null || statusPill is null) return;

        accountCard.Bounds = new Rectangle(42, 118, 330, 450);
        bandCard.Bounds = new Rectangle(392, 118, 560, 450);
        statusPill.Bounds = new Rectangle(310, 616, 370, 30);

        accountCard.Invalidate();
        bandCard.Invalidate();
        statusPill.Invalidate();
    }

    private void BuildSettingsDrawer()
    {
        settingsDrawer = new RoundedPanel { Bounds = new Rectangle(ClientSize.Width + 4, 0, 380, ClientSize.Height), Radius = 24 };
        settingsDrawer.Controls.Add(Header("Settings", 24, 24, 180, 38));

        settingsDrawer.Controls.Add(Label("Launch method", 24, 76, 170, 24));
        launchModeInput = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(24, 104, 220, 29) };
        launchModeInput.Items.AddRange(["Instanced", "Shared"]);
        launchModeInput.SelectedItem = IsSharedLaunchMode() ? "Shared" : "Instanced";
        launchModeInput.SelectedIndexChanged += (_, _) =>
        {
            SaveSettingsFromInputs();
            UpdateLaunchModeUi();
            LoadAccounts();
            PopulateLists();
        };
        settingsDrawer.Controls.Add(launchModeInput);

        folderLabel = Label("Instanced folder", 24, 146, 220, 24);
        folderInput = new TextBox { Text = settings.DalamudFolder, Bounds = new Rectangle(24, 174, 332, 29) };
        folderInput.Leave += (_, _) => SaveAndRescan();
        browseBatButton = Button("Browse", 24, 212, 96, 32, "Secondary");
        browseBatButton.Click += (_, _) => BrowseFolder(folderInput, "Choose the folder containing XIVLauncher .bat files", SaveAndRescan);
        settingsDrawer.Controls.AddRange([folderLabel, folderInput, browseBatButton]);

        sharedProfileLabel = Label("Shared folder", 24, 146, 260, 24);
        sharedProfileInput = new TextBox { Text = settings.SharedProfileFolder, Bounds = new Rectangle(24, 174, 332, 29) };
        sharedProfileInput.Leave += (_, _) => { SaveSettingsFromInputs(); SaveAndRescan(); };
        browseSharedProfileButton = Button("Browse", 24, 212, 96, 32, "Secondary");
        browseSharedProfileButton.Click += (_, _) => BrowseFolder(sharedProfileInput, "Choose the folder containing accountsList.json", SaveAndRescan);
        settingsDrawer.Controls.AddRange([sharedProfileLabel, sharedProfileInput, browseSharedProfileButton]);

        cooldownLabel = Label("Cooldown between launches", 24, 420, 220, 24);
        delayInput = new NumericUpDown { Minimum = 1, Maximum = 300, Value = Math.Clamp(settings.DelaySeconds, 1, 300), Bounds = new Rectangle(246, 418, 72, 29) };
        delayInput.ValueChanged += (_, _) => SaveSettingsFromInputs();
        secondsLabel = Label("sec", 326, 421, 42, 24);
        settingsDrawer.Controls.AddRange([cooldownLabel, delayInput, secondsLabel]);

        themeLabel = Label("Theme", 24, 580, 120, 24);
        themeInput = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(24, 608, 260, 29) };
        themeInput.Items.AddRange(Palettes.Keys.ToArray());
        themeInput.SelectedItem = Palettes.ContainsKey(NormalizeThemeName(settings.Theme)) ? NormalizeThemeName(settings.Theme) : "Pink";
        themeInput.SelectedIndexChanged += (_, _) => { SaveSettingsFromInputs(); ApplyTheme(settings.Theme); };
        settingsDrawer.Controls.AddRange([themeLabel, themeInput]);

        muteMusicInput = new CheckBox { Text = "Mute theme music", Checked = settings.MusicMuted, Bounds = new Rectangle(24, 420, 220, 28), BackColor = Color.Transparent };
        muteMusicInput.CheckedChanged += (_, _) =>
        {
            SaveSettingsFromInputs();
            ApplyThemeMusic(settings.Theme);
        };
        settingsDrawer.Controls.Add(muteMusicInput);

        updateButton = Button("Check for updates", 24, 464, 180, 34, "Secondary");
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        settingsDrawer.Controls.Add(updateButton);
        UpdateLaunchModeUi();
        background.Controls.Add(settingsDrawer);
    }

    private void UpdateLaunchModeUi()
    {
        if (launchModeInput is null || sharedProfileInput is null) return;

        var sharedMode = IsSharedLaunchMode();
        folderLabel.Text = "Instanced folder";
        folderLabel.Visible = !sharedMode;
        folderInput.Visible = !sharedMode;
        browseBatButton.Visible = !sharedMode;
        sharedProfileLabel.Visible = sharedMode;
        sharedProfileInput.Visible = sharedMode;
        browseSharedProfileButton.Visible = sharedMode;

        if (sharedMode)
        {
            SetY(cooldownLabel, 270);
            SetY(delayInput, 268);
            SetY(secondsLabel, 271);
            SetY(themeLabel, 344);
            SetY(themeInput, 372);
            SetY(muteMusicInput, 420);
            SetY(updateButton, 464);
        }
        else
        {
            SetY(cooldownLabel, 270);
            SetY(delayInput, 268);
            SetY(secondsLabel, 271);
            SetY(themeLabel, 344);
            SetY(themeInput, 372);
            SetY(muteMusicInput, 420);
            SetY(updateButton, 464);
        }
    }

    private static void SetY(Control control, int y) => control.Bounds = new Rectangle(control.Left, y, control.Width, control.Height);

    private void BuildLoadingOverlay()
    {
        loadingOverlay = new CuteBackgroundPanel { Bounds = new Rectangle(0, 0, 990, 700), Visible = false };
        var card = new RoundedPanel { Bounds = new Rectangle(120, 92, 750, 500), Radius = 28 };

        loadingPicture = new PictureBox
        {
            Bounds = new Rectangle(275, 46, 200, 200),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        loadingTitle = new Label
        {
            Text = "Now loading...",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 24F, FontStyle.Bold),
            Bounds = new Rectangle(40, 262, 670, 48)
        };
        loadingStatus = new Label
        {
            Text = "",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Bounds = new Rectangle(70, 326, 610, 64)
        };
        loadingCancel = Button("Cancel", 298, 410, 154, 42, "Danger");
        loadingCancel.Click += (_, _) => queueCancel?.Cancel();

        card.Controls.AddRange([loadingPicture, loadingTitle, loadingStatus, loadingCancel]);
        loadingOverlay.Controls.Add(card);
        background.Controls.Add(loadingOverlay);
        loadingOverlay.BringToFront();
    }

    private void BuildNewsOverlay()
    {
        newsOverlay = new RoundedPanel { Bounds = new Rectangle(95, 62, 800, 590), Radius = 26, Visible = false };
        newsOverlay.Controls.Add(Header("What's new?", 24, 18, 230, 40));
        newsCloseButton = Button("X", 744, 20, 34, 30, "Danger");
        newsCloseButton.Click += (_, _) => HideNewsOverlay();
        newsOverlay.Controls.Add(newsCloseButton);

        newsBannerPicture = new PictureBox
        {
            Bounds = new Rectangle(28, 68, 744, 250),
            BackColor = Color.Black,
            Cursor = Cursors.Hand,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        newsBannerPicture.Click += (_, _) => OpenSelectedNewsBanner();
        newsOverlay.Controls.Add(newsBannerPicture);

        newsBannerTitle = new Label
        {
            Text = "Loading launcher news...",
            Bounds = new Rectangle(28, 324, 744, 24),
            BackColor = Color.FromArgb(255, palette.Card),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        newsBannerTitle.Click += (_, _) => OpenSelectedNewsBanner();
        newsOverlay.Controls.Add(newsBannerTitle);

        newsDots = new NewsDotsControl
        {
            Bounds = new Rectangle(300, 352, 200, 28),
            BackColor = Color.FromArgb(255, palette.Card),
            Cursor = Cursors.Hand,
            Palette = palette
        };
        newsDots.DotSelected += async (_, index) =>
        {
            selectedNewsBannerIndex = index;
            RenderNewsDots();
            await RenderSelectedNewsBannerAsync();
        };
        newsOverlay.Controls.Add(newsDots);

        newsListPanel = new BufferedFlowLayoutPanel
        {
            Bounds = new Rectangle(28, 390, 744, 170),
            BackColor = palette.ListBack,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        newsOverlay.Controls.Add(newsListPanel);
        background.Controls.Add(newsOverlay);
    }

    private void BuildLaunchChoiceOverlay()
    {
        launchChoiceOverlay = new CuteBackgroundPanel { Bounds = new Rectangle(0, 0, 990, 700), Visible = false };
        var card = new RoundedPanel { Bounds = new Rectangle(155, 118, 680, 430), Radius = 30 };
        var title = new Label
        {
            Text = "Choose your launch method",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 23F, FontStyle.Bold),
            Bounds = new Rectangle(20, 34, 640, 70)
        };
        var subtitle = new Label
        {
            Text = "Pick how Potato Launcher should start your accounts. You can change this later in Settings.",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Bounds = new Rectangle(46, 106, 588, 56)
        };
        var mascot = new PictureBox
        {
            Bounds = new Rectangle(282, 164, 116, 130),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        try
        {
            if (File.Exists(MascotGifPath())) mascot.Image = Image.FromFile(MascotGifPath());
        }
        catch { }

        var instanced = Button("Instanced", 88, 326, 210, 50, "Primary");
        instanced.Click += (_, _) => ChooseLaunchMode("Instanced");
        var shared = Button("Shared", 382, 326, 210, 50, "Secondary");
        shared.Click += (_, _) => ChooseLaunchMode("Shared");
        card.Controls.AddRange([title, subtitle, mascot, instanced, shared]);
        launchChoiceOverlay.Controls.Add(card);
        background.Controls.Add(launchChoiceOverlay);
    }

    private void ShowLaunchChoiceOverlay()
    {
        if (launchChoiceOverlay is null) return;
        launchChoiceOverlay.Palette = palette;
        launchChoiceOverlay.Visible = true;
        launchChoiceOverlay.BringToFront();
        UpdateMascotOverlay();
        launchChoiceOverlay.Focus();
    }

    private void ChooseLaunchMode(string launchMode)
    {
        settings.LaunchMode = NormalizeLaunchMode(launchMode);
        settings.LaunchModeChosen = true;
        if (launchModeInput is not null) launchModeInput.SelectedItem = launchMode;
        SaveSettings(settings);
        LoadAccounts();
        PopulateLists();
        UpdateLaunchModeUi();
        launchChoiceOverlay.Visible = false;
        UpdateMascotOverlay();
    }

    private void LoadAccounts()
    {
        accounts.Clear();
        if (IsSharedLaunchMode())
        {
            LoadSharedAccounts();
            return;
        }
        LoadBatAccounts();
    }

    private void LoadBatAccounts()
    {
        if (string.IsNullOrWhiteSpace(settings.DalamudFolder)) return;
        if (!Directory.Exists(settings.DalamudFolder)) return;
        foreach (var file in Directory.GetFiles(settings.DalamudFolder, "*.bat").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            var name = Path.GetFileNameWithoutExtension(fileName);
            var order = 999;
            var dash = name.IndexOf('-');
            if (dash > 0 && int.TryParse(name[..dash], out var parsed))
            {
                order = parsed;
                name = name[(dash + 1)..];
            }
            var accountKey = ResolveBatchAccountKey(file);
            var (useSteam, useOtp) = ReadAccountFlagsFromKey(accountKey);
            accounts.Add(new Account(name, fileName, order, accountKey, useSteam, useOtp));
        }
    }

    private void LoadSharedAccounts()
    {
        if (string.IsNullOrWhiteSpace(settings.SharedProfileFolder)) return;
        var accountListPath = Path.Combine(settings.SharedProfileFolder, "accountsList.json");
        if (!File.Exists(accountListPath)) return;

        var batLookup = LoadBatAccountLookup();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(accountListPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return;
            var accountEntries = document.RootElement.EnumerateArray()
                .Select(element => new
                {
                    Element = element,
                    UserName = GetJsonString(element, "UserName"),
                    UseSteam = GetJsonBool(element, "UseSteamServiceAccount"),
                    UseOtp = GetJsonBool(element, "UseOtp")
                })
                .Where(account => !string.IsNullOrWhiteSpace(account.UserName))
                .ToList();

            foreach (var entry in accountEntries)
            {
                var userName = entry.UserName;
                var useSteam = entry.UseSteam;
                var useOtp = entry.UseOtp;
                var accountKey = BuildAccountKey(userName, useSteam, useOtp);
                var characterName = GetJsonString(entry.Element, "ChosenCharacterName");
                var displayName = string.IsNullOrWhiteSpace(characterName) ? userName : $"{userName} - {characterName}";
                var key = accountKey;
                var order = 999;
                if (batLookup.TryGetValue(accountKey, out var batAccount))
                {
                    displayName = $"{userName} - {batAccount.Name}";
                    key = batAccount.BatchFile;
                    order = batAccount.SortOrder;
                }

                accounts.Add(new Account(displayName, key, order, accountKey, useSteam, useOtp));
            }
        }
        catch { }
    }

    private Dictionary<string, Account> LoadBatAccountLookup()
    {
        var lookup = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(settings.DalamudFolder)) return lookup;
        if (!Directory.Exists(settings.DalamudFolder)) return lookup;
        foreach (var file in Directory.GetFiles(settings.DalamudFolder, "*.bat").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var accountKey = ResolveBatchAccountKey(file);
            if (string.IsNullOrWhiteSpace(accountKey)) continue;
            var fileName = Path.GetFileName(file);
            var name = Path.GetFileNameWithoutExtension(fileName);
            var order = 999;
            var dash = name.IndexOf('-');
            if (dash > 0 && int.TryParse(name[..dash], out var parsed))
            {
                order = parsed;
                name = name[(dash + 1)..];
            }
            var (useSteam, useOtp) = ReadAccountFlagsFromKey(accountKey);
            lookup.TryAdd(accountKey, new Account(name, fileName, order, accountKey, useSteam, useOtp));
        }
        return lookup;
    }

    private static string ResolveBatchAccountKey(string batchPath)
    {
        var launchInfo = ReadBatchLaunchInfo(batchPath);
        if (string.IsNullOrWhiteSpace(launchInfo.AccountKey)) return "";
        if (string.IsNullOrWhiteSpace(launchInfo.RoamingPath)) return launchInfo.AccountKey;
        var accountListPath = Path.Combine(Environment.ExpandEnvironmentVariables(launchInfo.RoamingPath), "accountsList.json");
        if (!File.Exists(accountListPath)) return launchInfo.AccountKey;

        var userName = GetUserNameFromAccountKey(launchInfo.AccountKey);
        if (string.IsNullOrWhiteSpace(userName)) return launchInfo.AccountKey;
        var (batchUseSteam, _) = ReadAccountFlagsFromKey(launchInfo.AccountKey);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(accountListPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return launchInfo.AccountKey;
            return document.RootElement.EnumerateArray()
                .Where(element => userName.Equals(GetJsonString(element, "UserName"), StringComparison.OrdinalIgnoreCase))
                .Select(element => new
                {
                    UserName = GetJsonString(element, "UserName"),
                    UseSteam = GetJsonBool(element, "UseSteamServiceAccount"),
                    UseOtp = GetJsonBool(element, "UseOtp")
                })
                .OrderByDescending(account => account.UseSteam == batchUseSteam)
                .ThenByDescending(account => account.UseOtp)
                .Select(account => BuildAccountKey(account.UserName, account.UseSteam, account.UseOtp))
                .FirstOrDefault() ?? launchInfo.AccountKey;
        }
        catch
        {
            return launchInfo.AccountKey;
        }
    }

    private static string GetUserNameFromAccountKey(string accountKey)
    {
        var parts = accountKey.Split('-');
        return parts.Length >= 3 ? string.Join("-", parts.Take(parts.Length - 2)) : accountKey;
    }

    private static string BuildAccountKey(string userName, bool useSteamServiceAccount, bool useOtp)
    {
        return $"{userName}-{useOtp}-{useSteamServiceAccount}";
    }

    private static (bool UseSteamServiceAccount, bool UseOtp) ReadAccountFlagsFromKey(string accountKey)
    {
        var parts = accountKey.Split('-');
        if (parts.Length < 3) return (false, false);
        return (bool.TryParse(parts[^1], out var useSteam) && useSteam, bool.TryParse(parts[^2], out var useOtp) && useOtp);
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private static bool GetJsonBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
    }

    private static BatchLaunchInfo ReadBatchLaunchInfo(string batchPath)
    {
        try
        {
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var accountKey = "";
            var roamingPath = "";
            foreach (var rawLine in File.ReadLines(batchPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) || line.Equals("rem", StringComparison.OrdinalIgnoreCase)) continue;
                var setMatch = System.Text.RegularExpressions.Regex.Match(line, @"^set\s+([^=]+)=(.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (setMatch.Success)
                {
                    variables[setMatch.Groups[1].Value.Trim()] = setMatch.Groups[2].Value.Trim();
                    continue;
                }

                var expanded = ExpandBatchVariables(line, variables);
                var accountMatch = System.Text.RegularExpressions.Regex.Match(expanded, @"-{1,2}account(?:=|\s+)(?:""([^""]+)""|(\S+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (accountMatch.Success)
                {
                    accountKey = (accountMatch.Groups[1].Success ? accountMatch.Groups[1].Value : accountMatch.Groups[2].Value).Trim();
                }
                var roamingMatch = System.Text.RegularExpressions.Regex.Match(expanded, @"-{1,2}roamingPath(?:=|\s+)(?:""([^""]+)""|(\S+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (roamingMatch.Success)
                {
                    roamingPath = (roamingMatch.Groups[1].Success ? roamingMatch.Groups[1].Value : roamingMatch.Groups[2].Value).Trim();
                }
            }
            return new BatchLaunchInfo(accountKey, roamingPath);
        }
        catch { }
        return new BatchLaunchInfo("", "");
    }

    private static string ExpandBatchVariables(string text, Dictionary<string, string> variables)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, "%([^%]+)%", match =>
            variables.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    private void PopulateLists()
    {
        accountList.Items.Clear();
        memberList.Items.Clear();
        IEnumerable<Account> orderedAccounts = IsSharedLaunchMode()
            ? accounts
            : accounts.OrderBy(account => account.SortOrder).ThenBy(account => account.Name);
        foreach (var account in orderedAccounts)
        {
            accountList.Items.Add(account);
            memberList.Items.Add(account);
        }
        bandList.Items.Clear();
        foreach (var band in CurrentBands())
        {
            NormalizeBand(band);
            bandList.Items.Add(band);
        }
        bandList.SelectedIndex = bandList.Items.Count > 0 ? 0 : -1;
        UpdateAccountStatus();
        ApplyTheme(settings.Theme);
    }

    private void UpdateAccountStatus()
    {
        if (status is null) return;
        if (accounts.Count == 0)
        {
            status.Text = IsSharedLaunchMode()
                ? "Click here to choose your Shared folder."
                : "Click here to choose your Instanced folder.";
            statusPill.Cursor = Cursors.Hand;
            status.Cursor = Cursors.Hand;
            return;
        }
        statusPill.Cursor = Cursors.Default;
        status.Cursor = Cursors.Default;
        status.Text = IsSharedLaunchMode()
            ? $"Found {accounts.Count} shared account{(accounts.Count == 1 ? "" : "s")}."
            : $"Found {accounts.Count} launcher BAT file{(accounts.Count == 1 ? "" : "s")}.";
    }

    private List<BandConfig> CurrentBands() => IsSharedLaunchMode() ? settings.SharedBands : settings.InstancedBands;

    private void MigrateLegacyBands()
    {
        if (settings.Bands.Count == 0) return;
        var target = CurrentBands();
        foreach (var band in settings.Bands)
        {
            target.Add(band);
        }
        settings.Bands.Clear();
        SaveSettings(settings);
    }

    private void RemoveOldDefaultBands()
    {
        var removed = 0;
        removed += settings.Bands.RemoveAll(IsOldDefaultBand);
        removed += settings.InstancedBands.RemoveAll(IsOldDefaultBand);
        removed += settings.SharedBands.RemoveAll(IsOldDefaultBand);
        if (removed > 0) SaveSettings(settings);
    }

    private static bool IsOldDefaultBand(BandConfig band)
    {
        return
            band.Name is "Band 1" or "Band 2" &&
            band.BatchFiles.Count == 8 &&
            band.BatchFiles.All(file => System.Text.RegularExpressions.Regex.IsMatch(file, @"^(0[1-9]|1[0-6])-", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private void NormalizeBand(BandConfig band)
    {
        band.BatchFiles = band.BatchFiles
            .Select(saved => accounts.FirstOrDefault(account => account.BatchFile.Equals(saved, StringComparison.OrdinalIgnoreCase) || account.BatchFile.StartsWith(saved, StringComparison.OrdinalIgnoreCase))?.BatchFile)
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private void LoadSelectedBand()
    {
        if (bandList.SelectedItem is not BandConfig band) return;
        loadingBand = true;
        bandName.Text = band.Name;
        for (var index = 0; index < memberList.Items.Count; index++)
        {
            var account = (Account)memberList.Items[index];
            memberList.SetItemChecked(index, band.BatchFiles.Contains(account.BatchFile, StringComparer.OrdinalIgnoreCase));
        }
        loadingBand = false;
    }

    private void SaveCurrentBand()
    {
        SaveCurrentBand(true);
    }

    private void SaveCurrentBand(bool refreshListItem)
    {
        if (bandList.SelectedItem is not BandConfig band) return;
        band.Name = string.IsNullOrWhiteSpace(bandName.Text) ? "Unnamed Band" : bandName.Text.Trim();
        band.BatchFiles = memberList.CheckedItems.Cast<Account>().Select(account => account.BatchFile).ToList();
        var index = bandList.SelectedIndex;
        SaveSettingsFromInputs();
        if (refreshListItem && index >= 0)
        {
            bandList.Items[index] = band;
            bandList.SelectedIndex = index;
        }
    }

    private void AddBand()
    {
        var bands = CurrentBands();
        var band = new BandConfig { Name = $"Band {bands.Count + 1}" };
        bands.Add(band);
        bandList.Items.Add(band);
        bandList.SelectedIndex = bandList.Items.Count - 1;
        SaveSettingsFromInputs();
    }

    private void DeleteBand()
    {
        if (bandList.SelectedItem is not BandConfig band) return;
        var index = bandList.SelectedIndex;
        CurrentBands().Remove(band);
        bandList.Items.RemoveAt(index);
        bandList.SelectedIndex = bandList.Items.Count == 0 ? -1 : Math.Min(index, bandList.Items.Count - 1);
        SaveSettingsFromInputs();
    }

    private void LaunchSelectedAccount()
    {
        if (accountList.SelectedItem is Account account) _ = LaunchSingleAccountAsync(account);
    }

    private async Task LaunchSingleAccountAsync(Account account)
    {
        queueCancel?.Cancel();
        queueCancel?.Dispose();
        var cancellation = new CancellationTokenSource();
        queueCancel = cancellation;
        ShowLoadingOverlay($"Loading {account.Name}", "Preparing XIVLauncher handoff...");
        try
        {
            await LaunchAccountAsync(account, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            status.Text = $"Cancelled {account.Name}.";
            UpdateLoadingOverlay($"Cancelled {account.Name}.");
        }
        finally
        {
            HideLoadingOverlay();
            cancellation.Dispose();
            if (ReferenceEquals(queueCancel, cancellation)) queueCancel = null;
        }
    }

    private async Task LaunchSelectedBandAsync()
    {
        SaveCurrentBand();
        if (bandList.SelectedItem is not BandConfig band)
        {
            status.Text = "Choose or create a band first.";
            return;
        }
        var bandAccounts = band.BatchFiles.Select(file => accounts.FirstOrDefault(account => account.BatchFile.Equals(file, StringComparison.OrdinalIgnoreCase))).Where(account => account is not null).Cast<Account>().ToList();
        if (bandAccounts.Count == 0)
        {
            status.Text = $"{band.Name} has no accounts selected.";
            return;
        }
        queueCancel?.Cancel();
        queueCancel?.Dispose();
        var cancellation = new CancellationTokenSource();
        queueCancel = cancellation;
        cancelButton.Visible = true;
        launchBandButton.Enabled = false;
        ShowLoadingOverlay($"Loading {band.Name}", $"Queueing {bandAccounts.Count} account{(bandAccounts.Count == 1 ? "" : "s")}...");
        try
        {
            for (var index = 0; index < bandAccounts.Count; index++)
            {
                var account = bandAccounts[index];
                SetRandomLoadingGif();
                loadingTitle.Text = $"Loading {account.Name}";
                UpdateLoadingOverlay($"{band.Name}: launching {account.Name} ({index + 1}/{bandAccounts.Count}).");
                await LaunchAccountAsync(account, cancellation.Token, true);
                status.Text = $"{band.Name}: launched {account.Name} ({index + 1}/{bandAccounts.Count}).";
                if (index == bandAccounts.Count - 1) break;
                var next = bandAccounts[index + 1];
                for (var remaining = settings.DelaySeconds; remaining > 0; remaining--)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    status.Text = $"{band.Name}: next {next.Name} in {remaining}s.";
                    UpdateLoadingOverlay($"Next: {next.Name} in {remaining}s.");
                    await Task.Delay(1000, cancellation.Token);
                }
            }
            status.Text = $"{band.Name} queue complete.";
            UpdateLoadingOverlay($"{band.Name} queue complete.");
        }
        catch (OperationCanceledException)
        {
            status.Text = $"{band.Name} queue cancelled.";
            UpdateLoadingOverlay($"{band.Name} queue cancelled.");
        }
        finally
        {
            HideLoadingOverlay();
            cancelButton.Visible = false;
            launchBandButton.Enabled = true;
            cancellation.Dispose();
            if (ReferenceEquals(queueCancel, cancellation)) queueCancel = null;
        }
    }

    private async Task<bool> LaunchAccountAsync(Account account, CancellationToken token, bool quiet = false)
    {
        SaveSettingsFromInputs();
        try
        {
            var before = GetLauncherProcessIds();
            var command = IsSharedLaunchMode()
                ? BuildSharedLaunchCommand(account)
                : BuildBatchLaunchCommand(account);
            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                WorkingDirectory = command.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var launcherProcess = Process.Start(startInfo);
            status.Text = IsSharedLaunchMode()
                ? $"Started {account.Name} through Shared."
                : $"Started {account.Name} from BAT command.";
            var waitMessage = account.UseOtp
                ? $"Started {account.Name}. OTP is enabled, waiting for manual login..."
                : $"Started {account.Name}. Waiting for XIVLauncher to finish...";
            UpdateLoadingOverlay(waitMessage);

            await WaitForLauncherHandoffAsync(launcherProcess, before, account.Name, token);
            if (!quiet)
            {
                status.Text = $"Launcher finished for {account.Name}.";
            }
            UpdateLoadingOverlay($"{account.Name} launcher handoff complete.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status.Text = $"Could not launch {account.Name}: {ex.Message}";
            return false;
        }
    }

    private bool IsSharedLaunchMode() => NormalizeLaunchMode(settings.LaunchMode) == "Shared";

    private LaunchCommand BuildBatchLaunchCommand(Account account)
    {
        var batchPath = Path.Combine(settings.DalamudFolder, account.BatchFile);
        if (!File.Exists(batchPath))
        {
            throw new FileNotFoundException($"Missing BAT file: {account.BatchFile}", batchPath);
        }
        return BuildBatchLaunchCommand(batchPath, account);
    }

    private LaunchCommand BuildSharedLaunchCommand(Account account)
    {
        if (string.IsNullOrWhiteSpace(account.AccountKey))
        {
            throw new InvalidOperationException($"{account.Name} has no --account value in its BAT file.");
        }

        var exe = FindSharedXivLauncherExe();
        if (string.IsNullOrWhiteSpace(exe))
        {
            throw new FileNotFoundException("XIVLauncher.exe was not found in the standard Local XIVLauncher folders.");
        }

        var autoLogin = account.UseOtp ? "false" : "true";
        var arguments = $"--account={QuoteArgument(account.AccountKey)} --Autologinenabled={autoLogin}";
        return new LaunchCommand(exe, arguments, Path.GetDirectoryName(exe) ?? "");
    }

    private static string? FindSharedXivLauncherExe()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVLauncher");
        var candidates = new[]
        {
            Path.Combine(local, "current", "XIVLauncher.exe"),
            Path.Combine(local, "XIVLauncher.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private LaunchCommand BuildBatchLaunchCommand(string batchPath, Account account)
    {
        var line = File.ReadLines(batchPath).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith("rem", StringComparison.OrdinalIgnoreCase)) ?? "";
        var tokens = TokenizeCommandLine(Environment.ExpandEnvironmentVariables(line));
        if (tokens.Count > 0 && tokens[0].Equals("start", StringComparison.OrdinalIgnoreCase))
        {
            var index = 1;
            if (index < tokens.Count && string.IsNullOrWhiteSpace(tokens[index])) index++;
            var workingDirectory = Path.GetDirectoryName(batchPath) ?? settings.DalamudFolder;
            while (index < tokens.Count)
            {
                if (tokens[index].Equals("/d", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Count)
                {
                    workingDirectory = tokens[index + 1];
                    index += 2;
                    continue;
                }
                if (tokens[index].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var exe = tokens[index];
                    var args = BuildAccountAwareArguments(tokens.Skip(index + 1), account);
                    return new LaunchCommand(exe, args, workingDirectory);
                }
                index++;
            }
        }
        throw new InvalidOperationException($"Unsupported BAT format. Expected a start command that points to an .exe: {Path.GetFileName(batchPath)}");
    }

    private static string BuildAccountAwareArguments(IEnumerable<string> rawTokens, Account account)
    {
        var tokens = rawTokens.ToList();
        var normalized = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (IsOptionToken(token, "account"))
            {
                if (IsSeparateOption(token) && index + 1 < tokens.Count) index++;
                normalized.Add($"--account={account.AccountKey}");
                continue;
            }
            if (account.UseOtp && IsOptionToken(token, "autologinenabled"))
            {
                if (IsSeparateOption(token) && index + 1 < tokens.Count) index++;
                continue;
            }
            normalized.Add(token);
        }

        if (account.UseOtp)
        {
            normalized.Add("--Autologinenabled=false");
        }

        return string.Join(" ", normalized.Select(QuoteArgument));
    }

    private static bool IsOptionToken(string token, string optionName)
    {
        var trimmed = token.TrimStart('-', '/');
        return trimmed.Equals(optionName, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith($"{optionName}=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSeparateOption(string token)
    {
        return !token.Contains('=', StringComparison.Ordinal);
    }

    private static List<string> TokenizeCommandLine(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                tokens.Add(current.ToString());
                current.Clear();
                while (i + 1 < command.Length && char.IsWhiteSpace(command[i + 1])) i++;
                continue;
            }
            current.Append(ch);
        }
        tokens.Add(current.ToString());
        return tokens;
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0) return "\"\"";
        return argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument;
    }

    private async Task<LauncherWindow?> WaitForLauncherAsync(HashSet<int> existingProcessIds, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var windows = GetLauncherWindows();
            var fresh = windows.FirstOrDefault(window => !existingProcessIds.Contains(window.ProcessId));
            if (fresh.Handle != IntPtr.Zero) return fresh;
            var any = windows.FirstOrDefault();
            if (any.Handle != IntPtr.Zero) return any;
            await Task.Delay(400, token);
        }
        return null;
    }

    private async Task WaitForLauncherHandoffAsync(Process? launcherProcess, HashSet<int> existingProcessIds, string accountName, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        var sawLauncherWindow = false;
        var processExited = launcherProcess is null;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();

            if (launcherProcess is not null)
            {
                try
                {
                    launcherProcess.Refresh();
                    processExited = launcherProcess.HasExited;
                    if (!processExited && launcherProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        sawLauncherWindow = true;
                        status.Text = $"Waiting for XIVLauncher to finish {accountName}...";
                        UpdateLoadingOverlay(status.Text);
                    }
                }
                catch
                {
                    processExited = true;
                }
            }

            var windows = GetLauncherWindows();
            var window = windows.FirstOrDefault(item =>
                launcherProcess is not null && item.ProcessId == launcherProcess.Id ||
                !existingProcessIds.Contains(item.ProcessId));
            if (window.Handle != IntPtr.Zero)
            {
                sawLauncherWindow = true;
                status.Text = $"Waiting for XIVLauncher to finish {accountName}...";
                UpdateLoadingOverlay(status.Text);
                await WaitForLauncherCloseAsync(window, token);
                return;
            }

            if (processExited && sawLauncherWindow)
            {
                return;
            }

            if (processExited && launcherProcess is not null)
            {
                return;
            }

            await Task.Delay(500, token);
        }
    }

    private static HashSet<int> GetLauncherProcessIds() => GetLauncherWindows().Select(window => window.ProcessId).ToHashSet();

    private static List<LauncherWindow> GetLauncherWindows()
    {
        var windows = new List<LauncherWindow>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && (process.ProcessName.Contains("XIVLauncher", StringComparison.OrdinalIgnoreCase) || process.MainWindowTitle.Contains("XIVLauncher", StringComparison.OrdinalIgnoreCase)))
                {
                    windows.Add(new LauncherWindow(process.Id, process.MainWindowHandle));
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
        return windows;
    }

    private async Task WaitForLauncherCloseAsync(LauncherWindow launcher, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(launcher.ProcessId);
                if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                {
                    return;
                }
            }
            catch
            {
                return;
            }
            await Task.Delay(500, token);
        }
    }

    private void ShowLoadingOverlay(string title, string detail)
    {
        SetRandomLoadingGif();
        loadingTitle.Text = title;
        loadingStatus.Text = detail;
        loadingOverlay.Visible = true;
        loadingOverlay.BringToFront();
        loadingOverlay.Focus();
        loadingOverlay.Refresh();
    }

    private void UpdateLoadingOverlay(string detail)
    {
        if (!loadingOverlay.Visible) return;
        loadingStatus.Text = detail;
        loadingStatus.Refresh();
    }

    private void HideLoadingOverlay()
    {
        loadingOverlay.Visible = false;
    }

    private async Task ShowNewsOverlayAsync()
    {
        newsOverlay.Visible = true;
        newsOverlay.BringToFront();
        newsBannerTitle.Text = "Loading launcher news...";
        newsListPanel.Controls.Clear();
        newsDots.DotCount = 0;
        whatsNewButton.Enabled = false;
        try
        {
            await LoadLauncherNewsAsync();
            await RenderNewsOverlayAsync();
        }
        catch (Exception ex)
        {
            newsBannerTitle.Text = "Could not load FFXIV news.";
            newsListPanel.Controls.Add(new Label
            {
                Text = ex.Message,
                Width = 700,
                Height = 48,
                ForeColor = palette.Text,
                BackColor = palette.ListBack
            });
        }
        finally
        {
            whatsNewButton.Enabled = true;
            ApplyThemeRecursive(newsOverlay);
        }
    }

    private void HideNewsOverlay()
    {
        newsOverlay.Visible = false;
        var oldImage = newsBannerPicture.Image;
        newsBannerPicture.Image = null;
        oldImage?.Dispose();
    }

    private void KillGameInstances()
    {
        var killed = 0;
        var failures = 0;
        foreach (var processName in new[] { "ffxiv", "ffxiv_dx11" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    killed++;
                }
                catch
                {
                    failures++;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        status.Text = killed == 0
            ? "No FFXIV game instances found."
            : $"Terminated {killed} FFXIV game instance{(killed == 1 ? "" : "s")}{(failures > 0 ? $" ({failures} failed)" : "")}.";
    }

    private async Task LoadLauncherNewsAsync()
    {
        newsBanners.Clear();
        newsEntries.Clear();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var bannerJson = await http.GetStringAsync($"https://frontier.ffxiv.com/v2/topics/en-us/banner.json?lang=en-us&media=pcapp&_={timestamp}");
        using (var bannerDocument = JsonDocument.Parse(bannerJson))
        {
            if (bannerDocument.RootElement.TryGetProperty("banner", out var banners) && banners.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in banners.EnumerateArray()
                    .OrderBy(item => GetJsonInt(item, "fix_order") ?? int.MaxValue)
                    .ThenBy(item => GetJsonInt(item, "order_priority") ?? int.MaxValue)
                    .Take(5))
                {
                    var imageUrl = GetJsonString(item, "lsb_banner");
                    var linkUrl = GetJsonString(item, "link");
                    if (!string.IsNullOrWhiteSpace(imageUrl) && !string.IsNullOrWhiteSpace(linkUrl))
                    {
                        newsBanners.Add(new NewsBanner(imageUrl, linkUrl));
                    }
                }
            }
        }

        var headlineJson = await http.GetStringAsync($"https://frontier.ffxiv.com/news/headline.json?lang=en-us&media=pcapp&_={timestamp}");
        using var headlineDocument = JsonDocument.Parse(headlineJson);
        AddNewsEntries(headlineDocument.RootElement, "news", false);
        newsEntries.Sort((left, right) => right.Date.CompareTo(left.Date));
    }

    private void AddNewsEntries(JsonElement root, string propertyName, bool topic)
    {
        if (!root.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array) return;
        foreach (var item in items.EnumerateArray())
        {
            var title = GetJsonString(item, "title");
            if (string.IsNullOrWhiteSpace(title)) continue;
            var url = GetJsonString(item, "url");
            var id = GetJsonString(item, "id");
            if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(id))
            {
                url = topic
                    ? $"https://na.finalfantasyxiv.com/lodestone/topics/detail/{id}"
                    : $"https://na.finalfantasyxiv.com/lodestone/news/detail/{id}";
            }

            var date = ParseNewsDate(GetJsonString(item, "date"));
            var tag = GetJsonString(item, "tag");
            newsEntries.Add(new NewsEntry(title, url, date, tag));
        }
    }

    private async Task RenderNewsOverlayAsync()
    {
        selectedNewsBannerIndex = 0;
        await RenderSelectedNewsBannerAsync();
        RenderNewsDots();
        RenderNewsList();
    }

    private async Task RenderSelectedNewsBannerAsync()
    {
        var oldImage = newsBannerPicture.Image;
        newsBannerPicture.Image = null;
        oldImage?.Dispose();
        if (newsBanners.Count == 0)
        {
            newsBannerTitle.Text = "No featured events found.";
            return;
        }

        var banner = newsBanners[Math.Clamp(selectedNewsBannerIndex, 0, newsBanners.Count - 1)];
        newsBannerTitle.Text = banner.LinkUrl;
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher");
            var bytes = await http.GetByteArrayAsync(banner.ImageUrl);
            using var stream = new MemoryStream(bytes);
            newsBannerPicture.Image = Image.FromStream(stream);
        }
        catch
        {
            newsBannerTitle.Text = "Could not load featured image. Click to open it online.";
        }
    }

    private void RenderNewsDots()
    {
        newsDots.Palette = palette;
        newsDots.DotCount = newsBanners.Count;
        newsDots.SelectedIndex = selectedNewsBannerIndex;

    }

    private void RenderNewsList()
    {
        newsListPanel.Controls.Clear();
        newsListPanel.BackColor = palette.ListBack;
        var linkWidth = Math.Max(260, newsListPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
        foreach (var item in newsEntries.Take(5))
        {
            var link = new LinkLabel
            {
                Text = $"{NewsDateLabel(item.Date)}  {item.Title}",
                Width = linkWidth,
                Height = 25,
                LinkColor = palette.Text,
                ActiveLinkColor = palette.Primary,
                VisitedLinkColor = palette.Muted,
                BackColor = palette.ListBack,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 3),
                Tag = item.Url
            };
            link.Click += (_, _) => OpenUrl(link.Tag?.ToString() ?? "");
            newsListPanel.Controls.Add(link);
        }
    }

    private static string NewsDateLabel(DateTimeOffset date)
    {
        return date == DateTimeOffset.MinValue ? "News" : date.ToLocalTime().ToString("MMM d");
    }

    private static DateTimeOffset ParseNewsDate(string rawDate)
    {
        if (DateTimeOffset.TryParse(rawDate, out var parsedDate)) return parsedDate;
        if (long.TryParse(rawDate, out var unixSeconds)) return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        return DateTimeOffset.MinValue;
    }

    private void OpenSelectedNewsBanner()
    {
        if (newsBanners.Count == 0) return;
        OpenUrl(newsBanners[Math.Clamp(selectedNewsBannerIndex, 0, newsBanners.Count - 1)].LinkUrl);
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static int? GetJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)) return number;
        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number)) return number;
        return null;
    }

    private void SetRandomLoadingGif()
    {
        var folder = GetLoadingGifFolder();
        if (!Directory.Exists(folder)) return;
        var gifs = Directory.GetFiles(folder, "*.gif", SearchOption.AllDirectories);
        if (gifs.Length == 0) return;

        Image? oldImage = null;
        try
        {
            oldImage = loadingPicture.Image;
            loadingPicture.Image = Image.FromFile(gifs[Random.Shared.Next(gifs.Length)]);
        }
        catch
        {
        }
        finally
        {
            oldImage?.Dispose();
        }
    }

    private void BrowseFolder(TextBox target, string description, Action afterSelection)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            SelectedPath = Directory.Exists(target.Text) ? target.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
            afterSelection();
        }
    }

    private void BrowseFolderFromStatus()
    {
        if (accounts.Count > 0) return;
        if (IsSharedLaunchMode())
        {
            BrowseFolder(sharedProfileInput, "Choose the XIVLauncher folder containing accountsList.json", SaveAndRescan);
            return;
        }
        BrowseFolder(folderInput, "Choose the folder containing your instanced launcher .bat files", SaveAndRescan);
    }

    private void SaveAndRescan()
    {
        SaveSettingsFromInputs();
        LoadAccounts();
        PopulateLists();
    }

    private void SaveSettingsFromInputs()
    {
        settings.DalamudFolder = folderInput.Text.Trim();
        settings.SharedProfileFolder = sharedProfileInput.Text.Trim();
        settings.LaunchMode = NormalizeLaunchMode(launchModeInput?.SelectedItem?.ToString() ?? settings.LaunchMode);
        settings.LaunchModeChosen = true;
        settings.DelaySeconds = (int)delayInput.Value;
        settings.Theme = themeInput?.SelectedItem?.ToString() ?? settings.Theme;
        settings.MusicMuted = muteMusicInput?.Checked ?? settings.MusicMuted;
        SaveSettings(settings);
    }

    private async Task CheckForUpdatesAsync()
    {
        updateButton.Enabled = false;
        var previousStatus = status.Text;
        try
        {
            status.Text = "Checking GitHub for updates...";
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher");
            using var releaseResponse = await http.GetAsync($"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest");
            releaseResponse.EnsureSuccessStatusCode();

            using var releaseDocument = JsonDocument.Parse(await releaseResponse.Content.ReadAsStringAsync());
            var root = releaseDocument.RootElement;
            var tagName = GetJsonString(root, "tag_name");
            if (string.IsNullOrWhiteSpace(tagName)) throw new InvalidOperationException("The latest GitHub release has no tag.");
            var latestVersion = ParseReleaseVersion(tagName);
            var currentVersion = CurrentAppVersion();
            if (latestVersion.CompareTo(currentVersion) <= 0)
            {
                status.Text = $"Potato Launcher is up to date ({currentVersion}).";
                MessageBox.Show($"Potato Launcher is already up to date.\n\nCurrent version: {currentVersion}", "No update needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The latest GitHub release has no downloadable assets.");
            }

            var asset = assets.EnumerateArray()
                .FirstOrDefault(item => ReleaseZipName.Equals(GetJsonString(item, "name"), StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidOperationException($"The latest GitHub release does not include {ReleaseZipName}.");
            }

            var downloadUrl = GetJsonString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(downloadUrl)) throw new InvalidOperationException("The GitHub release asset has no download URL.");

            status.Text = $"Downloading Potato Launcher {latestVersion}...";
            var tempRoot = Path.Combine(Path.GetTempPath(), $"PotatoLauncherUpdate-{Guid.NewGuid():N}");
            var zipPath = Path.Combine(tempRoot, ReleaseZipName);
            var extractPath = Path.Combine(tempRoot, "extract");
            Directory.CreateDirectory(tempRoot);
            await using (var downloadStream = await http.GetStreamAsync(downloadUrl))
            await using (var fileStream = File.Create(zipPath))
            {
                await downloadStream.CopyToAsync(fileStream);
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            StartHiddenUpdater(tempRoot, extractPath);
            Application.Exit();
        }
        catch (Exception ex)
        {
            status.Text = previousStatus;
            MessageBox.Show($"Could not update Potato Launcher.\n\n{ex.Message}", "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed) updateButton.Enabled = true;
        }
    }

    private static Version CurrentAppVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    }

    private static Version ParseReleaseVersion(string tagName)
    {
        var normalized = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version)
            ? version
            : throw new InvalidOperationException($"The latest GitHub release tag is not a valid version: {tagName}");
    }

    private static void StartHiddenUpdater(string tempRoot, string extractPath)
    {
        var appDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exePath = Application.ExecutablePath;
        var scriptPath = Path.Combine(tempRoot, "update.ps1");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $pidToWait = {{Environment.ProcessId}}
            $extract = '{{EscapePowerShellString(extractPath)}}'
            $target = '{{EscapePowerShellString(appDirectory)}}'
            $exe = '{{EscapePowerShellString(exePath)}}'
            $temp = '{{EscapePowerShellString(tempRoot)}}'
            Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue
            Copy-Item -Path (Join-Path $extract '*') -Destination $target -Recurse -Force
            Start-Process -FilePath $exe
            Start-Sleep -Seconds 2
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
            """;
        File.WriteAllText(scriptPath, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File {QuoteArgument(scriptPath)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string EscapePowerShellString(string value) => value.Replace("'", "''");

    private void ApplyTheme(string themeName)
    {
        themeName = NormalizeThemeName(themeName);
        palette = Palettes.TryGetValue(themeName, out var chosen) ? chosen : Palettes["Pink"];
        background.Palette = palette;
        loadingOverlay.Palette = palette;
        launchChoiceOverlay.Palette = palette;
        ApplyThemeAssets(themeName);
        ApplyThemeMusic(themeName);
        background.Invalidate();
        loadingOverlay.Invalidate();
        launchChoiceOverlay.Invalidate();
        ApplyThemeRecursive(this);
    }

    private void ApplyThemeAssets(string themeName)
    {
        var folder = ThemeFolder(themeName);
        var video = PickThemeAsset(folder, [".mp4", ".wmv", ".mov", ".m4v"]);
        if (!string.IsNullOrWhiteSpace(video))
        {
            themeHasVideo = true;
            themeHasImage = false;
            background.BackgroundArt = null;
            wpfMascot.Visibility = System.Windows.Visibility.Collapsed;
            settingsButton.BringToFront();
            killGameButton.BringToFront();
            whatsNewButton.BringToFront();
            backgroundVideo.Stop();
            backgroundVideo.Source = new Uri(video, UriKind.Absolute);
            videoHost.Visible = true;
            videoHost.SendToBack();
            mascotTimer.Stop();
            backgroundVideo.Play();
            ApplyLauncherLayout();
            UpdateMascotOverlay();
            return;
        }

        backgroundVideo.Stop();
        videoHost.Visible = false;
        mascotTimer.Stop();
        wpfMascot.Visibility = System.Windows.Visibility.Collapsed;
        var image = PickThemeAsset(folder, [".png", ".jpg", ".jpeg", ".bmp"]);
        themeHasVideo = false;
        themeHasImage = !string.IsNullOrWhiteSpace(image);
        background.BackgroundArt = string.IsNullOrWhiteSpace(image) ? null : LoadUnlockedImage(image);
        ApplyLauncherLayout();
        UpdateMascotOverlay();
        settingsButton.BringToFront();
        killGameButton.BringToFront();
        whatsNewButton.BringToFront();
        statusPill.BringToFront();
        if (newsOverlay.Visible) newsOverlay.BringToFront();
        if (settingsDrawerOpen) settingsDrawer.BringToFront();
    }

    private void ApplyThemeMusic(string themeName)
    {
        themeName = NormalizeThemeName(themeName);
        if (settings.MusicMuted)
        {
            themeMusic.Stop();
            return;
        }

        var music = PickThemeAsset(ThemeFolder(themeName), [".mp3", ".wav", ".wma", ".aac", ".m4a"]);
        if (string.IsNullOrWhiteSpace(music))
        {
            themeMusic.Stop();
            currentMusicPath = null;
            return;
        }

        if (!music.Equals(currentMusicPath, StringComparison.OrdinalIgnoreCase))
        {
            themeMusic.Stop();
            themeMusic.Open(new Uri(music, UriKind.Absolute));
            currentMusicPath = music;
        }
        themeMusic.Volume = 0.45;
        themeMusic.Play();
    }

    private static Image? LoadUnlockedImage(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return Image.FromStream(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string? PickThemeAsset(string folder, string[] extensions)
    {
        if (!Directory.Exists(folder)) return null;
        return Directory.GetFiles(folder)
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private void ApplyThemeRecursive(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case RoundedPanel panel:
                    panel.BackColor = CurrentCardColor(panel);
                    panel.BorderColor = palette.Border;
                    panel.Invalidate();
                    break;
                case NewsDotsControl dots:
                    dots.Palette = palette;
                    dots.BackColor = Color.FromArgb(255, palette.Card);
                    break;
                case LinkLabel linkLabel:
                    linkLabel.LinkColor = palette.Text;
                    linkLabel.ActiveLinkColor = palette.Primary;
                    linkLabel.VisitedLinkColor = palette.Muted;
                    linkLabel.BackColor = ReferenceEquals(linkLabel.Parent, newsListPanel) ? palette.ListBack : Color.Transparent;
                    break;
                case Label label:
                    label.ForeColor = label.Font.Bold ? palette.Text : palette.Muted;
                    if (ReferenceEquals(label, newsBannerTitle)) label.BackColor = Color.FromArgb(255, palette.Card);
                    break;
                case FlowLayoutPanel flow:
                    flow.BackColor = ReferenceEquals(flow, newsListPanel) ? palette.ListBack : Color.Transparent;
                    break;
                case CheckedListBox checkedList:
                    checkedList.BackColor = palette.ListBack;
                    checkedList.ForeColor = palette.Text;
                    break;
                case ListBox list:
                    list.BackColor = palette.ListBack;
                    list.ForeColor = palette.Text;
                    break;
                case TextBox text:
                    text.BackColor = palette.ListBack;
                    text.ForeColor = palette.Text;
                    break;
                case CheckBox check:
                    check.ForeColor = palette.Text;
                    check.BackColor = Color.Transparent;
                    break;
                case Button button:
                    button.BackColor = (button.Tag?.ToString() ?? "Primary") switch
                    {
                        "Secondary" => palette.Secondary,
                        "Danger" => palette.Danger,
                        _ => palette.Primary
                    };
                    button.ForeColor = Color.White;
                    break;
            }
            ApplyThemeRecursive(control);
        }
    }

    private Color CurrentCardColor(RoundedPanel panel)
    {
        if (ReferenceEquals(panel, settingsDrawer)) return Color.FromArgb(244, palette.Card);
        if (ReferenceEquals(panel, newsOverlay)) return Color.FromArgb(255, palette.Card);
        if (ReferenceEquals(panel, statusPill)) return Color.FromArgb(themeHasVideo ? 135 : themeHasImage ? 155 : 170, palette.Card);
        if (ReferenceEquals(panel, accountCard) || ReferenceEquals(panel, bandCard))
        {
            return Color.FromArgb(themeHasVideo ? 214 : themeHasImage ? 230 : palette.Card.A, palette.Card);
        }
        return palette.Card;
    }

    private static AppSettings LoadSettings()
    {
        try
        {
            var path = SettingsPath();
            if (File.Exists(path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { }
        var settings = new AppSettings();
        SaveSettings(settings);
        return settings;
    }

    private static void SaveSettings(AppSettings settings)
    {
        try
        {
            var path = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static string SettingsPath() => Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static string GetAssetRoot() => Path.Combine(AppContext.BaseDirectory, "Potato Launcher Assets");
    private static string GetLoadingGifFolder() => Path.Combine(GetAssetRoot(), "Assets");
    private static string MascotGifPath() => Path.Combine(GetLoadingGifFolder(), "09-sIayC6DgB9QOsPj4jd.gif");
    private static string ThemeFolder(string themeName) => Path.Combine(GetAssetRoot(), "themes", SafeFolderName(themeName));
    private static string NormalizeThemeName(string themeName) => themeName.Equals("ARR", StringComparison.OrdinalIgnoreCase) ? "A Realm Reborn" : themeName;
    private static string NormalizeLaunchMode(string launchMode) => launchMode is "Shared" or "Shared XIVLauncher" ? "Shared" : "Instanced";
    private static string SafeFolderName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(text.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }

    private static void EnsureThemeAssetFolders()
    {
        Directory.CreateDirectory(GetAssetRoot());
        Directory.CreateDirectory(GetLoadingGifFolder());
        Directory.CreateDirectory(Path.Combine(GetAssetRoot(), "themes"));
    }

    private RoundedPanel Card(int x, int y, int width, int height) => new() { Bounds = new Rectangle(x, y, width, height), Radius = 22 };
    private Label Header(string text, int x, int y, int width, int height) => new() { Text = text, Font = new Font("Segoe UI", 16F, FontStyle.Bold), Bounds = new Rectangle(x, y, width, height), BackColor = Color.Transparent };
    private Label Label(string text, int x, int y, int width, int height) => new() { Text = text, Bounds = new Rectangle(x, y, width, height), BackColor = Color.Transparent };
    private Button Button(string text, int x, int y, int width, int height, string role)
    {
        var button = new Button { Text = text, Bounds = new Rectangle(x, y, width, height), Tag = role, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

}

internal sealed class CuteBackgroundPanel : Panel
{
    private readonly System.Windows.Forms.Timer timer = new();
    private Image? backgroundArt;
    private float tick;
    public ThemePalette Palette { get; set; } = new(Color.FromArgb(255,226,242), Color.FromArgb(210,236,255), Color.White, Color.White, Color.Black, Color.Gray, Color.HotPink, Color.CornflowerBlue, Color.IndianRed, Color.White);
    public Image? BackgroundArt
    {
        get => backgroundArt;
        set
        {
            if (!ReferenceEquals(backgroundArt, value))
            {
                backgroundArt?.Dispose();
                backgroundArt = value;
            }
            Invalidate();
        }
    }

    public CuteBackgroundPanel()
    {
        DoubleBuffered = true;
        timer.Interval = 33;
        timer.Tick += (_, _) => { tick += 0.018f; Invalidate(); };
        timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        PaintScene(e.Graphics);
    }

    public void PaintArea(Graphics graphics, Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        var state = graphics.Save();
        graphics.TranslateTransform(-area.X, -area.Y);
        PaintScene(graphics);
        graphics.Restore(state);
    }

    private void PaintScene(Graphics graphics)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (backgroundArt is not null)
        {
            graphics.DrawImage(backgroundArt, CoverRectangle(backgroundArt.Size, ClientRectangle));
            using var veil = new LinearGradientBrush(ClientRectangle, Color.FromArgb(166, Palette.Back1), Color.FromArgb(142, Palette.Back2), 35);
            graphics.FillRectangle(veil, ClientRectangle);
        }
        else
        {
            using var bg = new LinearGradientBrush(ClientRectangle, Palette.Back1, Palette.Back2, 35);
            graphics.FillRectangle(bg, ClientRectangle);
        }
        using var bubblePen = new Pen(Color.FromArgb(88,255,255,255), 2);
        for (var index = 0; index < 22; index++)
        {
            var x = (index * 91 + MathF.Sin(tick + index) * 18) % Math.Max(1, Width);
            var y = (Height - ((tick * 45 + index * 47) % Math.Max(1, Height + 80))) - 40;
            var size = 9 + (index % 4) * 5;
            graphics.DrawEllipse(bubblePen, x, y, size, size);
        }
    }

    private static Rectangle CoverRectangle(Size imageSize, Rectangle bounds)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0) return bounds;
        var scale = Math.Max(bounds.Width / (float)imageSize.Width, bounds.Height / (float)imageSize.Height);
        var width = (int)MathF.Ceiling(imageSize.Width * scale);
        var height = (int)MathF.Ceiling(imageSize.Height * scale);
        return new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
    }
}

internal sealed class MascotOverlayForm : Form
{
    private readonly Image image;

    public MascotOverlayForm(string imagePath, Size size)
    {
        image = Image.FromFile(imagePath);
        Size = size;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Color.Fuchsia;
        TransparencyKey = Color.Fuchsia;
        DoubleBuffered = true;
        ImageAnimator.Animate(image, (_, _) => Invalidate());
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExTransparent = 0x00000020;
            const int wsExToolWindow = 0x00000080;
            const int wsExNoActivate = 0x08000000;
            var createParams = base.CreateParams;
            createParams.ExStyle |= wsExTransparent | wsExToolWindow | wsExNoActivate;
            return createParams;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var brush = new SolidBrush(TransparencyKey);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        ImageAnimator.UpdateFrames(image);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(image, FitRectangle(image.Size, ClientRectangle));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) image.Dispose();
        base.Dispose(disposing);
    }

    private static Rectangle FitRectangle(Size imageSize, Rectangle bounds)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0) return bounds;
        var scale = Math.Min(bounds.Width / (float)imageSize.Width, bounds.Height / (float)imageSize.Height);
        var width = (int)MathF.Ceiling(imageSize.Width * scale);
        var height = (int)MathF.Ceiling(imageSize.Height * scale);
        return new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
    }
}

internal sealed class TransparentGifView : Control
{
    private Image? image;
    public Image? Image
    {
        get => image;
        set
        {
            image = value;
            if (image is not null)
            {
                ImageAnimator.Animate(image, (_, _) => Invalidate());
            }
            Invalidate();
        }
    }

    public TransparentGifView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExTransparent = 0x00000020;
            var createParams = base.CreateParams;
            createParams.ExStyle |= wsExTransparent;
            return createParams;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        if (image is null) return;
        ImageAnimator.UpdateFrames(image);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(image, FitRectangle(image.Size, ClientRectangle));
    }

    private static Rectangle FitRectangle(Size imageSize, Rectangle bounds)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0) return bounds;
        var scale = Math.Min(bounds.Width / (float)imageSize.Width, bounds.Height / (float)imageSize.Height);
        var width = (int)MathF.Ceiling(imageSize.Width * scale);
        var height = (int)MathF.Ceiling(imageSize.Height * scale);
        return new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
    }
}

internal sealed class LoadingSplashForm : Form
{
    private static readonly Random Random = new();
    private readonly ThemePalette palette;

    private LoadingSplashForm(string accountName, string themeName, string? gifPath)
    {
        palette = ThemeForSplash(themeName);
        Text = "Now Loading";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(620, 360);
        BackColor = palette.Back1;
        DoubleBuffered = true;

        var title = new Label
        {
            Text = "Now Loading",
            Font = new Font("Segoe UI", 24F, FontStyle.Bold),
            ForeColor = palette.Text,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(30, 22, 360, 46)
        };

        var subtitle = new Label
        {
            Text = $"Preparing {accountName}...",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = palette.Muted,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(33, 70, 420, 26)
        };

        var tip = new Label
        {
            Text = RandomTip(),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Italic),
            ForeColor = palette.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(34, 300, 552, 28)
        };

        var picture = new PictureBox
        {
            BackColor = Color.Transparent,
            Bounds = new Rectangle(160, 100, 300, 190),
            SizeMode = PictureBoxSizeMode.Zoom
        };

        if (!string.IsNullOrWhiteSpace(gifPath) && File.Exists(gifPath))
        {
            try
            {
                picture.Image = Image.FromFile(gifPath);
            }
            catch
            {
            }
        }

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(picture);
        Controls.Add(tip);
    }

    public static LoadingSplashForm Create(string accountName, string themeName, string gifFolder)
    {
        return new LoadingSplashForm(accountName, themeName, PickRandomGif(gifFolder));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(ClientRectangle, palette.Back1, palette.Back2, 30);
        e.Graphics.FillRectangle(background, ClientRectangle);
        using var borderPen = new Pen(palette.Border, 2);
        e.Graphics.DrawRectangle(borderPen, 1, 1, Width - 3, Height - 3);
        using var cardBrush = new SolidBrush(Color.FromArgb(180, palette.Card));
        using var path = Rounded(new Rectangle(22, 18, Width - 44, Height - 36), 26);
        e.Graphics.FillPath(cardBrush, path);
        e.Graphics.DrawPath(borderPen, path);
        using var orbBrush = new SolidBrush(Color.FromArgb(80, Color.White));
        e.Graphics.FillEllipse(orbBrush, Width - 92, 30, 40, 40);
        e.Graphics.FillEllipse(orbBrush, 44, Height - 82, 24, 24);
        e.Graphics.FillEllipse(orbBrush, Width - 156, Height - 84, 18, 18);
    }

    private static string? PickRandomGif(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            var gifs = Directory.GetFiles(folder, "*.gif", SearchOption.TopDirectoryOnly);
            if (gifs.Length == 0) return null;
            return gifs[Random.Next(gifs.Length)];
        }
        catch
        {
            return null;
        }
    }

    private static string RandomTip()
    {
        string[] tips =
        [
            "May your queues be short and your potatoes mighty.",
            "Checking glamour plates, kupo...",
            "Feeding chocobo. Please wait warmly.",
            "Reticulating aether currents...",
            "Preparing snacks for the next duty."
        ];
        return tips[Random.Next(tips.Length)];
    }

    private static ThemePalette ThemeForSplash(string themeName)
    {
        return themeName switch
        {
            "Dark" => new(Color.FromArgb(18,20,34), Color.FromArgb(45,33,69), Color.FromArgb(238,30,35,54), Color.FromArgb(78,65,105), Color.FromArgb(239,233,255), Color.FromArgb(188,177,214), Color.FromArgb(199,84,154), Color.FromArgb(92,120,220), Color.FromArgb(222,88,112), Color.FromArgb(40,38,55)),
            "Chocobo" => new(Color.FromArgb(255,241,177), Color.FromArgb(255,220,114), Color.FromArgb(252,255,250,232), Color.FromArgb(225,178,67), Color.FromArgb(96,68,28), Color.FromArgb(139,98,40), Color.FromArgb(219,180,87), Color.FromArgb(121,155,80), Color.FromArgb(198,91,62), Color.FromArgb(255,252,235)),
            "Limsa Lominsa" => new(Color.FromArgb(190,230,238), Color.FromArgb(25,88,118), Color.FromArgb(246,249,252,255), Color.FromArgb(119,178,193), Color.FromArgb(21,64,86), Color.FromArgb(72,112,130), Color.FromArgb(39,139,176), Color.FromArgb(226,236,235), Color.FromArgb(207,92,83), Color.FromArgb(248,253,255)),
            "Gridania" => new(Color.FromArgb(204,226,181), Color.FromArgb(71,111,72), Color.FromArgb(246,251,246,236), Color.FromArgb(139,174,113), Color.FromArgb(46,75,39), Color.FromArgb(92,116,70), Color.FromArgb(91,143,74), Color.FromArgb(159,119,75), Color.FromArgb(192,92,64), Color.FromArgb(250,255,244)),
            "Ul'dah" => new(Color.FromArgb(238,202,143), Color.FromArgb(143,88,49), Color.FromArgb(248,255,246,232), Color.FromArgb(196,142,75), Color.FromArgb(86,54,32), Color.FromArgb(130,86,52), Color.FromArgb(199,134,54), Color.FromArgb(124,92,142), Color.FromArgb(183,72,61), Color.FromArgb(255,250,235)),
            "Ishgard" => new(Color.FromArgb(217,226,235), Color.FromArgb(78,96,125), Color.FromArgb(244,248,251,255), Color.FromArgb(151,166,190), Color.FromArgb(45,55,76), Color.FromArgb(91,101,126), Color.FromArgb(82,111,164), Color.FromArgb(168,177,194), Color.FromArgb(174,76,91), Color.FromArgb(248,251,255)),
            "Fuchsia" => new(Color.FromArgb(74,20,86), Color.FromArgb(255,97,187), Color.FromArgb(242,255,241,252), Color.FromArgb(245,160,230), Color.FromArgb(84,27,96), Color.FromArgb(139,63,145), Color.FromArgb(217,42,154), Color.FromArgb(139,88,232), Color.FromArgb(224,76,112), Color.FromArgb(255,247,253)),
            _ => new(Color.FromArgb(255,226,242), Color.FromArgb(210,236,255), Color.FromArgb(250,255,250,255), Color.FromArgb(238,182,226), Color.FromArgb(92,48,104), Color.FromArgb(137,82,139), Color.FromArgb(239,111,166), Color.FromArgb(124,150,224), Color.FromArgb(222,104,130), Color.FromArgb(255,250,255))
        };
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        radius = Math.Min(radius, Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 2));
        var diameter = radius * 2;
        var rect = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(rect, 180, 90);
        rect.X = bounds.Right - diameter - 1;
        path.AddArc(rect, 270, 90);
        rect.Y = bounds.Bottom - diameter - 1;
        path.AddArc(rect, 0, 90);
        rect.X = bounds.Left;
        path.AddArc(rect, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 18;
    public Color BorderColor { get; set; } = Color.FromArgb(238,182,226);
    public RoundedPanel() => DoubleBuffered = true;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(ClientRectangle, Radius);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(BorderColor, 1F);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }
    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Region?.Dispose();
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            Region = null;
            return;
        }
        Region = new Region(Rounded(ClientRectangle, Radius));
    }
    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        radius = Math.Min(radius, Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 2));
        var diameter = radius * 2;
        var rect = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(rect, 180, 90);
        rect.X = bounds.Right - diameter - 1;
        path.AddArc(rect, 270, 90);
        rect.Y = bounds.Bottom - diameter - 1;
        path.AddArc(rect, 0, 90);
        rect.X = bounds.Left;
        path.AddArc(rect, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }
}

internal sealed class NewsDotsControl : Control
{
    private int dotCount;
    private int selectedIndex;
    public event EventHandler<int>? DotSelected;
    public ThemePalette Palette { get; set; } = new(Color.White, Color.White, Color.White, Color.LightGray, Color.Black, Color.Gray, Color.HotPink, Color.CornflowerBlue, Color.IndianRed, Color.White);

    public int DotCount
    {
        get => dotCount;
        set
        {
            dotCount = Math.Max(0, value);
            if (selectedIndex >= dotCount) selectedIndex = Math.Max(0, dotCount - 1);
            Invalidate();
        }
    }

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = dotCount == 0 ? 0 : Math.Clamp(value, 0, dotCount - 1);
            Invalidate();
        }
    }

    public NewsDotsControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        if (dotCount <= 0) return;

        const int dotSize = 12;
        const int spacing = 20;
        var totalWidth = (dotCount * dotSize) + ((dotCount - 1) * spacing);
        var startX = Math.Max(0, (Width - totalWidth) / 2);
        var y = Math.Max(0, (Height - dotSize) / 2);
        using var selectedBrush = new SolidBrush(Palette.Primary);
        using var emptyBrush = new SolidBrush(Color.FromArgb(230, Palette.Card));
        using var outlinePen = new Pen(Palette.Primary, 2);

        for (var index = 0; index < dotCount; index++)
        {
            var bounds = new Rectangle(startX + index * (dotSize + spacing), y, dotSize, dotSize);
            if (index == selectedIndex)
            {
                e.Graphics.FillEllipse(selectedBrush, bounds);
            }
            else
            {
                e.Graphics.FillEllipse(emptyBrush, bounds);
                e.Graphics.DrawEllipse(outlinePen, bounds);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var hit = HitTest(e.Location);
        if (hit < 0) return;
        SelectedIndex = hit;
        DotSelected?.Invoke(this, hit);
    }

    private int HitTest(Point point)
    {
        if (dotCount <= 0) return -1;
        const int dotSize = 12;
        const int spacing = 20;
        var totalWidth = (dotCount * dotSize) + ((dotCount - 1) * spacing);
        var startX = Math.Max(0, (Width - totalWidth) / 2);
        var y = Math.Max(0, (Height - dotSize) / 2);
        for (var index = 0; index < dotCount; index++)
        {
            var bounds = new Rectangle(startX + index * (dotSize + spacing) - 4, y - 4, dotSize + 8, dotSize + 8);
            if (bounds.Contains(point)) return index;
        }
        return -1;
    }
}
