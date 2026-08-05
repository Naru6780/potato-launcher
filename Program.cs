using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfGrid = System.Windows.Controls.Grid;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfImage = System.Windows.Controls.Image;
using WpfMediaElement = System.Windows.Controls.MediaElement;
using WpfStretch = System.Windows.Media.Stretch;
using WpfThickness = System.Windows.Thickness;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PotatoLauncher.Tests")]

namespace PotatoLauncher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--welcome-preview", StringComparer.OrdinalIgnoreCase))
        {
            Application.Run(new ArtemisWelcomeForm());
            return;
        }
        if (args.Contains("--pet-preview", StringComparer.OrdinalIgnoreCase))
        {
            var pet = ArtemisDesktopPetForm.TryCreate();
            if (pet is null)
            {
                MessageBox.Show("Artemis animation assets could not be loaded.", "Potato Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.Run(pet);
            return;
        }
        Application.Run(new StartupApplicationContext());
    }
}

internal sealed record Account(string Name, string BatchFile, int SortOrder, string AccountKey, bool UseSteamServiceAccount = false, bool UseOtp = false)
{
    public override string ToString()
    {
        var separator = Name.LastIndexOf(" - ", StringComparison.Ordinal);
        return separator >= 0 && separator < Name.Length - 3
            ? Name[(separator + 3)..].Trim()
            : Name;
    }
}

internal sealed class BandConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Band";
    public List<string> BatchFiles { get; set; } = [];
    public override string ToString() => $"{Name} ({BatchFiles.Count})";
}

internal sealed class AppSettings
{
    public string DalamudFolder { get; set; } = "";
    public string LaunchMode { get; set; } = "Instanced";
    public string SharedProfileFolder { get; set; } = "";
    public bool LaunchModeChosen { get; set; }
    public string Theme { get; set; } = "Pink";
    public int LaunchCooldownSeconds { get; set; } = 0;
    public bool WaitForClientInitializationBeforeNextLaunch { get; set; }
    public string AccountDisplayMode { get; set; } = "Text";
    public int AccountPanelWidth { get; set; }
    public bool RandomizeThemeAtLaunch { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public string LastShownChangelogVersion { get; set; } = "";
    public Dictionary<string, AccountIconProfile> AccountIcons { get; set; } = [];
    public List<BandConfig> Bands { get; set; } = [];
    public List<BandConfig> InstancedBands { get; set; } = [];
    public List<BandConfig> SharedBands { get; set; } = [];

}

internal sealed class AccountListState
{
    public List<string> SharedAccountOrder { get; set; } = [];
    public List<string> InstancedAccountOrder { get; set; } = [];
    public Dictionary<string, DateTime> LastConnectedUtc { get; set; } = [];
}

internal static class AppText
{
    public const string LodestoneCharacterSearchUrl = "https://na.finalfantasyxiv.com/lodestone/character/";
    public const string LodestoneHelperLinkText = "Open Lodestone character search";

    public static string WindowTitle(string version)
    {
        version = DisplayVersion(version);
        return string.IsNullOrWhiteSpace(version)
            ? "Potato Launcher"
            : $"Potato Launcher v{version}";
    }

    internal static string DisplayVersion(string version)
    {
        version = (version ?? "").Trim();
        if (string.IsNullOrWhiteSpace(version)) return "";
        var metadataIndex = version.IndexOf('+');
        if (metadataIndex >= 0) version = version[..metadataIndex];
        return Version.TryParse(version, out var parsed)
            ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}"
            : version;
    }

    public static string LoadingCooldownText(int seconds) => $"{Math.Max(0, seconds)}s";

    public static string MissingAccountIconStatus(int missingCount)
    {
        return missingCount == 1
            ? "1 account needs a linked Lodestone profile."
            : $"{missingCount} accounts need linked Lodestone profiles.";
    }

    public static string HelpWindowText()
    {
        return NormalizeLineBreaks("""
        Launch modes
        Instanced mode loads one account per XIVLauncher BAT file from your selected folder.
        Shared mode reads XIVLauncher's accountsList.json from the selected profile folder.

        Accounts
        Double-click an account to launch it. Drag accounts to reorder them. Right-click an account to open Lodestone options, kill that account's running client, sort accounts, or delete an account from Potato Launcher.

        Lodestone profiles and portraits
        Link each account to a Lodestone character profile URL to show character names and portraits. Use the Lodestone search link in the profile prompt if you need to find the character page.

        Bands
        Bands are launch groups. Create a band, select the accounts that belong to it, then use Launch band to start them in sequence. Right-click a band to rename it or terminate the running clients for only that band.

        Multiband
        Pair two Potato Launcher PCs on the same private network, choose one local band and one remote band, and save them as a launch plan. Launch both queues from the main PC with a synchronized countdown and combined progress. Account credentials always stay on their own PC.

        Launching
        OTP-enabled accounts launch with autologin disabled so you can finish login manually. The launch cooldown setting adds a delay between band launches.

        Display and themes
        Switch the account list between Text and Roster display in Settings. Themes can change colors and backgrounds, and can be randomized each time the app starts.

        Import and export
        Export accounts or bands when sharing setup data with friends. Import modes let you append, merge, replace, or overwrite existing data.

        News and updates
        What's new? shows recent FFXIV launcher news. Check for updates downloads the latest Potato Launcher release from GitHub.

        Safety tools
        Kill FFXIV closes every running FFXIV game process. Per-account and per-band kill actions only target clients Potato Launcher can match to those accounts.

        Optimizer
        Optimizer opens a live monitor for running FFXIV clients. It can show per-client CPU, GPU, memory, and affinity, and can apply CPU lanes and working-set trims when enabled.
        """);
    }

    private static string NormalizeLineBreaks(string text)
    {
        return Regex.Replace(text.Trim(), @"\r?\n", Environment.NewLine);
    }
}

internal sealed class TextUpdateGate(TimeSpan duplicateInterval)
{
    private string lastText = "";
    private DateTime lastAppliedUtc = DateTime.MinValue;

    public bool ShouldApply(string? text, DateTime nowUtc, bool force = false)
    {
        text ??= "";
        if (force || !text.Equals(lastText, StringComparison.Ordinal))
        {
            lastText = text;
            lastAppliedUtc = nowUtc;
            return true;
        }

        if (nowUtc - lastAppliedUtc < duplicateInterval) return false;
        lastAppliedUtc = nowUtc;
        return true;
    }

    public void Reset()
    {
        lastText = "";
        lastAppliedUtc = DateTime.MinValue;
    }
}

internal readonly record struct LauncherLayoutMetrics(int Margin, int Top, int Gap, int ContentHeight, int AccountWidth, int BandWidth)
{
    private const int MaximumContentWidth = 1600;

    public static LauncherLayoutMetrics Calculate(int clientWidth, int clientHeight, int requestedAccountWidth)
    {
        var safeClientWidth = Math.Max(860, clientWidth);
        var safeClientHeight = Math.Max(620, clientHeight);
        var outerMargin = Math.Clamp(safeClientWidth / 24, 24, 56);
        var contentWidth = Math.Min(MaximumContentWidth, safeClientWidth - outerMargin * 2);
        var margin = Math.Max(24, (safeClientWidth - contentWidth) / 2);
        var top = Math.Clamp(96 + safeClientHeight / 120, 104, 118);
        var bottomReserved = 64;
        var gap = contentWidth >= 1100 ? 22 : 16;
        var contentHeight = Math.Clamp(safeClientHeight - top - bottomReserved, 390, 920);
        var minimumBandWidth = contentWidth >= 1100 ? 520 : 420;
        var minimumAccountWidth = contentWidth >= 1400 ? 420 : contentWidth >= 1100 ? 360 : 300;
        var maxAccountWidth = Math.Max(minimumAccountWidth, contentWidth - gap - minimumBandWidth);
        var defaultAccountWidth = Math.Clamp((int)(contentWidth * 0.30), minimumAccountWidth, Math.Max(minimumAccountWidth, Math.Min(680, maxAccountWidth)));
        var accountWidth = Math.Clamp(requestedAccountWidth > 0 ? requestedAccountWidth : defaultAccountWidth, minimumAccountWidth, maxAccountWidth);
        var bandWidth = Math.Max(420, contentWidth - accountWidth - gap);
        return new LauncherLayoutMetrics(margin, top, gap, contentHeight, accountWidth, bandWidth);
    }
}

internal readonly record struct BandActionButtonMetrics(int ButtonHeight, int PanelHeight, int Gap, int[] ButtonWidths, int[] RowWidths)
{
    private static readonly int[] BaseWidths = [104, 82, 88, 136, 88];

    public static BandActionButtonMetrics Calculate(int availableWidth)
    {
        var safeWidth = Math.Max(300, availableWidth);
        var scale = Math.Clamp(safeWidth / 760F, 1F, 1.16F);
        var gap = Math.Clamp((int)MathF.Round(10 * scale), 10, 12);
        var buttonHeight = Math.Clamp((int)MathF.Round(36 * scale), 36, 42);
        var widths = BaseWidths.Select(width => Math.Max(82, (int)MathF.Round(width * scale))).ToArray();
        var rowWidths = new List<int>();
        var currentRowWidth = 0;
        foreach (var width in widths)
        {
            var nextWidth = currentRowWidth == 0 ? width : currentRowWidth + gap + width;
            if (currentRowWidth > 0 && nextWidth > safeWidth)
            {
                rowWidths.Add(currentRowWidth);
                currentRowWidth = width;
            }
            else
            {
                currentRowWidth = nextWidth;
            }
        }
        if (currentRowWidth > 0) rowWidths.Add(currentRowWidth);
        var panelHeight = rowWidths.Count * buttonHeight + Math.Max(0, rowWidths.Count - 1) * 8;
        return new BandActionButtonMetrics(buttonHeight, panelHeight, gap, widths, rowWidths.ToArray());
    }
}

internal readonly record struct AccountRosterLayoutMetrics(int TileWidth, int TileHeight, int TileGap, int PortraitSize, int ColumnCount)
{
    public static AccountRosterLayoutMetrics Calculate(int clientWidth)
    {
        var usableWidth = Math.Max(64, clientWidth - SystemInformation.VerticalScrollBarWidth);
        if (usableWidth < 340)
        {
            var compactColumns = Math.Max(1, (usableWidth + 7) / (64 + 7));
            return new AccountRosterLayoutMetrics(64, 86, 7, 48, compactColumns);
        }

        var gap = usableWidth >= 560 ? 10 : 8;
        var columnsWide = usableWidth >= 560 ? 92 : 88;
        var columns = Math.Clamp(usableWidth / columnsWide, 4, 7);
        var tileWidth = Math.Clamp((usableWidth - (columns - 1) * gap) / columns, 74, 92);
        var portraitSize = Math.Clamp(tileWidth - 26, 54, 66);
        var tileHeight = Math.Clamp(tileWidth + 28, 98, 120);
        return new AccountRosterLayoutMetrics(tileWidth, tileHeight, gap, portraitSize, columns);
    }
}

internal readonly record struct BandMemberListMetrics(int BandListWidth, int MemberLeft, int MemberWidth, int MemberColumnWidth, int ColumnCount, int ListGap)
{
    public const int LeftPadding = 18;
    public const int RightPadding = 18;
    public const int MinimumColumnWidth = 220;
    private const int MinimumBandListWidth = 160;
    private const int MaximumBandListWidth = 220;
    private const int PreferredGap = 14;
    private const int MaximumColumnCount = 4;

    public static BandMemberListMetrics Calculate(int bandCardWidth)
    {
        var safeWidth = Math.Max(420, bandCardWidth);
        var bandListWidth = Math.Clamp((int)(safeWidth * 0.28), MinimumBandListWidth, MaximumBandListWidth);
        var memberLeft = LeftPadding + bandListWidth + PreferredGap;
        var memberWidth = Math.Max(MinimumColumnWidth, safeWidth - memberLeft - RightPadding);
        var columnCount = Math.Clamp(memberWidth / MinimumColumnWidth, 1, MaximumColumnCount);
        var columnWidth = Math.Max(MinimumColumnWidth, memberWidth / columnCount);
        return new BandMemberListMetrics(bandListWidth, memberLeft, memberWidth, columnWidth, columnCount, PreferredGap);
    }
}

internal readonly record struct BandChecklistLayoutMetrics(int ColumnCount, int ColumnWidth, int RowCount, int ContentWidth, int ScrollHeight)
{
    public const int RowHeight = 28;
    public const int CheckSize = 16;
    public const int MinimumColumnWidth = 220;
    public const int ColumnGap = 10;
    public const int Padding = 6;

    public static BandChecklistLayoutMetrics Calculate(int width, int itemCount)
    {
        var usableWidth = Math.Max(MinimumColumnWidth, width - Padding * 2 - SystemInformation.VerticalScrollBarWidth);
        var columnCount = Math.Max(1, (usableWidth + ColumnGap) / (MinimumColumnWidth + ColumnGap));
        var columnWidth = Math.Max(MinimumColumnWidth, (usableWidth - (columnCount - 1) * ColumnGap) / columnCount);
        var rowCount = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)columnCount);
        var contentWidth = Padding * 2 + columnCount * columnWidth + (columnCount - 1) * ColumnGap;
        var scrollHeight = Padding * 2 + rowCount * RowHeight;
        return new BandChecklistLayoutMetrics(columnCount, columnWidth, rowCount, contentWidth, scrollHeight);
    }
}

internal readonly record struct LoadingOverlayMetrics(
    Rectangle OverlayBounds,
    Rectangle CardBounds,
    Rectangle PictureBounds,
    Rectangle TitleBounds,
    Rectangle QueueBounds,
    Rectangle StatusBounds,
    Rectangle CancelBounds)
{
    public static LoadingOverlayMetrics Calculate(int bandCardWidth, int bandCardHeight, bool showQueue = false)
    {
        var safeWidth = Math.Max(420, bandCardWidth);
        var safeHeight = Math.Max(360, bandCardHeight);
        var overlay = new Rectangle(0, 0, safeWidth, safeHeight);
        var overlayWidth = overlay.Width;
        var overlayHeight = overlay.Height;

        var cardMaxWidth = Math.Max(360, overlayWidth - 48);
        var cardMaxHeight = Math.Max(160, overlayHeight - 48);

        if (showQueue)
        {
            var queueCard = new Rectangle(0, 0, overlayWidth, overlayHeight);
            var queuePictureSize = Math.Clamp(overlayHeight / 10, 44, 72);
            var queuePicture = new Rectangle((overlayWidth - queuePictureSize) / 2, 54, queuePictureSize, queuePictureSize);
            var queueTitle = new Rectangle(34, queuePicture.Bottom + 8, overlayWidth - 68, 34);
            var queueCancel = new Rectangle(Math.Max(34, (overlayWidth - 154) / 2), overlayHeight - 70, 154, 34);
            var queueStatus = new Rectangle(44, queueCancel.Top - 30, overlayWidth - 88, 24);
            var queueListTop = queueTitle.Bottom + 16;
            var queueList = new Rectangle(44, queueListTop, overlayWidth - 88, Math.Max(0, queueStatus.Top - queueListTop - 10));
            return new LoadingOverlayMetrics(overlay, queueCard, queuePicture, queueTitle, queueList, queueStatus, queueCancel);
        }

        var cardWidth = Math.Clamp((int)(overlayWidth * 0.46), Math.Min(420, cardMaxWidth), Math.Min(620, cardMaxWidth));
        var cardHeight = Math.Clamp((int)(overlayHeight * 0.72), Math.Min(240, cardMaxHeight), Math.Min(460, cardMaxHeight));
        var card = new Rectangle((overlayWidth - cardWidth) / 2, (overlayHeight - cardHeight) / 2, cardWidth, cardHeight);
        var pictureSize = Math.Clamp(cardHeight / 6, 42, 72);
        var picture = new Rectangle((cardWidth - pictureSize) / 2, Math.Max(12, cardHeight / 18), pictureSize, pictureSize);
        var titleTop = picture.Bottom + 8;
        var title = new Rectangle(28, titleTop, cardWidth - 56, 34);
        var cancel = new Rectangle(Math.Max(28, (cardWidth - 154) / 2), cardHeight - 50, 154, 36);
        var status = new Rectangle(34, cancel.Top - 34, cardWidth - 68, 26);
        var queueTop = title.Bottom + 8;
        var queue = new Rectangle(34, queueTop, cardWidth - 68, Math.Max(0, status.Top - queueTop - 8));

        return new LoadingOverlayMetrics(overlay, card, picture, title, queue, status, cancel);
    }
}

internal readonly record struct StatusPillLayoutMetrics(Rectangle Bounds, Rectangle LabelBounds)
{
    public static StatusPillLayoutMetrics Calculate(int clientWidth, int clientHeight, LauncherLayoutMetrics launcher)
    {
        const int height = 30;
        const int sideMargin = 8;
        const int bottomMargin = 10;
        var safeWidth = Math.Max(320, clientWidth);
        var safeHeight = Math.Max(120, clientHeight);
        var width = Math.Clamp(safeWidth - sideMargin * 2, 300, 370);
        var preferredX = (safeWidth - width) / 2;
        var x = Math.Clamp(preferredX, sideMargin, Math.Max(sideMargin, safeWidth - width - sideMargin));
        var preferredY = launcher.Top + launcher.ContentHeight + 20;
        var maxVisibleY = Math.Max(0, safeHeight - height - bottomMargin);
        var y = Math.Min(preferredY, maxVisibleY);
        y = Math.Max(0, y);
        var bounds = new Rectangle(x, y, width, height);
        return new StatusPillLayoutMetrics(bounds, new Rectangle(10, 4, Math.Max(40, width - 20), 20));
    }
}

internal static class SettingsMigration
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string CleanSettingsJson(string json, out bool changed)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        CleanSettings(settings);
        var cleanedJson = JsonSerializer.Serialize(settings, WriteOptions);
        changed = !JsonEquivalent(json, cleanedJson);
        return cleanedJson;
    }

    public static bool CleanSettings(AppSettings settings)
    {
        var before = JsonSerializer.Serialize(settings, WriteOptions);
        settings.DalamudFolder ??= "";
        settings.LaunchMode = NormalizeLaunchModeValue(settings.LaunchMode);
        settings.SharedProfileFolder ??= "";
        settings.Theme = string.IsNullOrWhiteSpace(settings.Theme) ? "Pink" : settings.Theme.Trim();
        settings.LaunchCooldownSeconds = Math.Clamp(settings.LaunchCooldownSeconds, 0, 300);
        settings.AccountDisplayMode = NormalizeAccountDisplayModeValue(settings.AccountDisplayMode);
        settings.LastShownChangelogVersion ??= "";
        settings.AccountIcons ??= [];
        settings.Bands ??= [];
        settings.InstancedBands ??= [];
        settings.SharedBands ??= [];
        CleanAccountIcons(settings.AccountIcons);
        CleanBands(settings.Bands);
        CleanBands(settings.InstancedBands);
        CleanBands(settings.SharedBands);
        return !JsonEquivalent(before, JsonSerializer.Serialize(settings, WriteOptions));
    }

    public static bool CleanAccountListState(AccountListState state)
    {
        var changed = false;
        changed |= CleanOrder(state.SharedAccountOrder);
        changed |= CleanOrder(state.InstancedAccountOrder);
        foreach (var key in state.LastConnectedUtc.Keys.Where(string.IsNullOrWhiteSpace).ToList())
        {
            state.LastConnectedUtc.Remove(key);
            changed = true;
        }
        return changed;
    }

    private static void CleanAccountIcons(Dictionary<string, AccountIconProfile> profiles)
    {
        var cleaned = new Dictionary<string, AccountIconProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in profiles.ToList())
        {
            var key = pair.Key?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(key)) continue;

            var profile = pair.Value ?? new AccountIconProfile();
            CleanAccountIconProfile(profile);
            if (IsEmptyAccountIconProfile(profile)) continue;

            if (!cleaned.ContainsKey(key))
            {
                cleaned[key] = profile;
            }
        }

        profiles.Clear();
        foreach (var pair in cleaned)
        {
            profiles[pair.Key] = pair.Value;
        }
    }

    private static void CleanAccountIconProfile(AccountIconProfile profile)
    {
        profile.CharacterName = (profile.CharacterName ?? "").Trim();
        profile.World = (profile.World ?? "").Trim();
        profile.LodestoneId = (profile.LodestoneId ?? "").Trim();
        profile.ProfileUrl = (profile.ProfileUrl ?? "").Trim();
        profile.IconUrl = (profile.IconUrl ?? "").Trim();
        profile.IconFileName = (profile.IconFileName ?? "").Trim();
        profile.FullImageUrl = (profile.FullImageUrl ?? "").Trim();
        profile.FullImageFileName = (profile.FullImageFileName ?? "").Trim();
    }

    private static bool IsEmptyAccountIconProfile(AccountIconProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.CharacterName) &&
            string.IsNullOrWhiteSpace(profile.World) &&
            string.IsNullOrWhiteSpace(profile.LodestoneId) &&
            string.IsNullOrWhiteSpace(profile.ProfileUrl) &&
            string.IsNullOrWhiteSpace(profile.IconUrl) &&
            string.IsNullOrWhiteSpace(profile.IconFileName) &&
            string.IsNullOrWhiteSpace(profile.FullImageUrl) &&
            string.IsNullOrWhiteSpace(profile.FullImageFileName);
    }

    private static void CleanBands(List<BandConfig> bands)
    {
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var band in bands)
        {
            var id = band.Id?.Trim() ?? "";
            if (!Guid.TryParse(id, out var parsedId) || !usedIds.Add(parsedId.ToString("N")))
            {
                id = Guid.NewGuid().ToString("N");
                usedIds.Add(id);
            }
            band.Id = id;
            band.Name = string.IsNullOrWhiteSpace(band.Name) ? "New Band" : band.Name.Trim();
            CleanOrder(band.BatchFiles);
        }
    }

    private static bool CleanOrder(List<string> order)
    {
        var cleaned = order
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (cleaned.SequenceEqual(order, StringComparer.OrdinalIgnoreCase)) return false;
        order.Clear();
        order.AddRange(cleaned);
        return true;
    }

    private static string NormalizeLaunchModeValue(string? launchMode)
    {
        return launchMode is "Shared" or "Shared XIVLauncher" ? "Shared" : "Instanced";
    }

    private static string NormalizeAccountDisplayModeValue(string? displayMode)
    {
        return displayMode is not null && (displayMode.Equals("Icons", StringComparison.OrdinalIgnoreCase) || displayMode.Equals("Roster", StringComparison.OrdinalIgnoreCase))
            ? "Roster"
            : "Text";
    }

    private static bool JsonEquivalent(string left, string right)
    {
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class AccountIconProfile
{
    public string CharacterName { get; set; } = "";
    public string World { get; set; } = "";
    public string LodestoneId { get; set; } = "";
    public string ProfileUrl { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string IconFileName { get; set; } = "";
    public string FullImageUrl { get; set; } = "";
    public string FullImageFileName { get; set; } = "";
    public DateTime LastUpdatedUtc { get; set; }
}

internal sealed class AccountListTransfer
{
    public int Version { get; set; } = 1;
    public List<AccountListTransferEntry> Accounts { get; set; } = [];
    public Dictionary<string, AccountIconProfile> AccountIcons { get; set; } = [];
    public List<string> AccountOrder { get; set; } = [];
    public Dictionary<string, DateTime> LastConnectedUtc { get; set; } = [];
}

internal sealed class AccountListTransferEntry
{
    public string UserName { get; set; } = "";
    public bool UseSteamServiceAccount { get; set; }
    public bool UseOtp { get; set; }
    public string ChosenCharacterName { get; set; } = "";
    public string ChosenCharacterWorld { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
}

internal sealed class BandTransfer
{
    public int Version { get; set; } = 1;
    public string LaunchMode { get; set; } = "Shared";
    public List<BandConfig> Bands { get; set; } = [];
}

internal enum ImportMode
{
    AppendAll,
    AppendNew,
    Merge,
    ReplaceExisting,
    OverwriteAll
}

internal sealed record ThemePalette(Color Back1, Color Back2, Color Card, Color Border, Color Text, Color Muted, Color Primary, Color Secondary, Color Danger, Color ListBack);
internal readonly record struct LauncherWindow(int ProcessId, IntPtr Handle);
internal readonly record struct GameClientWindow(int ProcessId, IntPtr Handle, string Title);
internal readonly record struct LaunchCommand(string FileName, string Arguments, string WorkingDirectory);
internal readonly record struct BatchLaunchInfo(string AccountKey, string RoamingPath);
internal readonly record struct StartedGameClient(Account Account, int ProcessId);
internal sealed record NewsBanner(string ImageUrl, string LinkUrl, string Title);
internal sealed record NewsEntry(string Title, string Url, DateTimeOffset Date, string Tag);
internal sealed record NewsBandrollSlide(Image Image, string Url, string Title);
internal sealed record LodestoneIconResult(string LodestoneId, string CharacterName, string World, string ProfileUrl, string IconUrl, string FullImageUrl);
internal sealed record LodestoneSearchCandidate(string LodestoneId, string CharacterName, string World, string ProfileUrl, string IconUrl);
internal sealed record AccountRosterItem(Account Account, string DisplayName, string? FacePath, string? FullPath, string Tooltip);
internal sealed class AccountContextEventArgs(Account account, Point location) : EventArgs
{
    public Account Account { get; } = account;
    public Point Location { get; } = location;
}

internal sealed class AccountReorderEventArgs(Account account, int targetIndex) : EventArgs
{
    public Account Account { get; } = account;
    public int TargetIndex { get; } = targetIndex;
}

internal sealed class MainForm : Form
{
    private const string GitHubOwner = "Naru6780";
    private const string GitHubRepo = "potato-launcher";
    private const string ReleaseZipName = "PotatoLauncher.zip";
    private const string ReleaseExeName = "Potato Launcher.exe";
    private static readonly HttpClient LodestoneClient = CreateLodestoneClient();

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, TcpTableClass tblClass, uint reserved = 0);

    private const int AfInet = 2;
    private const uint TcpStateEstablished = 5;
    private const int WmSetRedraw = 0x000B;

    private enum TcpTableClass
    {
        OwnerPidAll = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    internal static readonly Dictionary<string, ThemePalette> Palettes = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly HashSet<string> DefaultThemeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pink",
        "Fuchsia",
        "Dark",
        "Sky"
    };

    private readonly AppSettings settings = LoadSettings();
    private readonly AccountListState accountState = LoadAccountListState();
    private readonly List<Account> accounts = [];
    private CuteBackgroundPanel background = null!;
    private RoundedPanel accountCard = null!;
    private RoundedPanel bandCard = null!;
    private RoundedPanel settingsDrawer = null!;
    private readonly System.Windows.Forms.Timer settingsDrawerTimer = new();
    private ListBox accountList = null!;
    private AccountRosterGrid accountRosterGrid = null!;
    private Panel accountResizeHandle = null!;
    private ListBox bandList = null!;
    private BandMemberChecklist memberList = null!;
    private Label folderLabel = null!;
    private Label sharedProfileLabel = null!;
    private Label themeLabel = null!;
    private TextBox folderInput = null!;
    private TextBox sharedProfileInput = null!;
    private ComboBox launchModeInput = null!;
    private ComboBox themeInput = null!;
    private Button browseBatButton = null!;
    private Button browseSharedProfileButton = null!;
    private Button updateButton = null!;
    private Label accountDisplayLabel = null!;
    private ComboBox accountDisplayInput = null!;
    private Label launchCooldownLabel = null!;
    private NumericUpDown launchCooldownInput = null!;
    private CheckBox waitForClientInitializationInput = null!;
    private CheckBox randomizeThemeInput = null!;
    private CheckBox notificationsEnabledInput = null!;
    private Button settingsButton = null!;
    private Button killGameButton = null!;
    private Button multibandButton = null!;
    private Button whatsNewButton = null!;
    private Button optimizerButton = null!;
    private Button helpButton = null!;
    private AppToolTip? appToolTip;
    private MascotOverlayForm? mascotOverlay;
    private ArtemisDesktopPetForm? artemisDesktopPet;
    private RoundedPanel statusPill = null!;
    private Label status = null!;
    private Button launchBandButton = null!;
    private Button cancelButton = null!;
    private Button newBandButton = null!;
    private Button saveBandsButton = null!;
    private Button deleteBandButton = null!;
    private FlowLayoutPanel bandButtonPanel = null!;
    private Button importAccountsButton = null!;
    private Button exportAccountsButton = null!;
    private Button importBandsButton = null!;
    private Button exportBandsButton = null!;
    private LoadingOverlayPanel loadingOverlay = null!;
    private CuteBackgroundPanel launchChoiceOverlay = null!;
    private RoundedPanel loadingCard = null!;
    private RoundedPanel launchChoiceCard = null!;
    private PictureBox loadingPicture = null!;
    private Label loadingTitle = null!;
    private Panel loadingQueuePanel = null!;
    private Label loadingStatus = null!;
    private Button loadingCancel = null!;
    private RoundedPanel newsOverlay = null!;
    private NewsBandrollControl newsBandroll = null!;
    private PictureBox newsBannerPicture = null!;
    private Label newsBannerTitle = null!;
    private NewsDotsControl newsDots = null!;
    private FlowLayoutPanel newsListPanel = null!;
    private Button newsCloseButton = null!;
    private ElementHost videoHost = null!;
    private WpfMediaElement backgroundVideo = null!;
    private WpfImage wpfMascot = null!;
    private readonly DispatcherTimer mascotTimer = new();
    private readonly List<BitmapSource> mascotFrames = [];
    private readonly List<int> mascotFrameDelays = [];
    private readonly Dictionary<int, Label> loadingQueueLabels = [];
    private readonly Dictionary<string, int> runningClientProcessIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly IntegratedOptimizerService optimizerService = new(OptimizerSettings.Load());
    private OptimizerMonitorForm? optimizerMonitor;
    private MultibandSettingsStore multibandSettingsStore = null!;
    private MultibandSettings multibandSettings = null!;
    private MultibandServer multibandServer = null!;
    private MultibandClient multibandClient = null!;
    private MultibandForm? multibandForm;
    private ThemePalette palette = Palettes["Pink"];
    private CancellationTokenSource? queueCancel;
    private bool loadingQueueActive;
    private bool loadingBand;
    private bool themeHasVideo;
    private bool themeHasImage;
    private bool settingsDrawerOpen;
    private readonly List<NewsBanner> newsBanners = [];
    private readonly List<NewsEntry> newsEntries = [];
    private int selectedNewsBannerIndex;
    private int accountDragIndex = -1;
    private Point accountDragStart;
    private bool resizingAccountPanel;
    private int accountResizeStartX;
    private int accountResizeStartWidth;
    private int accountResizeMinWidth;
    private int accountResizeMaxWidth;
    private int pendingAccountPanelWidth;
    private bool accountResizeFrameQueued;
    private bool accountResizeListsSuspended;
    private readonly TextUpdateGate statusUpdateGate = new(TimeSpan.FromMilliseconds(750));
    private readonly TextUpdateGate loadingStatusUpdateGate = new(TimeSpan.FromMilliseconds(250));
    private DateTime lastSaveNotificationUtc = DateTime.MinValue;

    public MainForm()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
        EnsureThemeAssetFolders();
        RandomizeThemeForLaunch();
        RemoveOldDefaultBands();
        LoadAccounts();
        BuildUi();
        InitializeMultiband();
        MigrateLegacyBands();
        PopulateLists();
        ApplyTheme(settings.Theme);
        Shown += async (_, _) =>
        {
            await ShowChangelogIfNewVersionAsync();
            await LoadNewsBandrollAsync();
            await RefreshLinkedAccountIconsOnStartupAsync();
        };
        if (!settings.LaunchModeChosen) Shown += (_, _) => ShowLaunchChoiceOverlay();
    }

    private void BuildUi()
    {
        Text = AppText.WindowTitle(Application.ProductVersion);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(990, 700);
        MinimumSize = new Size(860, 620);
        Font = new Font("Segoe UI", 10F);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        background = new CuteBackgroundPanel { Dock = DockStyle.Fill, AnimateBubbles = true };
        Controls.Add(background);
        BuildVideoBackground();

        settingsButton = Button("Settings", 36, 24, 102, 34, "Secondary");
        settingsButton.Click += (_, _) => ToggleSettingsDrawer();
        background.Controls.Add(settingsButton);
        killGameButton = Button("Kill FFXIV", 154, 24, 104, 34, "Danger");
        killGameButton.Click += (_, _) => KillGameInstances();
        background.Controls.Add(killGameButton);
        multibandButton = Button("Multiband", 272, 24, 110, 34, "Secondary");
        multibandButton.Click += (_, _) => ShowMultibandWindow();
        background.Controls.Add(multibandButton);
        whatsNewButton = NewsPillButton(396, 24, 68, 34);
        whatsNewButton.Click += async (_, _) => await ShowNewsOverlayAsync();
        background.Controls.Add(whatsNewButton);
        optimizerButton = Button("Optimizer", 478, 24, 96, 34, "Primary");
        optimizerButton.Click += (_, _) => ShowOptimizerMonitor();
        background.Controls.Add(optimizerButton);
        helpButton = Button("?", 588, 24, 34, 34, "Secondary");
        appToolTip = new AppToolTip(this);
        appToolTip.Attach(helpButton, "Help", "Open the Potato Launcher feature guide.");
        appToolTip.Attach(optimizerButton, "Optimizer", "Monitor FFXIV clients and manage CPU, affinity, and memory optimization.");
        appToolTip.Attach(multibandButton, "Multiband", "Pair another PC and launch one band on each PC together.");
        helpButton.Click += (_, _) => ShowHelpWindow();
        background.Controls.Add(helpButton);
        newsBandroll = new NewsBandrollControl { Bounds = new Rectangle(636, 24, 304, 34), Visible = false };
        newsBandroll.ItemClicked += (_, url) => OpenUrl(url);
        background.Controls.Add(newsBandroll);
        mascotOverlay = CreateMascotOverlay();
        artemisDesktopPet = ArtemisDesktopPetForm.TryCreate();
        if (artemisDesktopPet is not null)
        {
            artemisDesktopPet.RestoreRequested += (_, _) => RestoreFromDesktopPet();
        }
        Shown += (_, _) =>
        {
            UpdateMascotOverlay();
            UpdateDesktopPetVisibility();
        };
        Move += (_, _) => UpdateMascotOverlay();
        Resize += (_, _) =>
        {
            ApplyResponsiveLayout();
            UpdateMascotOverlay();
            UpdateDesktopPetVisibility();
        };
        Activated += (_, _) => UpdateMascotOverlay();
        FormClosed += async (_, _) =>
        {
            multibandForm?.Close();
            if (multibandServer is not null) await multibandServer.DisposeAsync();
            optimizerMonitor?.Close();
            optimizerService.Dispose();
            appToolTip?.Dispose();
            mascotOverlay?.Close();
            artemisDesktopPet?.Close();
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
        ApplyResponsiveLayout();
        settingsDrawer.BringToFront();
        ConfigureSettingsDrawerAnimation();
        EnableSmoothRendering(this);
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

    private void UpdateDesktopPetVisibility()
    {
        if (artemisDesktopPet is null || artemisDesktopPet.IsDisposed) return;
        if (WindowState == FormWindowState.Minimized)
        {
            artemisDesktopPet.ShowNear(Screen.FromControl(this).WorkingArea);
            return;
        }
        artemisDesktopPet.Hide();
    }

    private void RestoreFromDesktopPet()
    {
        if (IsDisposed) return;
        WindowState = FormWindowState.Normal;
        Show();
        Activate();
        BringToFront();
        artemisDesktopPet?.Hide();
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
        accountList = new ListBox { Bounds = new Rectangle(18, 58, 294, 320), AllowDrop = true };
        accountList.DoubleClick += (_, _) => LaunchSelectedAccount();
        accountList.MouseDown += (_, e) =>
        {
            var index = accountList.IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                accountDragIndex = index;
                accountDragStart = e.Location;
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                if (index < 0 || index >= accountList.Items.Count) return;
                accountList.SelectedIndex = index;
                if (accountList.Items[index] is Account account)
                {
                    ShowAccountContextMenu(account, accountList, e.Location);
                }
            }
        };
        accountList.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || accountDragIndex < 0 || accountDragIndex >= accountList.Items.Count) return;
            if (!IsDragGesture(accountDragStart, e.Location)) return;
            if (accountList.Items[accountDragIndex] is Account account)
            {
                accountList.DoDragDrop(account, DragDropEffects.Move);
            }
            accountDragIndex = -1;
        };
        accountList.DragOver += (_, e) => e.Effect = e.Data?.GetDataPresent(typeof(Account)) == true ? DragDropEffects.Move : DragDropEffects.None;
        accountList.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(typeof(Account)) is not Account account) return;
            var point = accountList.PointToClient(new Point(e.X, e.Y));
            ReorderAccount(account, DropIndexFromListBox(accountList, point));
        };
        accountCard.Controls.Add(accountList);
        accountRosterGrid = new AccountRosterGrid { Bounds = new Rectangle(18, 58, 294, 320), Visible = false };
        accountRosterGrid.AccountActivated += (_, _) => LaunchSelectedAccount();
        accountRosterGrid.AccountContextRequested += (_, args) => ShowAccountContextMenu(args.Account, accountRosterGrid, args.Location);
        accountRosterGrid.AccountReordered += (_, args) => ReorderAccount(args.Account, args.TargetIndex);
        accountCard.Controls.Add(accountRosterGrid);

        bandCard = Card(392, 118, 560, 450);
        bandCard.Controls.Add(Header("Band Manager", 18, 12, 180, 32));
        var initialBandMembers = BandMemberListMetrics.Calculate(bandCard.Width);
        bandList = new ListBox { Bounds = new Rectangle(BandMemberListMetrics.LeftPadding, 58, initialBandMembers.BandListWidth, 306) };
        bandList.SelectedIndexChanged += (_, _) => LoadSelectedBand();
        bandList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = bandList.IndexFromPoint(e.Location);
            if (index < 0 || index >= bandList.Items.Count) return;
            bandList.SelectedIndex = index;
            ShowBandContextMenu(bandList, e.Location);
        };
        bandCard.Controls.Add(bandList);
        memberList = new BandMemberChecklist
        {
            Bounds = new Rectangle(initialBandMembers.MemberLeft, 58, initialBandMembers.MemberWidth, 306),
            Palette = palette
        };
        memberList.CheckedChanged += (_, _) => { if (!loadingBand) BeginInvoke(() => SaveCurrentBand()); };
        bandCard.Controls.Add(memberList);

        newBandButton = Button("Add Band", 18, 384, 104, 36, "Secondary");
        newBandButton.Click += (_, _) => AddBand();
        saveBandsButton = Button("Save", 132, 384, 76, 36, "Secondary");
        saveBandsButton.Click += (_, _) => SaveBandsToDefault();
        deleteBandButton = Button("Delete", 218, 384, 76, 36, "Danger");
        deleteBandButton.Click += (_, _) => DeleteBand();
        launchBandButton = Button("Launch band", 304, 384, 136, 36, "Primary");
        launchBandButton.Click += async (_, _) => await LaunchSelectedBandAsync();
        cancelButton = Button("Cancel", 450, 384, 74, 36, "Danger");
        cancelButton.Visible = false;
        cancelButton.Click += (_, _) => queueCancel?.Cancel();
        bandButtonPanel = new BufferedFlowLayoutPanel
        {
            Bounds = new Rectangle(18, 384, 520, 48),
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        foreach (var button in new[] { newBandButton, saveBandsButton, deleteBandButton, launchBandButton, cancelButton })
        {
            button.Margin = new Padding(0, 0, 10, 8);
            bandButtonPanel.Controls.Add(button);
        }
        bandCard.Controls.Add(bandButtonPanel);
        tab.Controls.Add(accountCard);
        tab.Controls.Add(bandCard);
        accountResizeHandle = new Panel { Cursor = Cursors.VSplit, BackColor = Color.Transparent };
        accountResizeHandle.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            resizingAccountPanel = true;
            accountResizeStartX = Cursor.Position.X;
            accountResizeStartWidth = accountCard.Width;
            accountResizeMinWidth = 300;
            accountResizeMaxWidth = LauncherLayoutMetrics.Calculate(ClientSize.Width, ClientSize.Height, int.MaxValue).AccountWidth;
            pendingAccountPanelWidth = accountResizeStartWidth;
            accountResizeFrameQueued = false;
            BeginAccountResizeInteraction();
            accountResizeHandle.Capture = true;
        };
        accountResizeHandle.MouseMove += (_, _) =>
        {
            if (!resizingAccountPanel) return;
            QueueAccountPanelResize(Cursor.Position.X);
        };
        accountResizeHandle.MouseUp += (_, _) =>
        {
            if (!resizingAccountPanel) return;
            ApplyQueuedAccountPanelResize();
            resizingAccountPanel = false;
            accountResizeHandle.Capture = false;
            EndAccountResizeInteraction();
            SaveSettings(settings);
        };
        tab.Controls.Add(accountResizeHandle);
    }

    private void ApplyTopNavigationLayout(LauncherLayoutMetrics layout)
    {
        var scale = Math.Clamp(ClientSize.Width / 1100F, 1F, 1.16F);
        var buttonHeight = Math.Clamp((int)MathF.Round(34 * scale), 34, 40);
        var gap = Math.Clamp((int)MathF.Round(12 * scale), 12, 16);
        var y = Math.Clamp(ClientSize.Height / 32, 20, 28);
        var x = Math.Max(24, layout.Margin);
        var buttons = new (Button Button, int BaseWidth)[]
        {
            (settingsButton, 102),
            (killGameButton, 104),
            (multibandButton, 110),
            (whatsNewButton, 68),
            (optimizerButton, 96),
            (helpButton, 34)
        };

        foreach (var (button, baseWidth) in buttons)
        {
            var width = ReferenceEquals(button, helpButton)
                ? buttonHeight
                : Math.Clamp((int)MathF.Round(baseWidth * scale), baseWidth, baseWidth + 26);
            button.Bounds = new Rectangle(x, y, width, buttonHeight);
            x += width + gap;
        }

        if (newsBandroll is not null)
        {
            var bandroll = TopNavigationBandrollMetrics.Calculate(ClientSize.Width, ClientSize.Height, x, buttonHeight, y, layout.Margin);
            newsBandroll.Bounds = bandroll.Bounds;
            newsBandroll.Visible = bandroll.Visible && newsBandroll.HasSlides;
        }
    }

    private void ApplyBandActionButtonLayout(BandActionButtonMetrics layout)
    {
        var buttons = new[] { newBandButton, saveBandsButton, deleteBandButton, launchBandButton, cancelButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            buttons[index].Size = new Size(layout.ButtonWidths[index], layout.ButtonHeight);
            buttons[index].Margin = new Padding(0, 0, layout.Gap, 8);
        }
    }

    private void ApplyLauncherLayout(bool forceRepaint = false)
    {
        if (accountCard is null || bandCard is null || statusPill is null) return;

        var oldPanelBounds = Rectangle.Union(accountCard.Bounds, bandCard.Bounds);
        var layout = LauncherLayoutMetrics.Calculate(ClientSize.Width, ClientSize.Height, settings.AccountPanelWidth);

        using var redraw = forceRepaint ? null : BeginRedrawScope(background);
        background.SuspendLayout();
        accountCard.SuspendLayout();
        bandCard.SuspendLayout();
        ApplyTopNavigationLayout(layout);
        accountCard.SetBounds(layout.Margin, layout.Top, layout.AccountWidth, layout.ContentHeight);
        bandCard.SetBounds(layout.Margin + layout.AccountWidth + layout.Gap, layout.Top, layout.BandWidth, layout.ContentHeight);
        if (accountResizeHandle is not null)
        {
            accountResizeHandle.SetBounds(accountCard.Right + 3, layout.Top + 8, Math.Max(8, layout.Gap - 6), layout.ContentHeight - 16);
            accountResizeHandle.BringToFront();
        }

        accountList.Bounds = new Rectangle(18, 58, accountCard.Width - 36, accountCard.Height - 82);
        accountRosterGrid.Bounds = accountList.Bounds;

        var actionLayout = BandActionButtonMetrics.Calculate(bandCard.Width - 36);
        ApplyBandActionButtonLayout(actionLayout);
        var buttonPanelTop = bandCard.Height - 18 - actionLayout.PanelHeight;
        bandButtonPanel.Bounds = new Rectangle(18, buttonPanelTop, bandCard.Width - 36, actionLayout.PanelHeight);

        var memberLayout = BandMemberListMetrics.Calculate(bandCard.Width);
        var listHeight = Math.Max(190, buttonPanelTop - 70);
        bandList.Bounds = new Rectangle(BandMemberListMetrics.LeftPadding, 58, memberLayout.BandListWidth, listHeight);
        memberList.Bounds = new Rectangle(memberLayout.MemberLeft, 58, memberLayout.MemberWidth, listHeight);
        if (loadingOverlay is not null)
        {
            ApplyLoadingOverlayLayout();
        }

        var statusLayout = StatusPillLayoutMetrics.Calculate(ClientSize.Width, ClientSize.Height, layout);
        statusPill.Bounds = statusLayout.Bounds;
        status.Bounds = statusLayout.LabelBounds;

        bandCard.ResumeLayout(false);
        accountCard.ResumeLayout(false);
        background.ResumeLayout(false);
        if (forceRepaint)
        {
            var dirty = Rectangle.Union(oldPanelBounds, Rectangle.Union(accountCard.Bounds, bandCard.Bounds));
            dirty.Inflate(32, 32);
            background.Invalidate(dirty, false);
            accountCard.Invalidate(false);
            bandCard.Invalidate(false);
            accountResizeHandle?.Invalidate();
        }
    }

    private void QueueAccountPanelResize(int screenX)
    {
        pendingAccountPanelWidth = Math.Clamp(accountResizeStartWidth + screenX - accountResizeStartX, accountResizeMinWidth, accountResizeMaxWidth);
        if (accountResizeFrameQueued) return;
        accountResizeFrameQueued = true;
        BeginInvoke(new Action(ApplyQueuedAccountPanelResize));
    }

    private void ApplyQueuedAccountPanelResize()
    {
        if (!accountResizeFrameQueued && pendingAccountPanelWidth == settings.AccountPanelWidth) return;
        accountResizeFrameQueued = false;
        if (pendingAccountPanelWidth == settings.AccountPanelWidth) return;
        settings.AccountPanelWidth = pendingAccountPanelWidth;
        ApplyLauncherLayout(forceRepaint: true);
    }

    private void BeginAccountResizeInteraction()
    {
        if (accountResizeListsSuspended) return;
        accountResizeListsSuspended = true;
        accountList.BeginUpdate();
        bandList.BeginUpdate();
        memberList.BeginUpdate();
    }

    private void EndAccountResizeInteraction()
    {
        if (!accountResizeListsSuspended) return;
        accountResizeListsSuspended = false;
        memberList.EndUpdate();
        bandList.EndUpdate();
        accountList.EndUpdate();
        accountRosterGrid.Invalidate();
        memberList.Invalidate();
        bandList.Invalidate();
        accountList.Invalidate();
    }

    private void ApplyResponsiveLayout()
    {
        if (background is null || WindowState == FormWindowState.Minimized || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        ApplyLauncherLayout();

        if (settingsDrawer is not null)
        {
            settingsDrawer.Height = ClientSize.Height;
            settingsDrawer.Left = settingsDrawerOpen ? ClientSize.Width - settingsDrawer.Width : ClientSize.Width + 4;
            settingsDrawer.Invalidate();
        }

        if (loadingCard is not null) loadingCard.Invalidate();

        if (launchChoiceOverlay is not null)
        {
            launchChoiceOverlay.Bounds = ClientRectangle;
        }
        if (launchChoiceCard is not null)
        {
            launchChoiceCard.Location = new Point(Math.Max(24, (ClientSize.Width - launchChoiceCard.Width) / 2), Math.Max(54, (ClientSize.Height - launchChoiceCard.Height) / 2));
            launchChoiceCard.Invalidate();
        }

        if (newsOverlay is not null)
        {
            var width = Math.Min(800, Math.Max(640, ClientSize.Width - 96));
            var height = Math.Min(590, Math.Max(500, ClientSize.Height - 92));
            newsOverlay.Bounds = new Rectangle(Math.Max(24, (ClientSize.Width - width) / 2), Math.Max(34, (ClientSize.Height - height) / 2), width, height);
            newsCloseButton.Location = new Point(newsOverlay.Width - 56, 20);
            newsBannerPicture.Bounds = new Rectangle(28, 68, newsOverlay.Width - 56, Math.Min(250, Math.Max(190, newsOverlay.Height - 340)));
            newsBannerTitle.Bounds = new Rectangle(28, newsBannerPicture.Bottom + 6, newsOverlay.Width - 56, 24);
            newsDots.Bounds = new Rectangle(Math.Max(28, (newsOverlay.Width - 200) / 2), newsBannerTitle.Bottom + 4, 200, 28);
            newsListPanel.Bounds = new Rectangle(28, newsDots.Bottom + 10, newsOverlay.Width - 56, Math.Max(100, newsOverlay.Height - newsDots.Bottom - 30));
            newsOverlay.Invalidate();
        }
    }

    private void BuildSettingsDrawer()
    {
        settingsDrawer = new RoundedPanel { Bounds = new Rectangle(ClientSize.Width + 4, 0, 380, ClientSize.Height), Radius = 24, AutoScroll = true };
        settingsDrawer.Controls.Add(Header("Settings", 24, 24, 180, 38));

        settingsDrawer.Controls.Add(Label("Launch method", 24, 76, 170, 24));
        launchModeInput = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(24, 104, 220, 29) };
        launchModeInput.Items.AddRange(["Instanced", "Shared"]);
        launchModeInput.SelectedItem = IsSharedLaunchMode() ? "Shared" : "Instanced";
        launchModeInput.SelectedIndexChanged += (_, _) =>
        {
            SaveSettingsFromInputs(showFeedback: true);
            UpdateLaunchModeUi();
            LoadAccounts();
            PopulateLists();
        };
        settingsDrawer.Controls.Add(launchModeInput);

        folderLabel = Label("Instanced folder", 24, 146, 220, 24);
        folderInput = new TextBox { Text = settings.DalamudFolder, Bounds = new Rectangle(24, 174, 332, 29) };
        folderInput.Leave += (_, _) => SaveAndRescan(showFeedback: true);
        browseBatButton = Button("Browse", 24, 212, 96, 32, "Secondary");
        browseBatButton.Click += (_, _) => BrowseFolder(folderInput, "Choose the folder containing XIVLauncher .bat files", () => SaveAndRescan(showFeedback: true));
        settingsDrawer.Controls.AddRange([folderLabel, folderInput, browseBatButton]);

        sharedProfileLabel = Label("Shared folder", 24, 146, 260, 24);
        sharedProfileInput = new TextBox { Text = settings.SharedProfileFolder, Bounds = new Rectangle(24, 174, 332, 29) };
        sharedProfileInput.Leave += (_, _) => SaveAndRescan(showFeedback: true);
        browseSharedProfileButton = Button("Browse", 24, 212, 96, 32, "Secondary");
        browseSharedProfileButton.Click += (_, _) => BrowseFolder(sharedProfileInput, "Choose the folder containing accountsList.json", () => SaveAndRescan(showFeedback: true));
        settingsDrawer.Controls.AddRange([sharedProfileLabel, sharedProfileInput, browseSharedProfileButton]);

        accountDisplayLabel = Label("Account list display", 24, 270, 220, 24);
        accountDisplayInput = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(24, 298, 160, 29) };
        accountDisplayInput.Items.AddRange(["Text", "Roster"]);
        accountDisplayInput.SelectedItem = NormalizeAccountDisplayMode(settings.AccountDisplayMode);
        accountDisplayInput.SelectedIndexChanged += (_, _) =>
        {
            SaveSettingsFromInputs(showFeedback: true);
            UpdateAccountDisplayMode();
        };
        settingsDrawer.Controls.AddRange([accountDisplayLabel, accountDisplayInput]);

        exportAccountsButton = Button("Export accounts", 24, 336, 154, 30, "Secondary");
        exportAccountsButton.Click += (_, _) => ExportAccountList();
        importAccountsButton = Button("Import accounts", 190, 336, 154, 30, "Secondary");
        importAccountsButton.Click += (_, _) => ImportAccountList();
        exportBandsButton = Button("Export bands", 24, 374, 154, 30, "Secondary");
        exportBandsButton.Click += (_, _) => ExportBandsAs();
        importBandsButton = Button("Import bands", 190, 374, 154, 30, "Secondary");
        importBandsButton.Click += (_, _) => ImportBands();
        settingsDrawer.Controls.AddRange([exportAccountsButton, importAccountsButton, exportBandsButton, importBandsButton]);

        themeLabel = Label("Theme", 24, 580, 120, 24);
        themeInput = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(24, 608, 260, 29) };
        themeInput.Items.AddRange(Palettes.Keys.ToArray());
        themeInput.SelectedItem = Palettes.ContainsKey(NormalizeThemeName(settings.Theme)) ? NormalizeThemeName(settings.Theme) : "Pink";
        themeInput.SelectedIndexChanged += (_, _) => { SaveSettingsFromInputs(showFeedback: true); ApplyTheme(settings.Theme); };
        settingsDrawer.Controls.AddRange([themeLabel, themeInput]);

        launchCooldownLabel = Label("Launch cooldown: seconds between clients", 24, 488, 300, 24);
        settingsDrawer.Controls.Add(launchCooldownLabel);
        launchCooldownInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 300,
            Value = Math.Clamp(settings.LaunchCooldownSeconds, 0, 300),
            Bounds = new Rectangle(24, 522, 96, 29)
        };
        launchCooldownInput.ValueChanged += (_, _) => SaveSettingsFromInputs(showFeedback: true);
        settingsDrawer.Controls.Add(launchCooldownInput);

        waitForClientInitializationInput = new CheckBox { Text = "Wait until client is initialized before launching next", Checked = settings.WaitForClientInitializationBeforeNextLaunch, Bounds = new Rectangle(24, 560, 332, 28), BackColor = Color.Transparent };
        waitForClientInitializationInput.CheckedChanged += (_, _) => SaveSettingsFromInputs(showFeedback: true);
        settingsDrawer.Controls.Add(waitForClientInitializationInput);

        randomizeThemeInput = new CheckBox { Text = "Randomize theme at launch", Checked = settings.RandomizeThemeAtLaunch, Bounds = new Rectangle(24, 594, 250, 28), BackColor = Color.Transparent };
        randomizeThemeInput.CheckedChanged += (_, _) => SaveSettingsFromInputs(showFeedback: true);
        settingsDrawer.Controls.Add(randomizeThemeInput);

        notificationsEnabledInput = new CheckBox { Text = "Enable notifications", Checked = settings.NotificationsEnabled, Bounds = new Rectangle(24, 628, 250, 28), BackColor = Color.Transparent };
        notificationsEnabledInput.CheckedChanged += (_, _) => SaveSettingsFromInputs(showFeedback: true);
        settingsDrawer.Controls.Add(notificationsEnabledInput);

        updateButton = Button("Check for updates", 24, 660, 180, 34, "Secondary");
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
            SetY(accountDisplayLabel, 270);
            SetY(accountDisplayInput, 298);
            SetY(exportAccountsButton, 336);
            SetY(importAccountsButton, 336);
            SetY(exportBandsButton, 374);
            SetY(importBandsButton, 374);
            SetY(themeLabel, 420);
            SetY(themeInput, 448);
            SetY(launchCooldownLabel, 488);
            SetY(launchCooldownInput, 522);
            SetY(waitForClientInitializationInput, 560);
            SetY(randomizeThemeInput, 594);
            SetY(notificationsEnabledInput, 628);
            SetY(updateButton, 660);
        }
        else
        {
            SetY(accountDisplayLabel, 270);
            SetY(accountDisplayInput, 298);
            SetY(exportAccountsButton, 336);
            SetY(importAccountsButton, 336);
            SetY(exportBandsButton, 374);
            SetY(importBandsButton, 374);
            SetY(themeLabel, 420);
            SetY(themeInput, 448);
            SetY(launchCooldownLabel, 488);
            SetY(launchCooldownInput, 522);
            SetY(waitForClientInitializationInput, 560);
            SetY(randomizeThemeInput, 594);
            SetY(notificationsEnabledInput, 628);
            SetY(updateButton, 660);
        }
    }

    private static void SetY(Control control, int y) => control.Bounds = new Rectangle(control.Left, y, control.Width, control.Height);

    private void BuildLoadingOverlay()
    {
        var loadingLayout = LoadingOverlayMetrics.Calculate(520, 426);
        loadingOverlay = new LoadingOverlayPanel { Bounds = loadingLayout.OverlayBounds, Visible = false, TabStop = true };
        loadingCard = new RoundedPanel { Bounds = loadingLayout.CardBounds, Radius = 24 };

        loadingPicture = new PictureBox
        {
            Bounds = loadingLayout.PictureBounds,
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        loadingTitle = new Label
        {
            Text = "Now loading...",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            Bounds = loadingLayout.TitleBounds
        };
        loadingQueuePanel = new Panel
        {
            Bounds = loadingLayout.QueueBounds,
            BackColor = Color.Transparent,
            AutoScroll = true,
            Visible = false
        };
        loadingStatus = new Label
        {
            Text = "",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Bounds = loadingLayout.StatusBounds
        };
        loadingCancel = Button("Cancel", loadingLayout.CancelBounds.X, loadingLayout.CancelBounds.Y, loadingLayout.CancelBounds.Width, loadingLayout.CancelBounds.Height, "Danger");
        loadingCancel.Click += (_, _) => queueCancel?.Cancel();

        loadingCard.Controls.AddRange([loadingPicture, loadingTitle, loadingQueuePanel, loadingStatus, loadingCancel]);
        loadingOverlay.Controls.Add(loadingCard);
        bandCard.Controls.Add(loadingOverlay);
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
        launchChoiceCard = new RoundedPanel { Bounds = new Rectangle(155, 118, 680, 430), Radius = 30 };
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
        launchChoiceCard.Controls.AddRange([title, subtitle, mascot, instanced, shared]);
        launchChoiceOverlay.Controls.Add(launchChoiceCard);
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
                var chosenCharacterName = GetJsonString(entry.Element, "ChosenCharacterName");
                var chosenCharacterWorld = GetJsonString(entry.Element, "ChosenCharacterWorld");
                MergeSharedAccountIconMetadata(
                    accountKey,
                    chosenCharacterName,
                    chosenCharacterWorld,
                    GetJsonString(entry.Element, "ThumbnailUrl"));
                var characterName = settings.AccountIcons.TryGetValue(accountKey, out var profile) && !string.IsNullOrWhiteSpace(profile.CharacterName)
                    ? profile.CharacterName
                    : chosenCharacterName;
                var displayName = string.IsNullOrWhiteSpace(characterName) ? userName : $"{userName} - {characterName}";
                var key = accountKey;
                var order = 999;
                if (batLookup.TryGetValue(accountKey, out var batAccount))
                {
                    if (string.IsNullOrWhiteSpace(characterName))
                    {
                        displayName = $"{userName} - {batAccount.Name}";
                    }
                    key = batAccount.BatchFile;
                    order = batAccount.SortOrder;
                }

                accounts.Add(new Account(displayName, key, order, accountKey, useSteam, useOtp));
            }
        }
        catch { }
    }

    private void MergeSharedAccountIconMetadata(string accountKey, string characterName, string world, string thumbnailUrl)
    {
        if (string.IsNullOrWhiteSpace(accountKey)) return;
        if (string.IsNullOrWhiteSpace(characterName) && string.IsNullOrWhiteSpace(world) && string.IsNullOrWhiteSpace(thumbnailUrl)) return;

        if (!settings.AccountIcons.TryGetValue(accountKey, out var profile))
        {
            profile = new AccountIconProfile();
            settings.AccountIcons[accountKey] = profile;
        }

        if (string.IsNullOrWhiteSpace(profile.CharacterName) && !string.IsNullOrWhiteSpace(characterName))
        {
            profile.CharacterName = characterName.Trim();
        }

        if (string.IsNullOrWhiteSpace(profile.World) && !string.IsNullOrWhiteSpace(world))
        {
            profile.World = world.Trim();
        }

        if (string.IsNullOrWhiteSpace(profile.IconUrl) && !string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            profile.IconUrl = thumbnailUrl.Trim();
        }
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
                var setMatch = Regex.Match(line, @"^set\s+([^=]+)=(.*)$", RegexOptions.IgnoreCase);
                if (setMatch.Success)
                {
                    variables[setMatch.Groups[1].Value.Trim()] = setMatch.Groups[2].Value.Trim();
                    continue;
                }

                var expanded = ExpandBatchVariables(line, variables);
                var accountMatch = Regex.Match(expanded, @"-{1,2}account(?:=|\s+)(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase);
                if (accountMatch.Success)
                {
                    accountKey = (accountMatch.Groups[1].Success ? accountMatch.Groups[1].Value : accountMatch.Groups[2].Value).Trim();
                }
                var roamingMatch = Regex.Match(expanded, @"-{1,2}roamingPath(?:=|\s+)(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase);
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
        return Regex.Replace(text, "%([^%]+)%", match =>
            variables.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    private void PopulateLists()
    {
        using var redraw = BeginRedrawScope(background);
        accountCard.SuspendLayout();
        bandCard.SuspendLayout();
        accountList.BeginUpdate();
        bandList.BeginUpdate();
        try
        {
            accountList.Items.Clear();
            var orderedAccountList = OrderedAccounts().ToList();
            foreach (var account in orderedAccountList)
            {
                accountList.Items.Add(account);
            }
            accountRosterGrid.SetItems(orderedAccountList.Select(CreateRosterItem));
            bandList.Items.Clear();
            foreach (var band in CurrentBands())
            {
                NormalizeBand(band);
                bandList.Items.Add(band);
            }
            bandList.SelectedIndex = bandList.Items.Count > 0 ? 0 : -1;
            if (bandList.SelectedItem is not BandConfig)
            {
                PopulateMemberList(null);
            }
            UpdateAccountStatus();
            UpdateAccountDisplayMode();
        }
        finally
        {
            bandList.EndUpdate();
            accountList.EndUpdate();
            bandCard.ResumeLayout(false);
            accountCard.ResumeLayout(false);
        }
    }

    private IEnumerable<Account> OrderedAccounts()
    {
        var accountList = IsSharedLaunchMode()
            ? accounts.ToList()
            : accounts.OrderBy(account => account.SortOrder).ThenBy(account => account.Name).ToList();
        var savedOrder = CurrentAccountOrder();
        if (savedOrder.Count == 0) return accountList;

        var orderIndex = savedOrder
            .Select((key, index) => new { key, index })
            .GroupBy(pair => pair.key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        return accountList
            .Select((account, index) => new { account, index })
            .OrderBy(pair => orderIndex.TryGetValue(AccountIconKey(pair.account), out var savedIndex) ? savedIndex : int.MaxValue)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.account)
            .ToList();
    }

    private List<string> CurrentAccountOrder() => IsSharedLaunchMode() ? accountState.SharedAccountOrder : accountState.InstancedAccountOrder;

    private void SaveCurrentAccountOrder(IEnumerable<Account> orderedAccounts)
    {
        var order = orderedAccounts.Select(AccountIconKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (IsSharedLaunchMode())
        {
            accountState.SharedAccountOrder = order;
        }
        else
        {
            accountState.InstancedAccountOrder = order;
        }
        SaveAccountListState(accountState);
    }

    private void ReorderAccount(Account account, int targetIndex)
    {
        var ordered = OrderedAccounts().ToList();
        var currentIndex = ordered.FindIndex(item => AccountIconKey(item).Equals(AccountIconKey(account), StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0) return;

        var moved = ordered[currentIndex];
        ordered.RemoveAt(currentIndex);
        targetIndex = Math.Clamp(targetIndex, 0, ordered.Count);
        if (targetIndex > currentIndex) targetIndex--;
        ordered.Insert(targetIndex, moved);
        SaveCurrentAccountOrder(ordered);
        PopulateLists();
        SelectAccount(moved);
    }

    private void SortAccountsByName()
    {
        var ordered = OrderedAccounts()
            .OrderBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => GetUserNameFromAccountKey(account.AccountKey), StringComparer.OrdinalIgnoreCase)
            .ToList();
        SaveCurrentAccountOrder(ordered);
        PopulateLists();
        status.Text = "Sorted accounts alphabetically.";
    }

    private void SortAccountsByLastConnected()
    {
        var ordered = OrderedAccounts()
            .OrderByDescending(account => accountState.LastConnectedUtc.TryGetValue(AccountIconKey(account), out var connectedAt) ? connectedAt : DateTime.MinValue)
            .ThenBy(AccountDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SaveCurrentAccountOrder(ordered);
        PopulateLists();
        status.Text = "Sorted accounts by last connected.";
    }

    private void SortAccountsBySelectedBand()
    {
        if (bandList.SelectedItem is not BandConfig band)
        {
            status.Text = "Choose a band before sorting by band.";
            return;
        }

        NormalizeBand(band);
        var bandOrder = band.BatchFiles
            .Select((file, index) => new { file, index })
            .GroupBy(pair => pair.file, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        var ordered = OrderedAccounts()
            .Select((account, index) => new { account, index })
            .OrderBy(pair => bandOrder.TryGetValue(pair.account.BatchFile, out var bandIndex) ? 0 : 1)
            .ThenBy(pair => bandOrder.TryGetValue(pair.account.BatchFile, out var bandIndex) ? bandIndex : pair.index)
            .Select(pair => pair.account)
            .ToList();
        SaveCurrentAccountOrder(ordered);
        PopulateLists();
        status.Text = $"Sorted accounts by {band.Name}.";
    }

    private void SelectAccount(Account account)
    {
        var key = AccountIconKey(account);
        for (var index = 0; index < accountList.Items.Count; index++)
        {
            if (accountList.Items[index] is Account item && AccountIconKey(item).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                accountList.SelectedIndex = index;
                accountRosterGrid.SelectAccount(account);
                return;
            }
        }
    }

    private static bool IsDragGesture(Point start, Point current)
    {
        return Math.Abs(current.X - start.X) >= SystemInformation.DragSize.Width / 2 ||
            Math.Abs(current.Y - start.Y) >= SystemInformation.DragSize.Height / 2;
    }

    private static int DropIndexFromListBox(ListBox list, Point point)
    {
        var index = list.IndexFromPoint(point);
        if (index < 0) return list.Items.Count;
        var bounds = list.GetItemRectangle(index);
        return point.Y > bounds.Top + bounds.Height / 2 ? index + 1 : index;
    }

    private AccountRosterItem CreateRosterItem(Account account)
    {
        var displayName = AccountDisplayName(account);
        var tooltip = "This account has no Lodestone profile linked yet.";
        string? facePath = null;
        string? fullPath = null;
        if (settings.AccountIcons.TryGetValue(AccountIconKey(account), out var profile))
        {
            if (!string.IsNullOrWhiteSpace(profile.CharacterName) && !string.IsNullOrWhiteSpace(profile.World))
            {
                displayName = profile.CharacterName;
                tooltip = $"{profile.CharacterName}@{profile.World}\nRight-click for Lodestone options.";
            }

            var iconPath = AccountIconPath(profile);
            if (File.Exists(iconPath))
            {
                facePath = iconPath;
            }
            else if (!string.IsNullOrWhiteSpace(profile.CharacterName) && !string.IsNullOrWhiteSpace(profile.World))
            {
                tooltip = $"No downloaded portrait yet for {profile.CharacterName}@{profile.World}. Right-click to refresh from Lodestone.";
            }

            var bodyPath = AccountFullImagePath(profile);
            if (File.Exists(bodyPath))
            {
                fullPath = bodyPath;
            }
        }
        return new AccountRosterItem(account, displayName, facePath, fullPath, tooltip);
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

    private void UpdateAccountDisplayMode()
    {
        if (accountList is null || accountRosterGrid is null) return;
        var iconMode = IsAccountIconMode();
        accountList.Visible = !iconMode;
        accountRosterGrid.Visible = iconMode;
        if (iconMode && accounts.Count > 0)
        {
            var missingIcons = accounts.Count(account => !HasAccountIcon(account));
            if (missingIcons > 0)
            {
                status.Text = AppText.MissingAccountIconStatus(missingIcons);
            }
        }
    }

    private void RefreshAccountRosterOnly()
    {
        if (accountRosterGrid is null || accountRosterGrid.IsDisposed) return;
        accountRosterGrid.SetItems(OrderedAccounts().Select(CreateRosterItem));
    }

    private bool IsAccountIconMode() => NormalizeAccountDisplayMode(settings.AccountDisplayMode).Equals("Roster", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAccountDisplayMode(string displayMode) => displayMode.Equals("Icons", StringComparison.OrdinalIgnoreCase) || displayMode.Equals("Roster", StringComparison.OrdinalIgnoreCase) ? "Roster" : "Text";

    private bool HasAccountIcon(Account account)
    {
        return settings.AccountIcons.TryGetValue(AccountIconKey(account), out var profile) && File.Exists(AccountIconPath(profile));
    }

    private static string AccountDisplayName(Account account)
    {
        var name = account.Name.Trim();
        var separator = name.LastIndexOf(" - ", StringComparison.Ordinal);
        if (separator >= 0 && separator < name.Length - 3)
        {
            return name[(separator + 3)..].Trim();
        }
        return name;
    }

    private void RememberAccountCharacterTitle(Account account, string title)
    {
        if (!TryParseCharacterTitle(title, out var characterName, out var world)) return;
        var key = AccountIconKey(account);
        if (!settings.AccountIcons.TryGetValue(key, out var profile))
        {
            profile = new AccountIconProfile();
            settings.AccountIcons[key] = profile;
        }

        profile.CharacterName = characterName;
        profile.World = world;
        if (string.IsNullOrWhiteSpace(profile.IconFileName))
        {
            profile.IconFileName = AccountIconFileName(key);
        }
        SaveSettings(settings);
        _ = RefreshAccountIconAsync(account, quiet: true).ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion && task.Result && !IsDisposed)
            {
                BeginInvoke(new Action(RefreshAccountRosterOnly));
            }
        }, TaskScheduler.Default);
    }

    private void ShowAccountContextMenu(Account account, Control owner, Point location)
    {
        var menu = new ContextMenuStrip();
        var profile = GetAccountIconProfile(account) ?? new AccountIconProfile();
        var profileUrl = AccountProfileUrl(profile);

        var openProfile = new ToolStripMenuItem("Open Lodestone profile");
        openProfile.Enabled = !string.IsNullOrWhiteSpace(profileUrl);
        openProfile.Click += (_, _) => OpenUrl(profileUrl);
        menu.Items.Add(openProfile);

        var setProfile = new ToolStripMenuItem("Set Lodestone profile URL...");
        setProfile.Click += async (_, _) =>
        {
            var enteredUrl = ShowTextPrompt(
                "Set Lodestone profile URL",
                "Paste the Lodestone character profile URL for this account:",
                AccountProfileUrl(profile),
                AppText.LodestoneCharacterSearchUrl);
            if (string.IsNullOrWhiteSpace(enteredUrl)) return;
            var lodestoneId = ExtractLodestoneId(enteredUrl);
            if (string.IsNullOrWhiteSpace(lodestoneId))
            {
                MessageBox.Show("That does not look like a Lodestone character profile URL.", "Invalid profile URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            profile.LodestoneId = lodestoneId;
            profile.ProfileUrl = NormalizeLodestoneProfileUrl(lodestoneId);
            settings.AccountIcons[AccountIconKey(account)] = profile;
            SaveSettings(settings);
            SetStatus($"Refreshing {AccountDisplayName(account)} from profile...");
            if (await RefreshAccountIconAsync(account, quiet: false))
            {
                TryUpdateXivLauncherAccountMetadata(account, profile, showResult: true);
                LoadAccounts();
                PopulateLists();
            }
        };
        menu.Items.Add(setProfile);

        var refreshProfile = new ToolStripMenuItem("Refresh / auto-detect portrait now");
        refreshProfile.Enabled = CanRefreshPortraitManually(profile);
        refreshProfile.Click += async (_, _) =>
        {
            SetStatus($"Refreshing {AccountDisplayName(account)} portrait...");
            if (await RefreshAccountIconAsync(account, quiet: false))
            {
                RefreshAccountRosterOnly();
            }
        };
        menu.Items.Add(refreshProfile);

        menu.Items.Add(new ToolStripSeparator());

        var killClient = new ToolStripMenuItem("Kill this client");
        killClient.Click += (_, _) => KillGameInstance(account);
        menu.Items.Add(killClient);

        menu.Items.Add(new ToolStripSeparator());

        var sortMenu = new ToolStripMenuItem("Sort accounts");
        sortMenu.DropDownItems.Add("Alphabetically", null, (_, _) => SortAccountsByName());
        sortMenu.DropDownItems.Add("By last connected", null, (_, _) => SortAccountsByLastConnected());
        sortMenu.DropDownItems.Add("By selected band", null, (_, _) => SortAccountsBySelectedBand());
        menu.Items.Add(sortMenu);

        var delete = new ToolStripMenuItem("Delete account");
        delete.Click += (_, _) => DeleteAccount(account);
        menu.Items.Add(delete);

        menu.Show(owner, location);
    }

    private AccountIconProfile? GetAccountIconProfile(Account account)
    {
        return settings.AccountIcons.TryGetValue(AccountIconKey(account), out var profile) ? profile : null;
    }

    private static string AccountProfileUrl(AccountIconProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LodestoneId)) return NormalizeLodestoneProfileUrl(profile.LodestoneId);
        var id = ExtractLodestoneId(profile.ProfileUrl);
        return string.IsNullOrWhiteSpace(id) ? "" : NormalizeLodestoneProfileUrl(id);
    }

    private static string NormalizeLodestoneProfileUrl(string lodestoneId) => $"https://eu.finalfantasyxiv.com/lodestone/character/{lodestoneId}/";

    private static string ExtractLodestoneId(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var match = Regex.Match(text, @"/lodestone/character/(?<id>\d+)/?", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups["id"].Value;
        return Regex.IsMatch(text.Trim(), @"^\d+$") ? text.Trim() : "";
    }

    private static string ShowTextPrompt(string title, string prompt, string defaultValue, string helperUrl = "")
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = string.IsNullOrWhiteSpace(helperUrl) ? new Size(520, 150) : new Size(520, 178)
        };
        var label = new Label { Text = prompt, Bounds = new Rectangle(14, 14, 492, 24) };
        var input = new TextBox { Text = defaultValue, Bounds = new Rectangle(14, 44, 492, 29) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(326, form.ClientSize.Height - 52, 86, 32) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(420, form.ClientSize.Height - 52, 86, 32) };
        form.Controls.AddRange([label, input, ok, cancel]);
        if (!string.IsNullOrWhiteSpace(helperUrl))
        {
            var helper = new LinkLabel
            {
                Text = AppText.LodestoneHelperLinkText,
                Bounds = new Rectangle(14, 78, 230, 24),
                LinkColor = Color.RoyalBlue,
                ActiveLinkColor = Color.DeepPink,
                VisitedLinkColor = Color.MediumPurple
            };
            helper.LinkClicked += (_, _) => OpenUrl(helperUrl);
            var helperUrlLabel = new Label
            {
                Text = helperUrl,
                Bounds = new Rectangle(14, 102, 492, 20),
                ForeColor = Color.DimGray
            };
            form.Controls.AddRange([helper, helperUrlLabel]);
        }
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? input.Text.Trim() : "";
    }

    private static ImportMode? ShowImportModePrompt(string title, string itemName)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(560, 360)
        };

        var label = new Label { Text = "Import Mode:", Bounds = new Rectangle(16, 16, 520, 22) };
        var modeBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Bounds = new Rectangle(16, 42, 220, 28)
        };
        modeBox.Items.AddRange(Enum.GetNames<ImportMode>());
        modeBox.SelectedItem = ImportMode.Merge.ToString();

        var description = new TextBox
        {
            Bounds = new Rectangle(16, 84, 528, 210),
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical
        };
        var ok = new Button { Text = "Import", DialogResult = DialogResult.OK, Bounds = new Rectangle(360, 312, 86, 32) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(454, 312, 86, 32) };

        void UpdateDescription()
        {
            var mode = Enum.Parse<ImportMode>(modeBox.SelectedItem?.ToString() ?? nameof(ImportMode.Merge));
            description.Text = ImportModeDescription(mode, itemName);
        }

        modeBox.SelectedIndexChanged += (_, _) => UpdateDescription();
        form.Controls.AddRange([label, modeBox, description, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        UpdateDescription();

        return form.ShowDialog() == DialogResult.OK
            ? Enum.Parse<ImportMode>(modeBox.SelectedItem?.ToString() ?? nameof(ImportMode.Merge))
            : null;
    }

    private static string ImportModeDescription(ImportMode mode, string itemName)
    {
        return mode switch
        {
            ImportMode.AppendAll =>
                $"AppendAll:\r\nImports all {itemName} from the source.\r\nExisting band names are duplicated as copies. Existing XIVLauncher account identities cannot be duplicated and are skipped.",
            ImportMode.AppendNew =>
                $"AppendNew:\r\nImports only {itemName} that do not already exist.\r\nExisting names or account identities are ignored.",
            ImportMode.Merge =>
                $"Merge:\r\nAdds new {itemName} and replaces matching existing {itemName} with the imported data.",
            ImportMode.ReplaceExisting =>
                $"ReplaceExisting:\r\nReplaces only matching {itemName} already in your list.\r\nNew source items are ignored.",
            ImportMode.OverwriteAll =>
                $"OverwriteAll:\r\nDeletes your current {itemName} list and imports everything from the source.",
            _ => ""
        };
    }

    private bool TryUpdateXivLauncherAccountMetadata(Account account, AccountIconProfile profile, bool showResult)
    {
        if (!IsSharedLaunchMode()) return false;
        if (string.IsNullOrWhiteSpace(profile.CharacterName) ||
            string.IsNullOrWhiteSpace(profile.World) ||
            string.IsNullOrWhiteSpace(profile.IconUrl))
        {
            return false;
        }

        var accountListPath = SharedAccountListPath();
        if (!File.Exists(accountListPath)) return false;

        try
        {
            var jsonText = File.ReadAllText(accountListPath);
            var entries = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(jsonText);
            if (entries is null) return false;

            var userName = GetUserNameFromAccountKey(account.AccountKey);
            var changed = false;
            var updatedEntries = new List<Dictionary<string, object?>>();
            foreach (var entry in entries)
            {
                var updated = entry.ToDictionary(pair => pair.Key, pair => JsonElementToObject(pair.Value), StringComparer.Ordinal);
                if (IsMatchingXivLauncherAccountEntry(entry, userName, account.UseSteamServiceAccount, account.UseOtp))
                {
                    updated["ChosenCharacterName"] = profile.CharacterName;
                    updated["ChosenCharacterWorld"] = profile.World;
                    updated["ThumbnailUrl"] = profile.IconUrl;
                    changed = true;
                }
                updatedEntries.Add(updated);
            }

            if (!changed) return false;

            BackupXivLauncherAccountList(accountListPath);
            File.WriteAllText(accountListPath, JsonSerializer.Serialize(updatedEntries, new JsonSerializerOptions { WriteIndented = true }));
            if (showResult)
            {
                status.Text = $"Updated XIVLauncher account metadata for {profile.CharacterName}.";
            }
            return true;
        }
        catch (Exception ex)
        {
            if (showResult)
            {
                MessageBox.Show($"Could not update XIVLauncher accountsList.json.\n\n{ex.Message}", "Account metadata update failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }
    }

    private string SharedAccountListPath()
    {
        var folder = !string.IsNullOrWhiteSpace(settings.SharedProfileFolder)
            ? settings.SharedProfileFolder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher");
        return Path.Combine(folder, "accountsList.json");
    }

    private static bool IsMatchingXivLauncherAccountEntry(Dictionary<string, JsonElement> entry, string userName, bool useSteam, bool useOtp)
    {
        return entry.TryGetValue("UserName", out var userNameElement) &&
            (userNameElement.GetString() ?? "").Equals(userName, StringComparison.OrdinalIgnoreCase) &&
            GetJsonElementBool(entry, "UseSteamServiceAccount") == useSteam &&
            GetJsonElementBool(entry, "UseOtp") == useOtp;
    }

    private static bool GetJsonElementBool(Dictionary<string, JsonElement> entry, string propertyName)
    {
        return entry.TryGetValue(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => JsonSerializer.Deserialize<object>(element.GetRawText())
        };
    }

    private static void BackupXivLauncherAccountList(string accountListPath)
    {
        var backupFolder = Path.Combine(Path.GetDirectoryName(accountListPath)!, "backups", "PotatoLauncher");
        Directory.CreateDirectory(backupFolder);
        var backupPath = Path.Combine(backupFolder, $"accountsList-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(accountListPath, backupPath, overwrite: false);
    }

    private void DeleteAccount(Account account)
    {
        var displayName = AccountDisplayName(account);
        var detail = IsSharedLaunchMode()
            ? "This removes the selected Shared account entry from accountsList.json. No backup will be created."
            : "This deletes only the selected Instanced BAT launcher file. Shared mode accounts are not touched.";
        var result = MessageBox.Show(
            $"Delete {displayName} from Potato Launcher?\n\n{detail}",
            "Delete account",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        try
        {
            if (IsSharedLaunchMode())
            {
                DeleteSharedAccount(account);
            }
            else
            {
                DeleteInstancedAccount(account);
            }

            RemoveAccountReferences(account);
            SaveSettings(settings);
            LoadAccounts();
            PopulateLists();
            status.Text = $"Deleted {displayName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete {displayName}.\n\n{ex.Message}", "Delete account failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSharedAccount(Account account)
    {
        var accountListPath = SharedAccountListPath();
        if (!File.Exists(accountListPath)) throw new FileNotFoundException("Missing accountsList.json.", accountListPath);

        var entries = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(File.ReadAllText(accountListPath))
            ?? throw new InvalidOperationException("accountsList.json could not be read.");
        var userName = GetUserNameFromAccountKey(account.AccountKey);
        var updatedEntries = entries
            .Where(entry => !IsMatchingXivLauncherAccountEntry(entry, userName, account.UseSteamServiceAccount, account.UseOtp))
            .Select(entry => entry.ToDictionary(pair => pair.Key, pair => JsonElementToObject(pair.Value), StringComparer.Ordinal))
            .ToList();

        if (updatedEntries.Count == entries.Count) throw new InvalidOperationException("No matching XIVLauncher account entry was found.");

        File.WriteAllText(accountListPath, JsonSerializer.Serialize(updatedEntries, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void DeleteInstancedAccount(Account account)
    {
        if (string.IsNullOrWhiteSpace(settings.DalamudFolder)) throw new InvalidOperationException("No Instanced folder is selected.");
        var root = Path.GetFullPath(settings.DalamudFolder);
        var target = Path.GetFullPath(Path.Combine(root, account.BatchFile));
        if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The BAT file is outside the selected Instanced folder.");
        }
        if (!File.Exists(target)) throw new FileNotFoundException("Missing BAT launcher file.", target);
        File.Delete(target);
    }

    private void RemoveAccountReferences(Account account)
    {
        var key = AccountIconKey(account);
        CurrentAccountOrder().RemoveAll(value => value.Equals(key, StringComparison.OrdinalIgnoreCase));
        accountState.LastConnectedUtc.Remove(key);
        foreach (var band in CurrentBands())
        {
            band.BatchFiles.RemoveAll(file => file.Equals(account.BatchFile, StringComparison.OrdinalIgnoreCase));
        }
        SaveAccountListState(accountState);
    }

    private void ExportAccountList()
    {
        if (!IsSharedLaunchMode())
        {
            MessageBox.Show("Account list export uses Shared mode accountsList.json.", "Export accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var accountListPath = SharedAccountListPath();
        if (!File.Exists(accountListPath))
        {
            MessageBox.Show("Choose a Shared folder with accountsList.json before exporting.", "Export accounts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(accountListPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return;

            var transfer = new AccountListTransfer();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var userName = GetJsonString(element, "UserName");
                if (string.IsNullOrWhiteSpace(userName)) continue;
                var useSteam = GetJsonBool(element, "UseSteamServiceAccount");
                var useOtp = GetJsonBool(element, "UseOtp");
                var accountKey = BuildAccountKey(userName, useSteam, useOtp);
                transfer.Accounts.Add(new AccountListTransferEntry
                {
                    UserName = userName,
                    UseSteamServiceAccount = useSteam,
                    UseOtp = useOtp,
                    ChosenCharacterName = GetJsonString(element, "ChosenCharacterName"),
                    ChosenCharacterWorld = GetJsonString(element, "ChosenCharacterWorld"),
                    ThumbnailUrl = GetJsonString(element, "ThumbnailUrl")
                });
                if (settings.AccountIcons.TryGetValue(accountKey, out var profile))
                {
                    transfer.AccountIcons[accountKey] = profile;
                }
            }
            var exportedKeys = transfer.Accounts
                .Select(AccountTransferKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            transfer.AccountOrder = OrderedAccounts()
                .Select(AccountIconKey)
                .Where(exportedKeys.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var key in transfer.AccountOrder)
            {
                if (accountState.LastConnectedUtc.TryGetValue(key, out var connectedAt))
                {
                    transfer.LastConnectedUtc[key] = connectedAt;
                }
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Export account list",
                Filter = "Potato account list (*.json)|*.json",
                FileName = "potato-account-list.json"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(transfer, new JsonSerializerOptions { WriteIndented = true }));
            ShowSaveFeedback($"Exported {transfer.Accounts.Count} account entries.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export account list.\n\n{ex.Message}", "Export accounts failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ImportAccountList()
    {
        if (!IsSharedLaunchMode())
        {
            MessageBox.Show("Account list import uses Shared mode accountsList.json.", "Import accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Import account list",
            Filter = "Potato account list (*.json)|*.json|JSON files (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var transfer = JsonSerializer.Deserialize<AccountListTransfer>(File.ReadAllText(dialog.FileName));
            if (transfer is null || transfer.Accounts.Count == 0)
            {
                MessageBox.Show("That file does not contain exported Potato Launcher accounts.", "Import accounts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var mode = ShowImportModePrompt("Import accounts", "accounts");
            if (mode is null) return;

            var accountListPath = SharedAccountListPath();
            Directory.CreateDirectory(Path.GetDirectoryName(accountListPath)!);
            var existingEntries = File.Exists(accountListPath)
                ? JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(File.ReadAllText(accountListPath)) ?? []
                : [];
            var updatedEntries = mode == ImportMode.OverwriteAll
                ? new List<Dictionary<string, object?>>()
                : existingEntries
                    .Select(entry => entry.ToDictionary(pair => pair.Key, pair => JsonElementToObject(pair.Value), StringComparer.Ordinal))
                    .ToList();

            var added = 0;
            var replaced = 0;
            var skipped = 0;
            foreach (var imported in transfer.Accounts.Where(account => !string.IsNullOrWhiteSpace(account.UserName)))
            {
                var existing = updatedEntries.FirstOrDefault(entry => IsMatchingAccountEntry(entry, imported.UserName, imported.UseSteamServiceAccount, imported.UseOtp));
                if (mode == ImportMode.OverwriteAll)
                {
                    updatedEntries.Add(CreateAccountListEntry(imported));
                    added++;
                    continue;
                }

                if (existing is null)
                {
                    if (mode == ImportMode.ReplaceExisting)
                    {
                        skipped++;
                        continue;
                    }

                    updatedEntries.Add(CreateAccountListEntry(imported));
                    added++;
                    continue;
                }

                if (mode is ImportMode.AppendAll or ImportMode.AppendNew)
                {
                    skipped++;
                    continue;
                }

                if (ReplaceAccountMetadata(existing, imported)) replaced++;
            }

            var importedProfiles = ApplyImportedProfiles(transfer.AccountIcons ?? [], mode.Value);
            var importedOrder = ApplyImportedAccountState(transfer, mode.Value);

            if (File.Exists(accountListPath)) BackupXivLauncherAccountList(accountListPath);
            File.WriteAllText(accountListPath, JsonSerializer.Serialize(updatedEntries, new JsonSerializerOptions { WriteIndented = true }));
            SaveSettings(settings);
            SaveAccountListState(accountState);
            LoadAccounts();
            PopulateLists();
            status.Text = $"Imported accounts: {added} added, {replaced} updated, {skipped} skipped, {importedProfiles} profiles linked, {importedOrder} order entries.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not import account list.\n\n{ex.Message}", "Import accounts failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveBandsToDefault()
    {
        SaveCurrentBand();
        var transfer = new BandTransfer
        {
            LaunchMode = NormalizeLaunchMode(settings.LaunchMode),
            Bands = CurrentBands().Select(CloneBand).ToList()
        };
        var path = BandExportPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(transfer, new JsonSerializerOptions { WriteIndented = true }));
        ShowSaveFeedback($"Saved {transfer.Bands.Count} band{(transfer.Bands.Count == 1 ? "" : "s")} to {Path.GetFileName(path)}.");
    }

    private void ExportBandsAs()
    {
        SaveCurrentBand();
        var transfer = new BandTransfer
        {
            LaunchMode = NormalizeLaunchMode(settings.LaunchMode),
            Bands = CurrentBands().Select(CloneBand).ToList()
        };
        using var dialog = new SaveFileDialog
        {
            Title = "Export bands",
            Filter = "Potato bands (*.json)|*.json",
            FileName = "band.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(transfer, new JsonSerializerOptions { WriteIndented = true }));
            ShowSaveFeedback($"Exported {transfer.Bands.Count} band{(transfer.Bands.Count == 1 ? "" : "s")}.");
    }

    private void ImportBands()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import bands",
            Filter = "Potato bands (*.json)|*.json|JSON files (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            var importedBands = text.TrimStart().StartsWith("[", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize<List<BandConfig>>(text) ?? []
                : JsonSerializer.Deserialize<BandTransfer>(text)?.Bands ?? [];
            if (importedBands.Count == 0)
            {
                MessageBox.Show("That file does not contain any bands.", "Import bands", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var mode = ShowImportModePrompt("Import bands", "bands");
            if (mode is null) return;

            var result = ApplyImportedBands(importedBands, mode.Value);

            SaveSettingsFromInputs();
            PopulateLists();
            status.Text = $"Imported bands: {result.added} added, {result.replaced} replaced, {result.skipped} skipped.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not import bands.\n\n{ex.Message}", "Import bands failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private int ApplyImportedProfiles(Dictionary<string, AccountIconProfile> importedProfiles, ImportMode mode)
    {
        var linked = 0;
        foreach (var pair in importedProfiles.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)))
        {
            var hasExisting = settings.AccountIcons.TryGetValue(pair.Key, out var existingProfile);
            var shouldApply = mode switch
            {
                ImportMode.AppendAll or ImportMode.AppendNew => !hasExisting || string.IsNullOrWhiteSpace(existingProfile?.LodestoneId),
                ImportMode.Merge or ImportMode.OverwriteAll => true,
                ImportMode.ReplaceExisting => hasExisting,
                _ => false
            };
            if (!shouldApply) continue;
            settings.AccountIcons[pair.Key] = CloneProfile(pair.Value);
            linked++;
        }
        return linked;
    }

    private int ApplyImportedAccountState(AccountListTransfer transfer, ImportMode mode)
    {
        var importedKeys = transfer.Accounts
            .Select(AccountTransferKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var importedKeySet = importedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importedOrder = transfer.AccountOrder
            .Where(importedKeySet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        importedOrder.AddRange(importedKeys.Where(key => !importedOrder.Contains(key, StringComparer.OrdinalIgnoreCase)));

        var currentOrder = accountState.SharedAccountOrder;
        var importedOrderSet = importedOrder.ToHashSet(StringComparer.OrdinalIgnoreCase);
        accountState.SharedAccountOrder = mode switch
        {
            ImportMode.AppendAll or ImportMode.AppendNew => currentOrder
                .Concat(importedOrder.Where(key => !currentOrder.Contains(key, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ImportMode.Merge => importedOrder
                .Concat(currentOrder.Where(key => !importedOrderSet.Contains(key)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ImportMode.ReplaceExisting => importedOrder
                .Where(key => currentOrder.Contains(key, StringComparer.OrdinalIgnoreCase))
                .Concat(currentOrder.Where(key => !importedOrderSet.Contains(key)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ImportMode.OverwriteAll => importedOrder,
            _ => currentOrder
        };

        foreach (var pair in transfer.LastConnectedUtc.Where(pair => importedKeySet.Contains(pair.Key)))
        {
            var hasExisting = accountState.LastConnectedUtc.ContainsKey(pair.Key);
            var shouldApply = mode switch
            {
                ImportMode.AppendAll or ImportMode.AppendNew => !hasExisting,
                ImportMode.Merge or ImportMode.OverwriteAll => true,
                ImportMode.ReplaceExisting => hasExisting,
                _ => false
            };
            if (shouldApply) accountState.LastConnectedUtc[pair.Key] = pair.Value;
        }

        return importedOrder.Count;
    }

    private (int added, int replaced, int skipped) ApplyImportedBands(List<BandConfig> importedBands, ImportMode mode)
    {
        var target = CurrentBands();
        var added = 0;
        var replaced = 0;
        var skipped = 0;

        if (mode == ImportMode.OverwriteAll)
        {
            target.Clear();
            target.AddRange(importedBands.Select(CloneBandWithDefaultName));
            return (target.Count, 0, 0);
        }

        foreach (var imported in importedBands.Select(CloneBandWithDefaultName))
        {
            var existingIndex = target.FindIndex(band => band.Name.Equals(imported.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                if (mode == ImportMode.ReplaceExisting)
                {
                    skipped++;
                    continue;
                }

                target.Add(imported);
                added++;
                continue;
            }

            switch (mode)
            {
                case ImportMode.AppendAll:
                    imported.Name = UniqueBandName(imported.Name, target);
                    target.Add(imported);
                    added++;
                    break;
                case ImportMode.AppendNew:
                    skipped++;
                    break;
                case ImportMode.Merge:
                case ImportMode.ReplaceExisting:
                    imported.Id = target[existingIndex].Id;
                    target[existingIndex] = imported;
                    replaced++;
                    break;
            }
        }

        return (added, replaced, skipped);
    }

    private static bool IsMatchingAccountEntry(Dictionary<string, object?> entry, string userName, bool useSteam, bool useOtp)
    {
        return entry.TryGetValue("UserName", out var storedUserName) &&
            string.Equals(storedUserName?.ToString() ?? "", userName, StringComparison.OrdinalIgnoreCase) &&
            Convert.ToBoolean(entry.GetValueOrDefault("UseSteamServiceAccount") ?? false) == useSteam &&
            Convert.ToBoolean(entry.GetValueOrDefault("UseOtp") ?? false) == useOtp;
    }

    private static string AccountTransferKey(AccountListTransferEntry account)
    {
        return string.IsNullOrWhiteSpace(account.UserName)
            ? ""
            : BuildAccountKey(account.UserName, account.UseSteamServiceAccount, account.UseOtp);
    }

    private static Dictionary<string, object?> CreateAccountListEntry(AccountListTransferEntry imported)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["UserName"] = imported.UserName,
            ["UseSteamServiceAccount"] = imported.UseSteamServiceAccount,
            ["UseOtp"] = imported.UseOtp,
            ["LastSuccessfulOtp"] = null,
            ["SavePassword"] = false,
            ["ChosenCharacterName"] = imported.ChosenCharacterName,
            ["ChosenCharacterWorld"] = imported.ChosenCharacterWorld,
            ["ThumbnailUrl"] = imported.ThumbnailUrl
        };
    }

    private static bool ReplaceAccountMetadata(Dictionary<string, object?> entry, AccountListTransferEntry imported)
    {
        var changed = false;
        changed |= ReplaceIfProvided(entry, "ChosenCharacterName", imported.ChosenCharacterName);
        changed |= ReplaceIfProvided(entry, "ChosenCharacterWorld", imported.ChosenCharacterWorld);
        changed |= ReplaceIfProvided(entry, "ThumbnailUrl", imported.ThumbnailUrl);
        return changed;
    }

    private static bool ReplaceIfProvided(Dictionary<string, object?> entry, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (entry.TryGetValue(key, out var existing) && string.Equals(existing?.ToString() ?? "", value, StringComparison.Ordinal)) return false;
        entry[key] = value;
        return true;
    }

    private static BandConfig CloneBand(BandConfig band)
    {
        return new BandConfig { Id = band.Id, Name = band.Name, BatchFiles = band.BatchFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
    }

    private static BandConfig CloneBandWithDefaultName(BandConfig band)
    {
        var clone = CloneBand(band);
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = string.IsNullOrWhiteSpace(clone.Name) ? "Imported Band" : clone.Name;
        return clone;
    }

    private static AccountIconProfile CloneProfile(AccountIconProfile profile)
    {
        return new AccountIconProfile
        {
            CharacterName = profile.CharacterName,
            World = profile.World,
            LodestoneId = profile.LodestoneId,
            ProfileUrl = profile.ProfileUrl,
            IconUrl = profile.IconUrl,
            IconFileName = profile.IconFileName,
            FullImageUrl = profile.FullImageUrl,
            FullImageFileName = profile.FullImageFileName,
            LastUpdatedUtc = profile.LastUpdatedUtc
        };
    }

    private static string UniqueBandName(string requestedName, List<BandConfig> bands)
    {
        if (!bands.Any(band => band.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase))) return requestedName;
        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{requestedName} ({suffix})";
            if (!bands.Any(band => band.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
        return $"{requestedName} ({DateTime.Now:HHmmss})";
    }

    internal static string BandExportPath()
    {
        EnsurePersistentDataMigrated();
        return Path.Combine(PersistentDataRoot(), "band.json");
    }

    internal static bool ShouldRefreshPortraitOnStartup(AccountIconProfile profile)
    {
        return !string.IsNullOrWhiteSpace(AccountProfileUrl(profile));
    }

    internal static bool CanRefreshPortraitManually(AccountIconProfile profile)
    {
        return ShouldRefreshPortraitOnStartup(profile) ||
            (!string.IsNullOrWhiteSpace(profile.CharacterName) && !string.IsNullOrWhiteSpace(profile.World));
    }

    private async Task RefreshLinkedAccountIconsOnStartupAsync()
    {
        if (accounts.Count == 0) return;

        var refreshTargets = OrderedAccounts()
            .Select(account => new { account, key = AccountIconKey(account) })
            .Where(item => settings.AccountIcons.TryGetValue(item.key, out var profile) && ShouldRefreshPortraitOnStartup(profile))
            .Select(item => item.account)
            .ToList();

        if (refreshTargets.Count == 0)
        {
            RefreshAccountRosterOnly();
            return;
        }

        SetStatus($"Refreshing {refreshTargets.Count} linked portrait{(refreshTargets.Count == 1 ? "" : "s")}...");
        using var semaphore = new SemaphoreSlim(6);
        var results = await Task.WhenAll(refreshTargets.Select(async account =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await RefreshAccountIconAsync(account, quiet: true, saveSettings: false);
            }
            finally
            {
                semaphore.Release();
            }
        }));

        var refreshed = results.Count(result => result);
        if (refreshed > 0) SaveSettings(settings);
        RefreshAccountRosterOnly();
        SetStatus($"Portraits refreshed: {refreshed}/{refreshTargets.Count}.", force: true);
    }

    private async Task<bool> RefreshAccountIconAsync(Account account, bool quiet, bool saveSettings = true)
    {
        var key = AccountIconKey(account);
        if (!settings.AccountIcons.TryGetValue(key, out var profile))
        {
            profile = new AccountIconProfile();
            settings.AccountIcons[key] = profile;
        }

        try
        {
            var result = await FindLodestoneIconAsync(account, profile);
            Directory.CreateDirectory(AccountIconsFolder());
            profile.LodestoneId = result.LodestoneId;
            profile.CharacterName = result.CharacterName;
            profile.World = result.World;
            profile.ProfileUrl = result.ProfileUrl;
            profile.IconUrl = result.IconUrl;
            profile.FullImageUrl = result.FullImageUrl;
            profile.IconFileName = AccountIconFileName(key);
            profile.FullImageFileName = AccountFullImageFileName(key);
            profile.LastUpdatedUtc = DateTime.UtcNow;

            await DownloadAccountImageAsync(result.IconUrl, AccountIconPath(profile));
            await DownloadAccountImageAsync(result.FullImageUrl, AccountFullImagePath(profile));
            if (saveSettings) SaveSettings(settings);

            if (!quiet && status is not null)
            {
                SetStatus($"Updated {profile.CharacterName}@{profile.World}.", force: true);
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!quiet && status is not null)
            {
                SetStatus($"Could not refresh {AccountDisplayName(account)}: {ex.Message}", force: true);
            }
            return false;
        }
    }

    private static async Task<LodestoneIconResult> FindLodestoneIconAsync(Account account, AccountIconProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LodestoneId))
        {
            return await FetchLodestoneProfileAsync(profile.LodestoneId);
        }

        var profileId = ExtractLodestoneId(profile.ProfileUrl);
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            return await FetchLodestoneProfileAsync(profileId);
        }

        if (!string.IsNullOrWhiteSpace(profile.CharacterName) && !string.IsNullOrWhiteSpace(profile.World))
        {
            return await SearchLodestoneProfileAsync(profile.CharacterName, profile.World);
        }

        throw new InvalidOperationException($"No Lodestone profile URL is set for {AccountDisplayName(account)}. Right-click the account and set the profile URL.");
    }

    private static async Task<LodestoneIconResult> SearchLodestoneProfileAsync(string characterName, string world)
    {
        var searchUrl = BuildLodestoneCharacterSearchUrl(characterName, world);
        var searchHtml = await LodestoneClient.GetStringAsync(searchUrl);
        if (!TryFindExactLodestoneSearchCandidate(searchHtml, characterName, world, out var candidate))
        {
            throw new InvalidOperationException($"No exact Lodestone match found for {characterName}@{world}.");
        }

        return await FetchLodestoneProfileAsync(candidate.LodestoneId);
    }

    internal static string BuildLodestoneCharacterSearchUrl(string characterName, string world)
    {
        var nameQuery = Uri.EscapeDataString((characterName ?? "").Trim());
        var worldQuery = Uri.EscapeDataString((world ?? "").Trim());
        return $"{AppText.LodestoneCharacterSearchUrl}?q={nameQuery}&worldname={worldQuery}";
    }

    internal static bool TryFindExactLodestoneSearchCandidate(string html, string characterName, string world, out LodestoneSearchCandidate candidate)
    {
        candidate = new LodestoneSearchCandidate("", "", "", "", "");
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(world))
        {
            return false;
        }

        const string entryPattern = """
            <div\s+class="entry">\s*
            <a\s+href="/lodestone/character/(?<id>\d+)/"[^>]*>
            .*?<img\s+src="(?<icon>https://img2\.finalfantasyxiv\.com/f/[^"]+)"[^>]*>
            .*?<p\s+class="entry__name">(?<name>.*?)</p>
            \s*<p\s+class="entry__world">(?<worldHtml>.*?)</p>
            """;

        foreach (Match match in Regex.Matches(html, entryPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace))
        {
            var resultName = WebUtility.HtmlDecode(StripHtmlTags(match.Groups["name"].Value)).Trim();
            var resultWorldText = WebUtility.HtmlDecode(StripHtmlTags(match.Groups["worldHtml"].Value)).Trim();
            var resultWorld = ExtractWorldName(resultWorldText);
            if (!resultName.Equals(characterName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                !resultWorld.Equals(world.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lodestoneId = match.Groups["id"].Value;
            candidate = new LodestoneSearchCandidate(
                lodestoneId,
                resultName,
                resultWorld,
                NormalizeLodestoneProfileUrl(lodestoneId),
                WebUtility.HtmlDecode(match.Groups["icon"].Value).Trim());
            return true;
        }

        return false;
    }

    private static string StripHtmlTags(string html)
    {
        return Regex.Replace(html, "<[^>]+>", "", RegexOptions.Singleline).Trim();
    }

    private static string ExtractWorldName(string worldText)
    {
        var bracketIndex = worldText.IndexOf('[', StringComparison.Ordinal);
        return (bracketIndex >= 0 ? worldText[..bracketIndex] : worldText).Trim();
    }

    private static async Task<LodestoneIconResult> FetchLodestoneProfileAsync(string lodestoneId)
    {
        var profileUrl = $"https://eu.finalfantasyxiv.com/lodestone/character/{lodestoneId}/";
        var profileHtml = await LodestoneClient.GetStringAsync(profileUrl);
        var faceUrl = FindFirstImageUrl(profileHtml, "fc0") ?? throw new InvalidOperationException($"No Lodestone face portrait found for character {lodestoneId}.");
        var fullUrl = FindFirstImageUrl(profileHtml, "fl0") ?? throw new InvalidOperationException($"No full Lodestone portrait found for character {lodestoneId}.");
        var characterName = WebUtility.HtmlDecode(Regex.Match(profileHtml, @"<p\s+class=""frame__chara__name"">(?<name>[^<]+)</p>", RegexOptions.IgnoreCase).Groups["name"].Value).Trim();
        var worldMatch = Regex.Match(profileHtml, @"<p\s+class=""frame__chara__world"">.*?(?<world>[A-Za-z'\-]+)\s*\[", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var world = WebUtility.HtmlDecode(worldMatch.Groups["world"].Value).Trim();
        if (string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(world))
        {
            var titleMatch = Regex.Match(profileHtml, @"<title>\s*(?<name>[^|]+)\|[^|]+\|(?<world>[A-Za-z'\-]+)\s*\[", RegexOptions.IgnoreCase);
            characterName = string.IsNullOrWhiteSpace(characterName) ? WebUtility.HtmlDecode(titleMatch.Groups["name"].Value).Trim() : characterName;
            world = string.IsNullOrWhiteSpace(world) ? WebUtility.HtmlDecode(titleMatch.Groups["world"].Value).Trim() : world;
        }
        if (string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(world))
        {
            throw new InvalidOperationException($"Could not read character name/world from Lodestone profile {lodestoneId}.");
        }

        return new LodestoneIconResult(lodestoneId, characterName, world, profileUrl, faceUrl, fullUrl);
    }

    private static string? FindFirstImageUrl(string html, string suffix)
    {
        var match = Regex.Match(html, $@"https://img2\.finalfantasyxiv\.com/f/[^""']+{Regex.Escape(suffix)}\.jpg\?[^""']+", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Value) : null;
    }

    private static async Task DownloadAccountImageAsync(string imageUrl, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("Missing Lodestone image URL or cache path.");
        }

        var bytes = await LodestoneClient.GetByteArrayAsync(imageUrl);
        var tempPath = $"{targetPath}.tmp";
        await File.WriteAllBytesAsync(tempPath, bytes);
        if (File.Exists(targetPath)) File.Delete(targetPath);
        File.Move(tempPath, targetPath);
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
            band.BatchFiles.All(file => Regex.IsMatch(file, @"^(0[1-9]|1[0-6])-", RegexOptions.IgnoreCase));
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
        PopulateMemberList(band);
    }

    private void PopulateMemberList(BandConfig? band)
    {
        loadingBand = true;
        memberList.BeginUpdate();
        try
        {
            var orderedAccounts = OrderedAccounts().ToList();
            if (band is not null)
            {
                NormalizeBand(band);
            }

            memberList.SetAccounts(orderedAccounts, band?.BatchFiles ?? []);
        }
        finally
        {
            memberList.EndUpdate();
            loadingBand = false;
        }
    }

    private void SaveCurrentBand()
    {
        SaveCurrentBand(true);
    }

    private void SaveCurrentBand(bool refreshListItem)
    {
        if (bandList.SelectedItem is not BandConfig band) return;
        band.BatchFiles = memberList.CheckedAccounts.Select(account => account.BatchFile).ToList();
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

    private void ShowBandContextMenu(Control owner, Point location)
    {
        if (bandList.SelectedItem is not BandConfig band) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Set name", null, (_, _) => SetSelectedBandName(band));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Kill this band's clients", null, (_, _) => KillBandGameInstances(band));
        menu.Show(owner, location);
    }

    private void SetSelectedBandName(BandConfig band)
    {
        var name = ShowTextPrompt("Set band name", "Type the name of this band:", band.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        band.Name = name.Trim();
        var index = bandList.SelectedIndex;
        SaveSettingsFromInputs();
        if (index >= 0)
        {
            bandList.Items[index] = band;
            bandList.SelectedIndex = index;
        }
    }

    private void LaunchSelectedAccount()
    {
        if (IsAccountIconMode() && accountRosterGrid.SelectedAccount is Account rosterAccount)
        {
            _ = LaunchSingleAccountAsync(rosterAccount);
            return;
        }
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
            SetStatus($"Cancelled {account.Name}.");
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
            SetStatus("Choose or create a band first.", force: true);
            return;
        }
        await LaunchBandAsync(band, DateTimeOffset.UtcNow, null, CancellationToken.None);
    }

    private async Task LaunchBandAsync(BandConfig band, DateTimeOffset startAtUtc, Action<MultibandLaunchProgress>? progress, CancellationToken externalToken)
    {
        var bandAccounts = AccountsForBand(band);
        if (bandAccounts.Count == 0)
        {
            SetStatus($"{band.Name} has no accounts selected.", force: true);
            progress?.Invoke(new MultibandLaunchProgress("Failed", $"{band.Name} has no accounts selected.", []));
            return;
        }
        queueCancel?.Cancel();
        queueCancel?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        queueCancel = cancellation;
        launchBandButton.Enabled = false;
        ShowLoadingOverlay($"Loading {band.Name}", $"Queueing {bandAccounts.Count} account{(bandAccounts.Count == 1 ? "" : "s")}...");
        BeginLoadingQueue(bandAccounts);
        var readinessTasks = new List<Task>();
        var accountStatuses = bandAccounts.Select(account => new MultibandAccountStatus(AccountDisplayName(account), "Queued")).ToList();
        void Report(string state, string detail)
        {
            progress?.Invoke(new MultibandLaunchProgress(state, detail, accountStatuses.ToList()));
        }
        try
        {
            var startDelay = startAtUtc - DateTimeOffset.UtcNow;
            if (startDelay > TimeSpan.Zero)
            {
                Report("Scheduled", $"Starting at {startAtUtc.LocalDateTime:T}.");
                while (startAtUtc > DateTimeOffset.UtcNow)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var remaining = Math.Max(1, (int)Math.Ceiling((startAtUtc - DateTimeOffset.UtcNow).TotalSeconds));
                    UpdateLoadingOverlay($"{band.Name}: synchronized start in {remaining}s.", force: true);
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500, Math.Max(1, (startAtUtc - DateTimeOffset.UtcNow).TotalMilliseconds))), cancellation.Token);
                }
            }

            for (var index = 0; index < bandAccounts.Count; index++)
            {
                var account = bandAccounts[index];
                accountStatuses[index] = accountStatuses[index] with { Status = "Launching" };
                SetRandomLoadingGif();
                loadingTitle.Text = $"Loading {band.Name}";
                UpdateLoadingOverlay($"{band.Name}: launching {account.Name} ({index + 1}/{bandAccounts.Count}).");
                Report("Launching", $"Launching {AccountDisplayName(account)} ({index + 1}/{bandAccounts.Count}).");
                var client = await StartAccountAndWaitForClientAsync(account, cancellation.Token);
                var startedClient = new StartedGameClient(account, client.ProcessId);
                UpdateLoadingQueueItem(index, account, "Loading");
                accountStatuses[index] = accountStatuses[index] with { Status = "Loading" };
                Report("Launching", $"{AccountDisplayName(account)} is loading.");
                var capturedIndex = index;
                var readinessTask = MonitorBandClientReadinessAsync(index, startedClient, cancellation.Token, readinessStatus =>
                {
                    accountStatuses[capturedIndex] = accountStatuses[capturedIndex] with { Status = readinessStatus };
                    Report("Launching", $"{AccountDisplayName(account)}: {readinessStatus}.");
                });
                SetStatus($"{band.Name}: started {account.Name} ({index + 1}/{bandAccounts.Count}).");
                if (settings.WaitForClientInitializationBeforeNextLaunch)
                {
                    UpdateLoadingOverlay($"{band.Name}: waiting for {AccountDisplayName(account)} to initialize.");
                    await readinessTask;
                }
                else
                {
                    readinessTasks.Add(readinessTask);
                }

                if (index < bandAccounts.Count - 1)
                {
                    await WaitForLaunchCooldownAsync(band.Name, cancellation.Token);
                }
            }
            await Task.WhenAll(readinessTasks);
            var loadedMessage = $"All of {band.Name} is loaded.";
            Report("Completed", loadedMessage);
            SetStatus(loadedMessage, force: true);
            UpdateLoadingOverlay(loadedMessage, force: true);
            await Task.Delay(800);
            ClearLoadingQueue();
            ApplyLoadingOverlayLayout();
            loadingTitle.Text = loadedMessage;
            UpdateLoadingOverlay(loadedMessage, force: true);
            await Task.Delay(700);
        }
        catch (OperationCanceledException)
        {
            for (var index = 0; index < bandAccounts.Count; index++)
            {
                UpdateLoadingQueueItem(index, bandAccounts[index], "Cancelled");
            }
            SetStatus($"{band.Name} queue cancelled.", force: true);
            UpdateLoadingOverlay($"{band.Name} queue cancelled.");
            Report("Cancelled", $"{band.Name} queue cancelled.");
        }
        catch (Exception ex)
        {
            var message = $"{band.Name} queue failed: {ex.Message}";
            SetStatus(message, force: true);
            UpdateLoadingOverlay(message, force: true);
            Report("Failed", message);
        }
        finally
        {
            HideLoadingOverlay();
            ClearLoadingQueue();
            launchBandButton.Enabled = true;
            cancellation.Dispose();
            if (ReferenceEquals(queueCancel, cancellation)) queueCancel = null;
        }
    }

    private List<Account> AccountsForBand(BandConfig band)
    {
        return band.BatchFiles
            .Select(file => accounts.FirstOrDefault(account => account.BatchFile.Equals(file, StringComparison.OrdinalIgnoreCase)))
            .Where(account => account is not null)
            .Cast<Account>()
            .ToList();
    }

    private async Task MonitorBandClientReadinessAsync(int queueIndex, StartedGameClient client, CancellationToken token, Action<string>? progress = null)
    {
        UpdateLoadingQueueItem(queueIndex, client.Account, "Loading");
        progress?.Invoke("Loading");
        await WaitForGameClientCharacterTitleAsync(client, token, status =>
        {
            var normalizedStatus = status.Equals("Initialized", StringComparison.OrdinalIgnoreCase) ? "Initialized" : "Loading";
            UpdateLoadingQueueItem(queueIndex, client.Account, normalizedStatus);
            progress?.Invoke(normalizedStatus);
        });
        UpdateLoadingQueueItem(queueIndex, client.Account, "Initialized");
        progress?.Invoke("Initialized");
        RememberAccountConnected(client.Account);
    }

    private async Task<bool> LaunchAccountAsync(Account account, CancellationToken token, bool quiet = false)
    {
        try
        {
            var client = await StartAccountAndWaitForClientAsync(account, token);
            await WaitForGameClientCharacterTitleAsync(new StartedGameClient(account, client.ProcessId), token);
            RememberAccountConnected(account);
            if (!quiet)
            {
                SetStatus($"{account.Name} reached the character window title.");
            }
            UpdateLoadingOverlay($"{account.Name} reached the character window title.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Could not launch {account.Name}: {ex.Message}", force: true);
            return false;
        }
    }

    private void RememberAccountConnected(Account account)
    {
        accountState.LastConnectedUtc[AccountIconKey(account)] = DateTime.UtcNow;
        SaveAccountListState(accountState);
    }

    private async Task<GameClientWindow> StartAccountAndWaitForClientAsync(Account account, CancellationToken token)
    {
        SaveSettingsFromInputs();
        var launcherProcessesBefore = GetLauncherProcessIds();
        var gameClientsBefore = GetGameClientProcessIds();
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
        SetStatus(IsSharedLaunchMode()
            ? $"Started {account.Name} through Shared."
            : $"Started {account.Name} from BAT command.");
        var waitMessage = account.UseOtp
            ? $"Started {account.Name}. OTP is enabled, waiting for manual login..."
            : $"Started {account.Name}. Waiting for XIVLauncher to finish...";
        UpdateLoadingOverlay(waitMessage);

        await WaitForLauncherHandoffAsync(launcherProcess, launcherProcessesBefore, account.Name, token);
        var client = await WaitForFreshGameClientAsync(gameClientsBefore, account.Name, token);
        runningClientProcessIds[AccountIconKey(account)] = client.ProcessId;
        return client;
    }

    private async Task WaitForLaunchCooldownAsync(string bandName, CancellationToken token)
    {
        var seconds = Math.Clamp(settings.LaunchCooldownSeconds, 0, 300);
        if (seconds <= 0) return;
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            token.ThrowIfCancellationRequested();
            var message = $"{bandName}: next client launches in {remaining}s.";
            SetStatus(message);
            UpdateLoadingOverlay(AppText.LoadingCooldownText(remaining), force: true);
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        UpdateLoadingOverlay("", force: true);
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
                        var message = $"Waiting for XIVLauncher to finish {accountName}...";
                        SetStatus(message);
                        UpdateLoadingOverlay(message);
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
                var message = $"Waiting for XIVLauncher to finish {accountName}...";
                SetStatus(message);
                UpdateLoadingOverlay(message);
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

    private async Task<GameClientWindow> WaitForFreshGameClientAsync(HashSet<int> existingProcessIds, string accountName, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var client = GetFreshGameClient(existingProcessIds);
            if (client is not null)
            {
                var message = $"Found {accountName}'s game client.";
                SetStatus(message);
                UpdateLoadingOverlay(message);
                return client.Value;
            }

            var waitingMessage = $"Waiting for {accountName}'s game client to appear...";
            SetStatus(waitingMessage);
            UpdateLoadingOverlay(waitingMessage);
            await Task.Delay(500, token);
        }

        throw new TimeoutException($"Timed out waiting for {accountName}'s FFXIV client to appear.");
    }

    private async Task WaitForGameClientCharacterTitleAsync(StartedGameClient startedClient, CancellationToken token, Action<string>? accountStatus = null)
    {
        var deadline = DateTime.UtcNow.AddMinutes(10);
        var stableCharacterTitleHits = 0;
        string? stableCharacterTitle = null;
        var sawGameConnection = false;
        var characterNameCandidates = AccountCharacterNameCandidates(startedClient.Account);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var matchedClient = FindGameClientByAccountTitle(startedClient.Account, characterNameCandidates);
            var client = matchedClient ?? GetGameClientByProcessId(startedClient.ProcessId);
            if (client is null)
            {
                var message = $"Waiting for {startedClient.Account.Name}'s game client...";
                SetStatus(message);
                accountStatus?.Invoke("Loading");
                UpdateLoadingOverlay(message);
                await Task.Delay(500, token);
                continue;
            }

            var processId = client.Value.ProcessId;
            if (HasEstablishedTcpConnection(processId))
            {
                sawGameConnection = true;
            }

            var title = client.Value.Title.Trim();
            if (IsMatchingCharacterTitle(title, characterNameCandidates))
            {
                runningClientProcessIds[AccountIconKey(startedClient.Account)] = processId;
                stableCharacterTitleHits = title.Equals(stableCharacterTitle, StringComparison.Ordinal)
                    ? stableCharacterTitleHits + 1
                    : 1;
                stableCharacterTitle = title;
                var message = $"Detected {title} ({stableCharacterTitleHits}/3).";
                SetStatus(message);
                accountStatus?.Invoke("Loading");
                UpdateLoadingOverlay(message);
                if (stableCharacterTitleHits >= 3)
                {
                    var readyMessage = $"{title} is ready.";
                    SetStatus(readyMessage, force: true);
                    accountStatus?.Invoke("Initialized");
                    UpdateLoadingOverlay(readyMessage);
                    RememberAccountCharacterTitle(startedClient.Account, title);
                    return;
                }
            }
            else
            {
                stableCharacterTitleHits = 0;
                stableCharacterTitle = null;
                var message = sawGameConnection
                    ? $"Waiting {startedClient.Account.Name} to connect..."
                    : $"Waiting for {startedClient.Account.Name}'s data center connection...";
                SetStatus(message);
                accountStatus?.Invoke("Loading");
                UpdateLoadingOverlay(message);
            }

            await Task.Delay(700, token);
        }

        throw new TimeoutException($"Timed out waiting for {startedClient.Account.Name}'s FFXIV window title to switch to Character@World.");
    }

    private static HashSet<int> GetGameClientProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var processName in new[] { "ffxiv", "ffxiv_dx11" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    ids.Add(process.Id);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return ids;
    }

    private static GameClientWindow? GetFreshGameClient(HashSet<int> existingProcessIds)
    {
        foreach (var processName in new[] { "ffxiv", "ffxiv_dx11" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (existingProcessIds.Contains(process.Id)) continue;
                    process.Refresh();
                    var handle = process.MainWindowHandle;
                    var title = process.MainWindowTitle ?? "";
                    if (handle == IntPtr.Zero) return new GameClientWindow(process.Id, handle, title);
                    if (IsWindowVisible(handle) && GetWindowRect(handle, out var rect) && rect.Width > 320 && rect.Height > 240)
                    {
                        return new GameClientWindow(process.Id, handle, title);
                    }
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return null;
    }

    private static GameClientWindow? GetGameClientByProcessId(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();
            return new GameClientWindow(process.Id, process.MainWindowHandle, process.MainWindowTitle ?? "");
        }
        catch
        {
            return null;
        }
    }

    private GameClientWindow? FindGameClientByAccountTitle(Account account, HashSet<string>? characterNameCandidates = null)
    {
        var candidates = characterNameCandidates ?? AccountCharacterNameCandidates(account);
        if (candidates.Count == 0) return null;

        foreach (var processName in new[] { "ffxiv", "ffxiv_dx11" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Refresh();
                    var title = process.MainWindowTitle ?? "";
                    if (GameClientTitleMatchesAccount(title, candidates))
                    {
                        return new GameClientWindow(process.Id, process.MainWindowHandle, title);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return null;
    }

    private static bool IsCharacterTitle(string title)
    {
        return TryParseCharacterTitle(title, out _, out _) &&
            !title.Equals("FINAL FANTASY XIV", StringComparison.OrdinalIgnoreCase) &&
            title.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static bool IsMatchingCharacterTitle(string title, HashSet<string> characterNameCandidates)
    {
        if (!IsCharacterTitle(title)) return false;
        return characterNameCandidates.Count == 0 || GameClientTitleMatchesAccount(title, characterNameCandidates);
    }

    private static bool TryParseCharacterTitle(string title, out string characterName, out string world)
    {
        characterName = "";
        world = "";
        var separator = title.LastIndexOf('@');
        if (separator <= 0 || separator >= title.Length - 1) return false;
        characterName = title[..separator].Trim();
        world = title[(separator + 1)..].Trim();
        return characterName.Length > 0 && world.Length > 0;
    }

    private static bool HasEstablishedTcpConnection(int processId)
    {
        var bufferLength = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferLength, true, AfInet, TcpTableClass.OwnerPidAll);
        if (bufferLength <= 0) return false;

        var buffer = Marshal.AllocHGlobal(bufferLength);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref bufferLength, true, AfInet, TcpTableClass.OwnerPidAll);
            if (result != 0) return false;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(uint));
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            for (var index = 0; index < rowCount; index++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(IntPtr.Add(rowPointer, index * rowSize));
                if (row.OwningPid == processId && row.State == TcpStateEstablished && row.RemoteAddr != 0)
                {
                    return true;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return false;
    }

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

    private void SetStatus(string text, bool force = false)
    {
        if (status is null || status.IsDisposed) return;
        if (statusUpdateGate.ShouldApply(text, DateTime.UtcNow, force))
        {
            status.Text = text;
        }
    }

    private void ShowSaveFeedback(string message)
    {
        SetStatus(message, force: true);
        var now = DateTime.UtcNow;
        if ((now - lastSaveNotificationUtc).TotalMilliseconds < 1200) return;
        lastSaveNotificationUtc = now;
        if (!settings.NotificationsEnabled) return;
        AppNotification.Show(this, "Potato Launcher", message);
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
        loadingStatusUpdateGate.Reset();
        loadingStatus.Text = detail;
        ClearLoadingQueue();
        ApplyLoadingOverlayLayout();
        loadingOverlay.Visible = true;
        loadingOverlay.BringToFront();
        statusPill.Visible = false;
        loadingOverlay.Focus();
        loadingOverlay.Invalidate(false);
    }

    private void UpdateLoadingOverlay(string detail, bool force = false)
    {
        if (!loadingOverlay.Visible) return;
        if (loadingQueueActive && !force)
        {
            return;
        }
        if (loadingStatusUpdateGate.ShouldApply(detail, DateTime.UtcNow, force))
        {
            loadingStatus.Text = detail;
            loadingStatus.Invalidate();
        }
    }

    private void BeginLoadingQueue(IReadOnlyList<Account> queuedAccounts)
    {
        loadingQueueActive = queuedAccounts.Count > 0;
        ApplyLoadingOverlayLayout();
        loadingQueueLabels.Clear();
        loadingQueuePanel.SuspendLayout();
        loadingQueuePanel.Controls.Clear();
        loadingQueuePanel.AutoScrollPosition = Point.Empty;
        loadingStatus.Text = "";
        loadingQueuePanel.Visible = loadingQueueActive;
        for (var index = 0; index < queuedAccounts.Count; index++)
        {
            var account = queuedAccounts[index];
            var label = new Label
            {
                AutoSize = false,
                Height = 22,
                Width = LoadingQueueRowWidth(),
                Location = LoadingQueueRowLocation(index),
                Padding = new Padding(8, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(55, palette.ListBack),
                ForeColor = LoadingQueueStateColor(palette, "Queued"),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Text = LoadingQueueText(account, "Queued")
            };
            loadingQueueLabels[index] = label;
            loadingQueuePanel.Controls.Add(label);
        }
        UpdateLoadingQueueScrollSize();
        loadingQueuePanel.ResumeLayout(false);
    }

    private void UpdateLoadingQueueItem(int queueIndex, Account account, string state)
    {
        if (!loadingQueueLabels.TryGetValue(queueIndex, out var label)) return;
        var visibleState = NormalizeLoadingQueueState(state);
        label.Text = LoadingQueueText(account, visibleState);
        label.ForeColor = LoadingQueueStateColor(palette, visibleState);
        label.Invalidate();
    }

    internal static string LoadingQueueText(Account account, string state) => $"{AccountDisplayName(account)} - {state}";

    internal static string NormalizeLoadingQueueState(string state)
    {
        if (state.Equals("Queued", StringComparison.OrdinalIgnoreCase)) return "Queued";
        if (state.Equals("Initialized", StringComparison.OrdinalIgnoreCase)) return "Initialized";
        if (state.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) return "Cancelled";
        if (state.Equals("Failed", StringComparison.OrdinalIgnoreCase)) return "Failed";
        return "Loading";
    }

    internal static Color LoadingQueueStateColor(ThemePalette palette, string state)
    {
        var normalized = NormalizeLoadingQueueState(state);
        if (normalized.Equals("Initialized", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(98, 214, 135);
        if (normalized.Equals("Loading", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(105, 172, 255);
        if (normalized.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Failed", StringComparison.OrdinalIgnoreCase)) return palette.Danger;
        return IsLightColor(palette.Card) ? palette.Text : Color.White;
    }

    private static bool IsLightColor(Color color)
    {
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        return luminance >= 0.62;
    }

    private void ResizeLoadingQueueRows()
    {
        if (loadingQueuePanel is null) return;
        for (var index = 0; index < loadingQueuePanel.Controls.Count; index++)
        {
            var control = loadingQueuePanel.Controls[index];
            control.Width = LoadingQueueRowWidth();
            control.Location = LoadingQueueRowLocation(index);
        }
        UpdateLoadingQueueScrollSize();
    }

    private int LoadingQueueRowWidth() => Math.Max(120, loadingQueuePanel.ClientSize.Width - 6);

    private static Point LoadingQueueRowLocation(int index) => new(0, index * 24);

    private void UpdateLoadingQueueScrollSize()
    {
        var rowCount = loadingQueuePanel?.Controls.Count ?? 0;
        if (loadingQueuePanel is null || rowCount == 0)
        {
            if (loadingQueuePanel is not null) loadingQueuePanel.AutoScrollMinSize = Size.Empty;
            return;
        }

        loadingQueuePanel.AutoScrollMinSize = new Size(0, LoadingQueueRowLocation(rowCount).Y);
    }

    private void ApplyLoadingOverlayLayout()
    {
        if (loadingOverlay is null || loadingCard is null || bandCard is null) return;
        var loadingLayout = LoadingOverlayMetrics.Calculate(bandCard.Width, bandCard.Height, loadingQueueActive);
        loadingOverlay.Bounds = loadingLayout.OverlayBounds;
        loadingCard.Bounds = loadingLayout.CardBounds;
        loadingPicture.Bounds = loadingLayout.PictureBounds;
        loadingPicture.Visible = !loadingLayout.PictureBounds.IsEmpty;
        loadingTitle.Bounds = loadingLayout.TitleBounds;
        loadingQueuePanel.Bounds = loadingLayout.QueueBounds;
        loadingStatus.Bounds = loadingLayout.StatusBounds;
        loadingStatus.Visible = !loadingLayout.StatusBounds.IsEmpty;
        loadingCancel.Bounds = loadingLayout.CancelBounds;
        ResizeLoadingQueueRows();
    }

    private void ClearLoadingQueue()
    {
        if (loadingQueuePanel is null) return;
        loadingQueueActive = false;
        loadingQueueLabels.Clear();
        loadingQueuePanel.Controls.Clear();
        loadingQueuePanel.Visible = false;
        if (loadingPicture is not null) loadingPicture.Visible = true;
        if (loadingStatus is not null) loadingStatus.Visible = true;
    }

    private void HideLoadingOverlay()
    {
        loadingOverlay.Visible = false;
        statusPill.Visible = true;
        statusPill.BringToFront();
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

    private async Task LoadNewsBandrollAsync()
    {
        try
        {
            await LoadLauncherNewsAsync();
            var slides = new List<NewsBandrollSlide>();
            foreach (var banner in newsBanners.Where(banner => !string.IsNullOrWhiteSpace(banner.ImageUrl)).Take(5))
            {
                var image = await DownloadNewsImageAsync(banner.ImageUrl);
                if (image is not null)
                {
                    slides.Add(new NewsBandrollSlide(image, banner.LinkUrl, banner.Title));
                }
            }

            newsBandroll.SetSlides(slides);
            newsBandroll.Visible = newsBandroll.HasSlides;
            ApplyResponsiveLayout();
            ApplyThemeRecursive(newsBandroll);
        }
        catch
        {
            newsBandroll.SetSlides([]);
        }
    }

    private void HideNewsOverlay()
    {
        newsOverlay.Visible = false;
        var oldImage = newsBannerPicture.Image;
        newsBannerPicture.Image = null;
        oldImage?.Dispose();
    }

    private void ShowHelpWindow()
    {
        using var form = new Form
        {
            Text = "Potato Launcher Help",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(680, 560),
            Font = new Font("Segoe UI", 10F),
            BackColor = Color.FromArgb(255, palette.Card)
        };

        var title = new Label
        {
            Text = "Potato Launcher Help",
            Bounds = new Rectangle(18, 16, 520, 30),
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = palette.Text,
            BackColor = Color.Transparent
        };
        var helpText = new RichTextBox
        {
            Text = AppText.HelpWindowText(),
            Bounds = new Rectangle(18, 58, 644, 432),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            WordWrap = true,
            BackColor = palette.ListBack,
            ForeColor = palette.Text,
            Font = new Font("Segoe UI", 10F)
        };
        StyleHelpText(helpText);
        var close = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Bounds = new Rectangle(552, 510, 110, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = palette.Primary,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        close.FlatAppearance.BorderSize = 0;

        form.Controls.AddRange([title, helpText, close]);
        form.AcceptButton = close;
        form.CancelButton = close;
        form.ShowDialog(this);
    }

    private void ShowOptimizerMonitor()
    {
        if (optimizerMonitor is { IsDisposed: false })
        {
            optimizerMonitor.ApplyTheme(palette);
            optimizerMonitor.Show();
            optimizerMonitor.WindowState = FormWindowState.Normal;
            optimizerMonitor.BringToFront();
            return;
        }

        optimizerMonitor = new OptimizerMonitorForm(optimizerService, palette, () => settings.NotificationsEnabled);
        optimizerMonitor.FormClosed += (_, _) => optimizerMonitor = null;
        optimizerMonitor.Show(this);
    }

    private void StyleHelpText(RichTextBox helpText)
    {
        using var headingFont = new Font(helpText.Font, FontStyle.Bold);
        foreach (var heading in new[]
        {
            "Launch modes",
            "Accounts",
            "Lodestone profiles and portraits",
            "Bands",
            "Multiband",
            "Launching",
            "Display and themes",
            "Import and export",
            "News and updates",
            "Safety tools",
            "Optimizer"
        })
        {
            var index = helpText.Text.IndexOf(heading, StringComparison.Ordinal);
            if (index < 0) continue;
            helpText.Select(index, heading.Length);
            helpText.SelectionFont = headingFont;
            helpText.SelectionColor = palette.Primary;
        }
        helpText.Select(0, 0);
    }

    private void KillGameInstance(Account account)
    {
        var processIds = FindGameClientProcessIdsForAccount(account);
        if (processIds.Count == 0)
        {
            SetStatus($"No running FFXIV client found for {AccountDisplayName(account)}.", force: true);
            return;
        }

        var killed = 0;
        var failures = 0;
        foreach (var processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch
            {
                failures++;
            }
        }

        runningClientProcessIds.Remove(AccountIconKey(account));
        var displayName = AccountDisplayName(account);
        SetStatus(killed == 0
            ? $"Could not terminate {displayName}'s FFXIV client."
            : $"Terminated {displayName}'s FFXIV client{(killed == 1 ? "" : "s")}{(failures > 0 ? $" ({failures} failed)" : "")}.",
            force: true);
    }

    private void KillBandGameInstances(BandConfig band)
    {
        SaveCurrentBand();
        var bandAccounts = AccountsForBand(band);
        if (bandAccounts.Count == 0)
        {
            SetStatus($"{band.Name} has no accounts selected.", force: true);
            return;
        }

        var processIds = new HashSet<int>();
        foreach (var account in bandAccounts)
        {
            foreach (var processId in FindGameClientProcessIdsForAccount(account))
            {
                processIds.Add(processId);
            }
        }

        if (processIds.Count == 0)
        {
            SetStatus($"No running FFXIV clients found for {band.Name}.", force: true);
            return;
        }

        var killed = 0;
        var failures = 0;
        foreach (var processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch
            {
                failures++;
            }
        }

        foreach (var account in bandAccounts)
        {
            runningClientProcessIds.Remove(AccountIconKey(account));
        }

        SetStatus(killed == 0
            ? $"Could not terminate any FFXIV clients for {band.Name}."
            : $"Terminated {killed} FFXIV client{(killed == 1 ? "" : "s")} for {band.Name}{(failures > 0 ? $" ({failures} failed)" : "")}.",
            force: true);
    }

    private List<int> FindGameClientProcessIdsForAccount(Account account)
    {
        var ids = new HashSet<int>();
        var key = AccountIconKey(account);
        if (runningClientProcessIds.TryGetValue(key, out var trackedProcessId))
        {
            if (IsRunningGameClientProcess(trackedProcessId))
            {
                ids.Add(trackedProcessId);
            }
            else
            {
                runningClientProcessIds.Remove(key);
            }
        }

        var candidates = AccountCharacterNameCandidates(account);
        if (candidates.Count == 0) return ids.ToList();
        foreach (var processName in new[] { "ffxiv", "ffxiv_dx11" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var title = process.MainWindowTitle ?? "";
                    if (GameClientTitleMatchesAccount(title, candidates))
                    {
                        ids.Add(process.Id);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return ids.ToList();
    }

    private static bool IsRunningGameClientProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited &&
                (process.ProcessName.Equals("ffxiv", StringComparison.OrdinalIgnoreCase) ||
                 process.ProcessName.Equals("ffxiv_dx11", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private HashSet<string> AccountCharacterNameCandidates(Account account)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings.AccountIcons.TryGetValue(AccountIconKey(account), out var profile))
        {
            if (!string.IsNullOrWhiteSpace(profile.CharacterName)) candidates.Add(profile.CharacterName.Trim());
        }

        var displayName = AccountDisplayName(account);
        if (!string.IsNullOrWhiteSpace(displayName)) candidates.Add(displayName.Trim());
        return candidates;
    }

    private static bool GameClientTitleMatchesAccount(string title, HashSet<string> characterNameCandidates)
    {
        if (!TryParseCharacterTitle(title.Trim(), out var characterName, out _)) return false;
        return characterNameCandidates.Contains(characterName);
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

        if (killed > 0)
        {
            runningClientProcessIds.Clear();
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

        var headlineJson = await http.GetStringAsync($"https://frontier.ffxiv.com/news/headline.json?lang=en-us&media=pcapp&_={timestamp}");
        using var headlineDocument = JsonDocument.Parse(headlineJson);
        var topicEntries = ReadNewsEntries(headlineDocument.RootElement, "topics", true)
            .OrderByDescending(entry => entry.Date)
            .Take(5)
            .ToList();
        newsEntries.AddRange(ReadNewsEntries(headlineDocument.RootElement, "news", false));
        newsEntries.Sort((left, right) => right.Date.CompareTo(left.Date));

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
                        newsBanners.Add(new NewsBanner(imageUrl, linkUrl, linkUrl));
                    }
                }
            }
        }

        foreach (var topic in topicEntries)
        {
            if (newsBanners.Count >= 5) break;
            if (newsBanners.Any(banner => banner.LinkUrl.Equals(topic.Url, StringComparison.OrdinalIgnoreCase))) continue;
            newsBanners.Add(new NewsBanner("", topic.Url, topic.Title));
        }
    }

    private static List<NewsEntry> ReadNewsEntries(JsonElement root, string propertyName, bool topic)
    {
        var entries = new List<NewsEntry>();
        if (!root.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array) return entries;
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
            entries.Add(new NewsEntry(title, url, date, tag));
        }
        return entries;
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
        newsBannerTitle.Text = string.IsNullOrWhiteSpace(banner.Title) ? banner.LinkUrl : banner.Title;
        if (string.IsNullOrWhiteSpace(banner.ImageUrl))
        {
            newsBannerPicture.Image = CreateGeneratedNewsBanner(banner.Title);
            return;
        }
        try
        {
            newsBannerPicture.Image = await DownloadNewsImageAsync(banner.ImageUrl);
        }
        catch
        {
            newsBannerTitle.Text = "Could not load featured image. Click to open it online.";
        }
    }

    private static async Task<Image?> DownloadNewsImageAsync(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return null;
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher");
        var bytes = await http.GetByteArrayAsync(imageUrl);
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private Image CreateGeneratedNewsBanner(string title)
    {
        var image = new Bitmap(newsBannerPicture.Width, newsBannerPicture.Height);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var backgroundBrush = new LinearGradientBrush(new Rectangle(Point.Empty, image.Size), palette.Back1, palette.Back2, 35);
        graphics.FillRectangle(backgroundBrush, new Rectangle(Point.Empty, image.Size));
        using var veil = new SolidBrush(Color.FromArgb(150, palette.Card));
        using var card = RoundedRectangle(new Rectangle(34, 34, image.Width - 68, image.Height - 68), 28);
        graphics.FillPath(veil, card);
        using var border = new Pen(palette.Border, 2);
        graphics.DrawPath(border, card);
        using var titleFont = new Font("Segoe UI", 20F, FontStyle.Bold);
        using var subtitleFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(palette.Text);
        using var mutedBrush = new SolidBrush(palette.Muted);
        using var titleFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord };
        graphics.DrawString("Latest Lodestone Topic", subtitleFont, mutedBrush, new RectangleF(50, 58, image.Width - 100, 28), titleFormat);
        graphics.DrawString(title, titleFont, titleBrush, new RectangleF(64, 82, image.Width - 128, 120), titleFormat);
        graphics.DrawString("Click to open", subtitleFont, mutedBrush, new RectangleF(50, 202, image.Width - 100, 28), titleFormat);
        return image;
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
        radius = Math.Min(radius, Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 2));
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
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
            BrowseFolder(sharedProfileInput, "Choose the XIVLauncher folder containing accountsList.json", () => SaveAndRescan(showFeedback: true));
            return;
        }
        BrowseFolder(folderInput, "Choose the folder containing your instanced launcher .bat files", () => SaveAndRescan(showFeedback: true));
    }

    private void SaveAndRescan(bool showFeedback = false)
    {
        SaveSettingsFromInputs(showFeedback);
        LoadAccounts();
        PopulateLists();
    }

    private void SaveSettingsFromInputs(bool showFeedback = false)
    {
        settings.DalamudFolder = folderInput.Text.Trim();
        settings.SharedProfileFolder = sharedProfileInput.Text.Trim();
        settings.LaunchMode = NormalizeLaunchMode(launchModeInput?.SelectedItem?.ToString() ?? settings.LaunchMode);
        settings.LaunchModeChosen = true;
        settings.Theme = themeInput?.SelectedItem?.ToString() ?? settings.Theme;
        settings.LaunchCooldownSeconds = (int)(launchCooldownInput?.Value ?? settings.LaunchCooldownSeconds);
        settings.WaitForClientInitializationBeforeNextLaunch = waitForClientInitializationInput?.Checked ?? settings.WaitForClientInitializationBeforeNextLaunch;
        settings.AccountDisplayMode = NormalizeAccountDisplayMode(accountDisplayInput?.SelectedItem?.ToString() ?? settings.AccountDisplayMode);
        settings.RandomizeThemeAtLaunch = randomizeThemeInput?.Checked ?? settings.RandomizeThemeAtLaunch;
        settings.NotificationsEnabled = notificationsEnabledInput?.Checked ?? settings.NotificationsEnabled;
        SaveSettings(settings);
        if (showFeedback) ShowSaveFeedback("Settings saved.");
    }

    private async Task CheckForUpdatesAsync()
    {
        updateButton.Enabled = false;
        var previousStatus = status.Text;
        try
        {
            status.Text = "Downloading latest Potato Launcher...";
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher");
            var tempRoot = Path.Combine(Path.GetTempPath(), $"PotatoLauncherUpdate-{Guid.NewGuid():N}");
            var zipPath = Path.Combine(tempRoot, ReleaseZipName);
            var extractPath = Path.Combine(tempRoot, "extract");
            Directory.CreateDirectory(tempRoot);
            await using (var downloadStream = await http.GetStreamAsync(LatestReleaseDownloadUrl()))
            await using (var fileStream = File.Create(zipPath))
            {
                await downloadStream.CopyToAsync(fileStream);
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            var latestVersion = ReadPackagedAppVersion(extractPath);
            var currentVersion = CurrentAppVersion();
            if (latestVersion.CompareTo(currentVersion) <= 0)
            {
                TryDeleteDirectory(tempRoot);
                status.Text = $"Potato Launcher is up to date ({currentVersion}).";
                MessageBox.Show($"Potato Launcher is already up to date.\n\nCurrent version: {currentVersion}", "No update needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            status.Text = $"Installing Potato Launcher {latestVersion}...";
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

    private async Task ShowChangelogIfNewVersionAsync()
    {
        if (!settings.LaunchModeChosen) return;
        var tagName = CurrentReleaseTag();
        if (tagName.Equals(settings.LastShownChangelogVersion, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/tags/{tagName}");
            using var document = JsonDocument.Parse(json);
            var body = GetJsonString(document.RootElement, "body");
            if (!string.IsNullOrWhiteSpace(body))
            {
                ShowChangelogOverlay(tagName, body);
            }
            settings.LastShownChangelogVersion = tagName;
            SaveSettings(settings);
        }
        catch
        {
        }
    }

    private void ShowChangelogOverlay(string tagName, string changelog)
    {
        var overlay = new RoundedPanel { Bounds = new Rectangle(220, 130, 550, 420), Radius = 24 };
        overlay.Controls.Add(Header($"Updated to {tagName}", 24, 22, 320, 38));
        var close = Button("OK", 394, 354, 110, 36, "Primary");
        close.Click += (_, _) =>
        {
            background.Controls.Remove(overlay);
            overlay.Dispose();
        };
        var notes = new TextBox
        {
            Text = changelog.Replace("\n", Environment.NewLine),
            Bounds = new Rectangle(24, 78, 480, 252),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle
        };
        overlay.Controls.Add(notes);
        overlay.Controls.Add(close);
        background.Controls.Add(overlay);
        ApplyThemeRecursive(overlay);
        overlay.BringToFront();
    }

    private static Version CurrentAppVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    }

    private static string CurrentReleaseTag()
    {
        var version = CurrentAppVersion();
        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    internal static string LatestReleaseDownloadUrl() => $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest/download/{ReleaseZipName}";

    internal static Version ParseExecutableVersion(string? versionText)
    {
        return Version.TryParse(versionText, out var version)
            ? version
            : throw new InvalidOperationException($"The downloaded Potato Launcher version is not valid: {versionText}");
    }

    private static Version ReadPackagedAppVersion(string extractPath)
    {
        var exePath = Directory.GetFiles(extractPath, ReleaseExeName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException($"The downloaded update is missing {ReleaseExeName}.");
        var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
        return ParseExecutableVersion(versionInfo.FileVersion ?? versionInfo.ProductVersion);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
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

    private void InitializeMultiband()
    {
        var dataRoot = PersistentDataRoot();
        multibandSettingsStore = new MultibandSettingsStore(Path.Combine(dataRoot, "multiband.json"));
        multibandSettings = multibandSettingsStore.Load();
        var certificate = new MultibandCertificateStore(Path.Combine(dataRoot, "multiband-server.pfx")).LoadOrCreate(multibandSettings.DeviceName);
        multibandServer = new MultibandServer(
            multibandSettings,
            multibandSettingsStore,
            certificate,
            () => RunOnUiAsync<IReadOnlyList<MultibandBandSummary>>(() => Task.FromResult(GetMultibandBandCatalog())),
            bandId => RunOnUiAsync(() => Task.FromResult(CanLaunchMultibandBand(bandId))),
            (bandId, startAtUtc, progress, token) => RunOnUiAsync(() => LaunchMultibandBandAsync(bandId, startAtUtc, progress, token)));
        multibandClient = new MultibandClient(multibandSettings, multibandSettingsStore, multibandServer.CertificateFingerprint);

        if (!multibandSettings.ListenEnabled) return;
        try
        {
            multibandServer.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            multibandSettings.ListenEnabled = false;
            multibandSettingsStore.Save(multibandSettings);
            SetStatus($"Multiband listener could not start: {ex.Message}", force: true);
        }
    }

    private void ShowMultibandWindow()
    {
        if (multibandForm is null || multibandForm.IsDisposed)
        {
            multibandForm = new MultibandForm(
                multibandSettings,
                multibandSettingsStore,
                multibandServer,
                multibandClient,
                GetMultibandBandCatalog,
                bandId => Task.FromResult(CanLaunchMultibandBand(bandId)),
                LaunchMultibandBandAsync,
                palette);
            multibandForm.FormClosed += (_, _) => multibandForm = null;
            multibandForm.Show(this);
        }
        else
        {
            multibandForm.ApplyPalette(palette);
            multibandForm.Show();
            multibandForm.Activate();
        }
    }

    private IReadOnlyList<MultibandBandSummary> GetMultibandBandCatalog()
    {
        return CurrentBands()
            .Select(band => new MultibandBandSummary(band.Id, band.Name, AccountsForBand(band).Count, NormalizeLaunchMode(settings.LaunchMode)))
            .ToList();
    }

    private MultibandReadiness CanLaunchMultibandBand(string bandId)
    {
        if (queueCancel is not null) return MultibandReadiness.Fail("This PC already has an active launch queue.");
        var band = CurrentBands().FirstOrDefault(item => item.Id.Equals(bandId, StringComparison.OrdinalIgnoreCase));
        if (band is null) return MultibandReadiness.Fail("The selected band no longer exists in the active launch mode.");
        if (AccountsForBand(band).Count == 0) return MultibandReadiness.Fail($"{band.Name} has no available accounts.");
        return MultibandReadiness.Success();
    }

    private Task LaunchMultibandBandAsync(string bandId, DateTimeOffset startAtUtc, Action<MultibandLaunchProgress> progress, CancellationToken token)
    {
        var readiness = CanLaunchMultibandBand(bandId);
        if (!readiness.Ready) return Task.FromException(new InvalidOperationException(readiness.Error));
        var band = CurrentBands().FirstOrDefault(item => item.Id.Equals(bandId, StringComparison.OrdinalIgnoreCase));
        return band is null
            ? Task.FromException(new InvalidOperationException("The selected band no longer exists in the active launch mode."))
            : LaunchBandAsync(band, startAtUtc, progress, token);
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        if (!InvokeRequired) return action();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(new Action(async () =>
        {
            try { completion.SetResult(await action()); }
            catch (Exception ex) { completion.SetException(ex); }
        }));
        return completion.Task;
    }

    private Task RunOnUiAsync(Func<Task> action)
    {
        if (!InvokeRequired) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(new Action(async () =>
        {
            try { await action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }));
        return completion.Task;
    }

    private void ApplyTheme(string themeName)
    {
        using var redraw = BeginRedrawScope(this);
        SuspendLayout();
        try
        {
            themeName = NormalizeThemeName(themeName);
            palette = Palettes.TryGetValue(themeName, out var chosen) ? chosen : Palettes["Pink"];
            background.Palette = palette;
            loadingOverlay.Palette = palette;
            launchChoiceOverlay.Palette = palette;
            if (appToolTip is not null) appToolTip.Palette = palette;
            ApplyThemeAssets(themeName);
            ApplyThemeRecursive(this);
            optimizerMonitor?.ApplyTheme(palette);
            multibandForm?.ApplyPalette(palette);
            background.Invalidate();
            loadingOverlay.Invalidate();
            launchChoiceOverlay.Invalidate();
        }
        finally
        {
            ResumeLayout(false);
        }
    }

    private void RandomizeThemeForLaunch()
    {
        if (!settings.RandomizeThemeAtLaunch) return;
        var themes = Palettes.Keys
            .Where(theme => !DefaultThemeNames.Contains(theme))
            .ToList();
        if (themes.Count == 0) return;
        var currentTheme = NormalizeThemeName(settings.Theme);
        var candidates = themes.Where(theme => !theme.Equals(currentTheme, StringComparison.OrdinalIgnoreCase)).ToList();
        settings.Theme = candidates.Count > 0 ? candidates[Random.Shared.Next(candidates.Count)] : themes[Random.Shared.Next(themes.Count)];
        SaveSettings(settings);
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
            multibandButton.BringToFront();
            whatsNewButton.BringToFront();
            helpButton.BringToFront();
            newsBandroll.BringToFront();
            backgroundVideo.Stop();
            backgroundVideo.Source = new Uri(video, UriKind.Absolute);
            videoHost.Visible = true;
            videoHost.SendToBack();
            mascotTimer.Stop();
            backgroundVideo.Play();
            ApplyResponsiveLayout();
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
        ApplyResponsiveLayout();
        UpdateMascotOverlay();
        settingsButton.BringToFront();
        killGameButton.BringToFront();
        multibandButton.BringToFront();
        whatsNewButton.BringToFront();
        helpButton.BringToFront();
        newsBandroll.BringToFront();
        statusPill.BringToFront();
        if (newsOverlay.Visible) newsOverlay.BringToFront();
        if (settingsDrawerOpen) settingsDrawer.BringToFront();
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
        return PickThemeAssets(folder, extensions).FirstOrDefault();
    }

    private static List<string> PickThemeAssets(string folder, string[] extensions)
    {
        if (!Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder)
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                case NewsBandrollControl bandroll:
                    bandroll.Palette = palette;
                    bandroll.BackColor = Color.Transparent;
                    break;
                case NewsPillButton newsButton:
                    newsButton.Palette = palette;
                    newsButton.ForeColor = Color.White;
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
                    flow.BackColor = ReferenceEquals(flow, newsListPanel)
                        ? palette.ListBack
                        : ReferenceEquals(flow, bandButtonPanel) ? Color.FromArgb(255, palette.Card) : Color.Transparent;
                    break;
                case AccountRosterGrid roster:
                    roster.Palette = palette;
                    break;
                case BandMemberChecklist checklist:
                    checklist.Palette = palette;
                    break;
                case LoadingOverlayPanel overlay:
                    overlay.Palette = palette;
                    break;
                case ListBox list:
                    list.BackColor = palette.ListBack;
                    list.ForeColor = palette.Text;
                    break;
                case ListView listView:
                    listView.BackColor = palette.ListBack;
                    listView.ForeColor = palette.Text;
                    break;
                case TextBox text:
                    text.BackColor = palette.ListBack;
                    text.ForeColor = palette.Text;
                    break;
                case RichTextBox richText:
                    richText.BackColor = palette.ListBack;
                    richText.ForeColor = palette.Text;
                    break;
                case ComboBox combo:
                    combo.BackColor = palette.ListBack;
                    combo.ForeColor = palette.Text;
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
        var path = SettingsPath();
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                SaveMigratedAccountListStateFromSettings(json);
                var cleanedJson = SettingsMigration.CleanSettingsJson(json, out var changed);
                if (changed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, cleanedJson);
                }

                return JsonSerializer.Deserialize<AppSettings>(cleanedJson) ?? new AppSettings();
            }
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
            SettingsMigration.CleanSettings(settings);
            var path = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static AccountListState LoadAccountListState()
    {
        try
        {
            var path = AccountListStatePath();
            if (File.Exists(path))
            {
                var loadedState = JsonSerializer.Deserialize<AccountListState>(File.ReadAllText(path)) ?? new AccountListState();
                if (SettingsMigration.CleanAccountListState(loadedState)) SaveAccountListState(loadedState);
                return loadedState;
            }
        }
        catch { }
        var state = MigrateAccountListStateFromSettings();
        SettingsMigration.CleanAccountListState(state);
        SaveAccountListState(state);
        return state;
    }

    private static void SaveMigratedAccountListStateFromSettings(string settingsJson)
    {
        try
        {
            if (File.Exists(AccountListStatePath())) return;
            var state = MigrateAccountListStateFromSettings(settingsJson);
            if (state.SharedAccountOrder.Count == 0 &&
                state.InstancedAccountOrder.Count == 0 &&
                state.LastConnectedUtc.Count == 0)
            {
                return;
            }

            SettingsMigration.CleanAccountListState(state);
            SaveAccountListState(state);
        }
        catch { }
    }

    private static AccountListState MigrateAccountListStateFromSettings()
    {
        try
        {
            var path = SettingsPath();
            return File.Exists(path)
                ? MigrateAccountListStateFromSettings(File.ReadAllText(path))
                : new AccountListState();
        }
        catch
        {
            return new AccountListState();
        }
    }

    private static AccountListState MigrateAccountListStateFromSettings(string settingsJson)
    {
        var state = new AccountListState();
        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return state;

            state.SharedAccountOrder = ReadStringList(document.RootElement, "SharedAccountOrder");
            state.InstancedAccountOrder = ReadStringList(document.RootElement, "InstancedAccountOrder");
            if (document.RootElement.TryGetProperty("LastConnectedUtc", out var lastConnected) &&
                lastConnected.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in lastConnected.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(property.Value.GetString(), out var value))
                    {
                        state.LastConnectedUtc[property.Name] = value;
                    }
                }
            }
        }
        catch { }
        SettingsMigration.CleanAccountListState(state);
        return state;
    }

    private static List<string> ReadStringList(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array) return [];
        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!)
            .ToList();
    }

    private static void SaveAccountListState(AccountListState state)
    {
        try
        {
            var path = AccountListStatePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static HttpClient CreateLodestoneClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher (+https://github.com/Naru6780/potato-launcher)");
        return client;
    }

    internal static string AccountIconKey(Account account)
    {
        return string.IsNullOrWhiteSpace(account.AccountKey) ? account.BatchFile : account.AccountKey;
    }

    private static string AccountIconFileName(string accountKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(accountKey));
        return $"{Convert.ToHexString(hash).ToLowerInvariant()}.jpg";
    }

    private static string AccountFullImageFileName(string accountKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"full:{accountKey}"));
        return $"{Convert.ToHexString(hash).ToLowerInvariant()}.jpg";
    }

    private static string AccountIconPath(AccountIconProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.IconFileName)
            ? ""
            : Path.Combine(AccountIconsFolder(), profile.IconFileName);
    }

    private static string AccountFullImagePath(AccountIconProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.FullImageFileName)
            ? ""
            : Path.Combine(AccountIconsFolder(), profile.FullImageFileName);
    }

    private static bool persistentDataMigrationChecked;

    internal static string PersistentDataRoot()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Potato Launcher");
    }

    internal static string SettingsPath()
    {
        EnsurePersistentDataMigrated();
        return Path.Combine(PersistentDataRoot(), "settings.json");
    }

    internal static string AccountListStatePath()
    {
        EnsurePersistentDataMigrated();
        return Path.Combine(PersistentDataRoot(), "accountList.json");
    }

    internal static string OptimizerSettingsPath()
    {
        EnsurePersistentDataMigrated();
        return Path.Combine(PersistentDataRoot(), "optimizer.json");
    }

    private static string GetAssetRoot() => Path.Combine(AppContext.BaseDirectory, "Potato Launcher Assets");
    internal static string AccountIconsFolder()
    {
        EnsurePersistentDataMigrated();
        return Path.Combine(PersistentDataRoot(), "Account Icons");
    }

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
        Directory.CreateDirectory(AccountIconsFolder());
        Directory.CreateDirectory(GetLoadingGifFolder());
        Directory.CreateDirectory(Path.Combine(GetAssetRoot(), "themes"));
    }

    private static void EnsurePersistentDataMigrated()
    {
        if (persistentDataMigrationChecked) return;
        persistentDataMigrationChecked = true;

        var dataRoot = PersistentDataRoot();
        Directory.CreateDirectory(dataRoot);
        CopyPortableFileIfMissing("settings.json");
        CopyPortableFileIfMissing("accountList.json");
        CopyPortableFileIfMissing("optimizer.json");
        CopyPortableFileIfMissing("band.json");
        CopyPortableAccountIconsIfMissing();

        void CopyPortableFileIfMissing(string fileName)
        {
            var source = Path.Combine(AppContext.BaseDirectory, fileName);
            var target = Path.Combine(dataRoot, fileName);
            if (!File.Exists(source) || File.Exists(target)) return;
            File.Copy(source, target);
        }

        void CopyPortableAccountIconsIfMissing()
        {
            var sourceFolder = Path.Combine(GetAssetRoot(), "Account Icons");
            var targetFolder = Path.Combine(dataRoot, "Account Icons");
            Directory.CreateDirectory(targetFolder);
            if (!Directory.Exists(sourceFolder)) return;

            foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder))
            {
                var targetFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
                if (!File.Exists(targetFile)) File.Copy(sourceFile, targetFile);
            }
        }
    }

    private RoundedPanel Card(int x, int y, int width, int height) => new() { Bounds = new Rectangle(x, y, width, height), Radius = 22 };
    private Label Header(string text, int x, int y, int width, int height) => new() { Text = text, Font = new Font("Segoe UI", 16F, FontStyle.Bold), Bounds = new Rectangle(x, y, width, height), BackColor = Color.Transparent };
    private Label Label(string text, int x, int y, int width, int height) => new() { Text = text, Bounds = new Rectangle(x, y, width, height), BackColor = Color.Transparent };
    private Button Button(string text, int x, int y, int width, int height, string role)
    {
        return new NewsPillButton
        {
            Text = text,
            Bounds = new Rectangle(x, y, width, height),
            Tag = role,
            Palette = palette,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };
    }

    private Button NewsPillButton(int x, int y, int width, int height)
    {
        return new NewsPillButton
        {
            Text = "NEWS",
            Bounds = new Rectangle(x, y, width, height),
            Tag = "News",
            Palette = palette
        };
    }

    private static IDisposable BeginRedrawScope(params Control[] controls)
    {
        return new RedrawScope(controls);
    }

    private static void EnableSmoothRendering(Control root)
    {
        TrySetDoubleBuffered(root);
        foreach (Control child in root.Controls)
        {
            EnableSmoothRendering(child);
        }
    }

    private static void TrySetDoubleBuffered(Control control)
    {
        if (control is TextBoxBase or ComboBox) return;
        try
        {
            var property = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            property?.SetValue(control, true);
        }
        catch
        {
        }
    }

    private sealed class RedrawScope : IDisposable
    {
        private readonly List<Control> controls = [];
        private bool disposed;

        public RedrawScope(IEnumerable<Control> controls)
        {
            foreach (var control in controls)
            {
                if (!control.IsHandleCreated) continue;
                SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
                this.controls.Add(control);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var control in controls)
            {
                if (control.IsDisposed) continue;
                SendMessage(control.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                control.Invalidate(true);
            }
        }
    }

}

internal sealed class CuteBackgroundPanel : Panel
{
    private readonly System.Windows.Forms.Timer timer = new();
    private Image? backgroundArt;
    private float tick;
    private bool animateBubbles;
    public ThemePalette Palette { get; set; } = new(Color.FromArgb(255,226,242), Color.FromArgb(210,236,255), Color.White, Color.White, Color.Black, Color.Gray, Color.HotPink, Color.CornflowerBlue, Color.IndianRed, Color.White);
    public bool AnimateBubbles
    {
        get => animateBubbles;
        set
        {
            if (animateBubbles == value) return;
            animateBubbles = value;
            if (animateBubbles) timer.Start();
            else timer.Stop();
            Invalidate(ClientRectangle, false);
        }
    }

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
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        timer.Interval = 33;
        timer.Tick += (_, _) =>
        {
            if (!Visible) return;
            tick += 0.018f;
            Invalidate(ClientRectangle, false);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            backgroundArt?.Dispose();
        }
        base.Dispose(disposing);
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

internal sealed class AppToolTip : Form
{
    private readonly Form owner;
    private readonly Label titleLabel;
    private readonly Label bodyLabel;
    private ThemePalette palette = MainForm.Palettes["Pink"];

    public ThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            ApplyPalette();
        }
    }

    public AppToolTip(Form owner)
    {
        this.owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(258, 82);
        DoubleBuffered = true;
        Padding = new Padding(14, 10, 14, 10);

        titleLabel = new Label
        {
            Bounds = new Rectangle(14, 9, 230, 22),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        bodyLabel = new Label
        {
            Bounds = new Rectangle(14, 32, 230, 38),
            Font = new Font("Segoe UI", 9F),
            BackColor = Color.Transparent
        };
        Controls.AddRange([titleLabel, bodyLabel]);
        ApplyPalette();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExToolWindow = 0x00000080;
            const int wsExNoActivate = 0x08000000;
            var createParams = base.CreateParams;
            createParams.ExStyle |= wsExToolWindow | wsExNoActivate;
            return createParams;
        }
    }

    public void Attach(Control control, string title, string body)
    {
        control.MouseEnter += (_, _) => ShowFor(control, title, body);
        control.MouseMove += (_, _) => PositionNear(control);
        control.MouseLeave += (_, _) => Hide();
        control.Disposed += (_, _) => Hide();
    }

    private void ShowFor(Control control, string title, string body)
    {
        titleLabel.Text = title;
        bodyLabel.Text = body;
        PositionNear(control);
        if (!Visible) Show(owner);
        BringToFront();
    }

    private void PositionNear(Control control)
    {
        if (owner.IsDisposed || !control.IsHandleCreated) return;
        var screenPoint = control.PointToScreen(new Point(0, control.Height + 8));
        var workingArea = Screen.FromControl(control).WorkingArea;
        var x = Math.Min(screenPoint.X, workingArea.Right - Width - 8);
        var y = screenPoint.Y + Height > workingArea.Bottom
            ? control.PointToScreen(new Point(0, -Height - 8)).Y
            : screenPoint.Y;
        Location = new Point(Math.Max(workingArea.Left + 8, x), Math.Max(workingArea.Top + 8, y));
    }

    private void ApplyPalette()
    {
        BackColor = Color.FromArgb(255, palette.Card);
        titleLabel.ForeColor = palette.Text;
        bodyLabel.ForeColor = palette.Muted;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var fill = new SolidBrush(Color.FromArgb(255, palette.Card));
        using var border = new Pen(palette.Border, 1.4F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
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

internal sealed class LoadingOverlayPanel : Panel
{
    private ThemePalette palette = new(Color.FromArgb(255,226,242), Color.FromArgb(210,236,255), Color.White, Color.White, Color.Black, Color.Gray, Color.HotPink, Color.CornflowerBlue, Color.IndianRed, Color.White);

    public ThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            Invalidate();
        }
    }

    public LoadingOverlayPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(palette.Card);
        e.Graphics.FillRectangle(background, ClientRectangle);
        using var border = new Pen(Color.FromArgb(90, palette.Border), 1F);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class BandMemberChecklist : ScrollableControl
{
    private readonly List<Account> items = [];
    private readonly HashSet<string> checkedBatchFiles = new(StringComparer.OrdinalIgnoreCase);
    private int selectedIndex = -1;
    private int updateDepth;
    private ThemePalette palette = MainForm.Palettes["Pink"];

    public event EventHandler? CheckedChanged;

    public IEnumerable<Account> CheckedAccounts => items.Where(item => checkedBatchFiles.Contains(item.BatchFile)).ToList();

    public ThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            BackColor = palette.ListBack;
            ForeColor = palette.Text;
            Invalidate();
        }
    }

    public BandMemberChecklist()
    {
        AutoScroll = true;
        DoubleBuffered = true;
        TabStop = true;
        BackColor = palette.ListBack;
        ForeColor = palette.Text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        UpdateStyles();
    }

    public void BeginUpdate()
    {
        updateDepth++;
    }

    public void EndUpdate()
    {
        if (updateDepth <= 0) return;
        updateDepth--;
        if (updateDepth == 0)
        {
            UpdateScrollSize();
            Invalidate();
        }
    }

    public void SetAccounts(IEnumerable<Account> accounts, IEnumerable<string> checkedFiles)
    {
        BeginUpdate();
        try
        {
            items.Clear();
            items.AddRange(accounts);
            checkedBatchFiles.Clear();
            foreach (var file in checkedFiles.Where(file => !string.IsNullOrWhiteSpace(file)))
            {
                checkedBatchFiles.Add(file);
            }

            if (selectedIndex >= items.Count) selectedIndex = items.Count - 1;
            if (items.Count == 0) selectedIndex = -1;
        }
        finally
        {
            EndUpdate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollSize();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Space || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (items.Count == 0) return;

        var layout = CurrentLayout();
        switch (e.KeyCode)
        {
            case Keys.Left:
                selectedIndex = Math.Max(0, selectedIndex - 1);
                e.Handled = true;
                Invalidate();
                break;
            case Keys.Right:
                selectedIndex = Math.Min(items.Count - 1, selectedIndex + 1);
                e.Handled = true;
                Invalidate();
                break;
            case Keys.Up:
                selectedIndex = Math.Max(0, selectedIndex - layout.ColumnCount);
                e.Handled = true;
                Invalidate();
                break;
            case Keys.Down:
                selectedIndex = Math.Min(items.Count - 1, selectedIndex + layout.ColumnCount);
                e.Handled = true;
                Invalidate();
                break;
            case Keys.Space:
                ToggleSelected();
                e.Handled = true;
                break;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var hit = HitTest(e.Location);
        if (hit < 0) return;
        selectedIndex = hit;
        if (e.Button == MouseButtons.Left)
        {
            ToggleChecked(hit);
        }
        else
        {
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(palette.ListBack);
        e.Graphics.FillRectangle(background, ClientRectangle);
        if (items.Count == 0) return;

        var origin = AutoScrollPosition;
        for (var index = 0; index < items.Count; index++)
        {
            var bounds = ItemBounds(index);
            bounds.Offset(origin);
            if (bounds.Bottom < 0 || bounds.Top > ClientSize.Height) continue;
            DrawItem(e.Graphics, index, bounds);
        }
    }

    private void ToggleSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= items.Count) return;
        ToggleChecked(selectedIndex);
    }

    private void ToggleChecked(int index)
    {
        var account = items[index];
        if (!checkedBatchFiles.Remove(account.BatchFile))
        {
            checkedBatchFiles.Add(account.BatchFile);
        }
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DrawItem(Graphics graphics, int index, Rectangle bounds)
    {
        var account = items[index];
        var isChecked = checkedBatchFiles.Contains(account.BatchFile);
        var isSelected = index == selectedIndex;
        if (isSelected)
        {
            using var selectedBrush = new SolidBrush(Color.FromArgb(42, palette.Primary));
            graphics.FillRectangle(selectedBrush, bounds);
        }

        var checkTop = bounds.Top + (bounds.Height - BandChecklistLayoutMetrics.CheckSize) / 2;
        var checkBounds = new Rectangle(bounds.Left + 4, checkTop, BandChecklistLayoutMetrics.CheckSize, BandChecklistLayoutMetrics.CheckSize);
        using var checkBack = new SolidBrush(isChecked ? palette.Primary : Color.FromArgb(245, palette.ListBack));
        using var checkBorder = new Pen(isChecked ? palette.Primary : palette.Border, 1.4F);
        graphics.FillRectangle(checkBack, checkBounds);
        graphics.DrawRectangle(checkBorder, checkBounds);
        if (isChecked)
        {
            using var checkPen = new Pen(Color.White, 2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLines(checkPen, new[]
            {
                new Point(checkBounds.Left + 3, checkBounds.Top + 8),
                new Point(checkBounds.Left + 7, checkBounds.Bottom - 4),
                new Point(checkBounds.Right - 3, checkBounds.Top + 4)
            });
        }

        var textBounds = new Rectangle(checkBounds.Right + 8, bounds.Top + 2, bounds.Width - checkBounds.Width - 16, bounds.Height - 4);
        using var textBrush = new SolidBrush(palette.Text);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(account.ToString(), Font, textBrush, textBounds, format);

        if (Focused && isSelected)
        {
            using var focusPen = new Pen(Color.FromArgb(150, palette.Secondary), 1F) { DashStyle = DashStyle.Dot };
            graphics.DrawRectangle(focusPen, new Rectangle(bounds.Left + 1, bounds.Top + 1, bounds.Width - 3, bounds.Height - 3));
        }
    }

    private int HitTest(Point point)
    {
        var scrolledPoint = new Point(point.X - AutoScrollPosition.X, point.Y - AutoScrollPosition.Y);
        for (var index = 0; index < items.Count; index++)
        {
            if (ItemBounds(index).Contains(scrolledPoint)) return index;
        }
        return -1;
    }

    private Rectangle ItemBounds(int index)
    {
        var layout = CurrentLayout();
        var row = index / layout.ColumnCount;
        var column = index % layout.ColumnCount;
        return new Rectangle(
            BandChecklistLayoutMetrics.Padding + column * (layout.ColumnWidth + BandChecklistLayoutMetrics.ColumnGap),
            BandChecklistLayoutMetrics.Padding + row * BandChecklistLayoutMetrics.RowHeight,
            layout.ColumnWidth,
            BandChecklistLayoutMetrics.RowHeight);
    }

    private BandChecklistLayoutMetrics CurrentLayout()
    {
        return BandChecklistLayoutMetrics.Calculate(ClientSize.Width, items.Count);
    }

    private void UpdateScrollSize()
    {
        if (updateDepth > 0) return;
        var layout = CurrentLayout();
        AutoScrollMinSize = new Size(0, layout.ScrollHeight);
    }
}

internal sealed class AccountRosterGrid : ScrollableControl
{
    private readonly List<AccountRosterItem> items = [];
    private readonly Dictionary<string, Image> imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolTip tooltip = new();
    private string currentTooltip = "";
    private int selectedIndex = -1;
    private ThemePalette palette = new(Color.White, Color.White, Color.White, Color.LightGray, Color.Black, Color.Gray, Color.HotPink, Color.CornflowerBlue, Color.IndianRed, Color.White);

    public event EventHandler? AccountActivated;
    public event EventHandler<AccountContextEventArgs>? AccountContextRequested;
    public event EventHandler<AccountReorderEventArgs>? AccountReordered;

    public Account? SelectedAccount => selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex].Account : null;
    private int dragIndex = -1;
    private Point dragStart;
    private Point dragPoint;
    private bool dragging;

    public ThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            BackColor = palette.ListBack;
            Invalidate();
        }
    }

    public AccountRosterGrid()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        AllowDrop = true;
        BackColor = palette.ListBack;
        tooltip.BackColor = Color.FromArgb(255, 252, 255);
        tooltip.ForeColor = Color.FromArgb(92, 48, 104);
        tooltip.InitialDelay = 450;
        tooltip.ReshowDelay = 120;
        tooltip.AutoPopDelay = 8000;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        UpdateStyles();
    }

    public void SetItems(IEnumerable<AccountRosterItem> rosterItems)
    {
        foreach (var image in imageCache.Values)
        {
            image.Dispose();
        }
        imageCache.Clear();
        items.Clear();
        items.AddRange(rosterItems);
        if (selectedIndex >= items.Count) selectedIndex = items.Count - 1;
        if (items.Count == 0) selectedIndex = -1;
        UpdateScrollSize();
        Invalidate();
    }

    public void SelectAccount(Account account)
    {
        var key = MainForm.AccountIconKey(account);
        selectedIndex = items.FindIndex(item => MainForm.AccountIconKey(item.Account).Equals(key, StringComparison.OrdinalIgnoreCase));
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollSize();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var backgroundBrush = new SolidBrush(palette.ListBack);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
        if (items.Count == 0) return;

        var origin = AutoScrollPosition;
        for (var index = 0; index < items.Count; index++)
        {
            if (dragging && index == dragIndex) continue;
            var bounds = TileBounds(index);
            bounds.Offset(origin);
            if (bounds.Bottom < 0 || bounds.Top > ClientSize.Height) continue;
            DrawTile(e.Graphics, index, bounds);
        }
        if (dragging)
        {
            DrawInsertionMarker(e.Graphics, DropIndex(dragPoint));
        }
        if (dragging && dragIndex >= 0 && dragIndex < items.Count)
        {
            var layout = CurrentLayout();
            DrawTile(e.Graphics, dragIndex, new Rectangle(dragPoint.X - layout.TileWidth / 2, dragPoint.Y - layout.TileHeight / 2, layout.TileWidth, layout.TileHeight), ghost: true);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var hit = HitTest(e.Location);
        if (hit < 0) return;
        selectedIndex = hit;
        if (e.Button == MouseButtons.Left)
        {
            dragIndex = hit;
            dragStart = e.Location;
            dragPoint = e.Location;
        }
        Invalidate();
        if (e.Button == MouseButtons.Right)
        {
            AccountContextRequested?.Invoke(this, new AccountContextEventArgs(items[hit].Account, e.Location));
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var hit = HitTest(e.Location);
        if (hit < 0) return;
        selectedIndex = hit;
        AccountActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left && dragIndex >= 0 && dragIndex < items.Count)
        {
            if (dragging || IsDragGesture(dragStart, e.Location))
            {
                dragging = true;
                dragPoint = e.Location;
                Invalidate();
                return;
            }
        }
        if (dragging)
        {
            dragging = false;
            dragIndex = -1;
            Invalidate();
            return;
        }
        var hit = HitTest(e.Location);
        var nextTooltip = hit >= 0 ? items[hit].Tooltip.Replace("\n", Environment.NewLine) : "";
        if (!nextTooltip.Equals(currentTooltip, StringComparison.Ordinal))
        {
            currentTooltip = nextTooltip;
            tooltip.SetToolTip(this, currentTooltip);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging || dragIndex < 0 || dragIndex >= items.Count)
        {
            dragging = false;
            dragIndex = -1;
            return;
        }
        var account = items[dragIndex].Account;
        dragging = false;
        dragIndex = -1;
        Invalidate();
        AccountReordered?.Invoke(this, new AccountReorderEventArgs(account, DropIndex(e.Location)));
    }

    private void DrawTile(Graphics graphics, int index, Rectangle bounds, bool ghost = false)
    {
        var item = items[index];
        var selected = index == selectedIndex;
        using var tileBrush = new SolidBrush(ghost ? Color.FromArgb(220, palette.Primary) : selected ? Color.FromArgb(235, palette.Primary) : Color.FromArgb(90, palette.Card));
        using var borderPen = new Pen(ghost ? palette.Secondary : selected ? palette.Secondary : Color.FromArgb(150, palette.Border), selected || ghost ? 3 : 1);
        using var path = Rounded(bounds, 10);
        graphics.FillPath(tileBrush, path);
        graphics.DrawPath(borderPen, path);

        var layout = CurrentLayout();
        var portraitBounds = new Rectangle(bounds.X + (bounds.Width - layout.PortraitSize) / 2, bounds.Y + 6, layout.PortraitSize, layout.PortraitSize);
        if (!string.IsNullOrWhiteSpace(item.FacePath) && File.Exists(item.FacePath))
        {
            var image = GetImage(item.FacePath);
            using var clip = Rounded(portraitBounds, 8);
            graphics.SetClip(clip);
            DrawImageCover(graphics, image, portraitBounds);
            graphics.ResetClip();
            using var portraitPen = new Pen(Color.FromArgb(220, Color.White), 1);
            graphics.DrawPath(portraitPen, clip);
        }
        else
        {
            using var missingBrush = new SolidBrush(Color.FromArgb(60, palette.Danger));
            using var missingPen = new Pen(palette.Danger, 1);
            graphics.FillRectangle(missingBrush, portraitBounds);
            graphics.DrawRectangle(missingPen, portraitBounds);
            using var refreshFont = new Font(Font.FontFamily, Math.Clamp(layout.TileWidth / 11F, 6.2F, 7.6F), FontStyle.Bold);
            DrawCenteredText(graphics, "No Data Found", portraitBounds, refreshFont, Color.White);
        }

        var nameTop = portraitBounds.Bottom + 3;
        var nameBounds = new Rectangle(bounds.X + 3, nameTop, bounds.Width - 6, bounds.Bottom - nameTop - 3);
        using var nameFont = new Font(Font.FontFamily, Math.Clamp(layout.TileWidth / 9.4F, 7.2F, 9F), FontStyle.Bold);
        DrawCenteredText(graphics, item.DisplayName, nameBounds, nameFont, selected ? Color.White : palette.Text);
    }

    private Image GetImage(string path)
    {
        if (imageCache.TryGetValue(path, out var cached)) return cached;
        using var source = Image.FromFile(path);
        var copy = new Bitmap(source);
        imageCache[path] = copy;
        return copy;
    }

    private static void DrawImageCover(Graphics graphics, Image image, Rectangle bounds)
    {
        var scale = Math.Max((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var x = bounds.X + (bounds.Width - width) / 2F;
        var y = bounds.Y + (bounds.Height - height) / 2F;
        graphics.DrawImage(image, x, y, width, height);
    }

    private static void DrawCenteredText(Graphics graphics, string text, Rectangle bounds, Font font, Color color)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private int HitTest(Point point)
    {
        var scrolledPoint = new Point(point.X - AutoScrollPosition.X, point.Y - AutoScrollPosition.Y);
        for (var index = 0; index < items.Count; index++)
        {
            if (TileBounds(index).Contains(scrolledPoint)) return index;
        }
        return -1;
    }

    private int DropIndex(Point point)
    {
        var hit = HitTest(point);
        if (hit < 0) return items.Count;
        var bounds = TileBounds(hit);
        bounds.Offset(AutoScrollPosition);
        if (CurrentLayout().ColumnCount > 1)
        {
            return point.X > bounds.Left + bounds.Width / 2 ? hit + 1 : hit;
        }
        return point.Y > bounds.Top + bounds.Height / 2 ? hit + 1 : hit;
    }

    private void DrawInsertionMarker(Graphics graphics, int dropIndex)
    {
        if (items.Count == 0) return;
        var marker = InsertionMarkerBounds(dropIndex);
        marker.Offset(AutoScrollPosition);
        using var glowBrush = new SolidBrush(Color.FromArgb(80, palette.Secondary));
        using var markerBrush = new SolidBrush(palette.Secondary);
        using var outlinePen = new Pen(Color.FromArgb(230, Color.White), 1.5F);
        using var glowPath = Rounded(new Rectangle(marker.X - 4, marker.Y - 3, marker.Width + 8, marker.Height + 6), 5);
        using var markerPath = Rounded(marker, 3);
        graphics.FillPath(glowBrush, glowPath);
        graphics.FillPath(markerBrush, markerPath);
        graphics.DrawPath(outlinePen, markerPath);
    }

    private Rectangle InsertionMarkerBounds(int dropIndex)
    {
        dropIndex = Math.Clamp(dropIndex, 0, items.Count);
        var bounds = TileBounds(dropIndex);
        var layout = CurrentLayout();
        var columns = layout.ColumnCount;
        var column = dropIndex % columns;
        var markerX = column == 0 ? bounds.Left - 5 : bounds.Left - layout.TileGap / 2 - 2;
        return new Rectangle(markerX, bounds.Top + 8, 5, bounds.Height - 16);
    }

    private static bool IsDragGesture(Point start, Point current)
    {
        return Math.Abs(current.X - start.X) >= SystemInformation.DragSize.Width / 2 ||
            Math.Abs(current.Y - start.Y) >= SystemInformation.DragSize.Height / 2;
    }

    private Rectangle TileBounds(int index)
    {
        var layout = CurrentLayout();
        var columns = layout.ColumnCount;
        var row = index / columns;
        var column = index % columns;
        var contentWidth = columns * layout.TileWidth + (columns - 1) * layout.TileGap;
        var startX = Math.Max(0, (ClientSize.Width - SystemInformation.VerticalScrollBarWidth - contentWidth) / 2);
        return new Rectangle(startX + column * (layout.TileWidth + layout.TileGap), layout.TileGap + row * (layout.TileHeight + layout.TileGap), layout.TileWidth, layout.TileHeight);
    }

    private AccountRosterLayoutMetrics CurrentLayout() => AccountRosterLayoutMetrics.Calculate(ClientSize.Width);

    private void UpdateScrollSize()
    {
        var layout = CurrentLayout();
        var columns = layout.ColumnCount;
        var rows = items.Count == 0 ? 0 : (int)Math.Ceiling(items.Count / (double)columns);
        AutoScrollMinSize = new Size(0, rows * (layout.TileHeight + layout.TileGap) + layout.TileGap);
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tooltip.Dispose();
            foreach (var image in imageCache.Values)
            {
                image.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}

internal sealed class NewsPillButton : Button
{
    private bool hovered;
    private bool pressed;

    public ThemePalette Palette { get; set; } = new(Color.White, Color.White, Color.White, Color.LightGray, Color.Black, Color.Gray, Color.HotPink, Color.CornflowerBlue, Color.IndianRed, Color.White);

    public NewsPillButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        ForeColor = Color.White;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateButtonRegion();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = ClientRectangle;
        bounds.Inflate(-2, -2);
        if (pressed) bounds.Offset(0, 1);

        using var shadowPath = RoundedRectangle(new Rectangle(bounds.X, bounds.Y + 2, bounds.Width, Math.Max(1, bounds.Height - 1)), bounds.Height / 2);
        using var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        e.Graphics.FillPath(shadowBrush, shadowPath);

        using var pillPath = RoundedRectangle(bounds, bounds.Height / 2);
        var (baseLeft, baseRight) = ButtonGradientColors();
        var left = hovered ? ControlPaint.Light(baseLeft, 0.18F) : baseLeft;
        var right = hovered ? ControlPaint.Light(baseRight, 0.15F) : baseRight;
        if (pressed)
        {
            left = ControlPaint.Dark(left, 0.08F);
            right = ControlPaint.Dark(right, 0.08F);
        }

        using var gradient = new LinearGradientBrush(bounds, left, right, LinearGradientMode.Horizontal);
        e.Graphics.FillPath(gradient, pillPath);

        using var glossPath = RoundedRectangle(new Rectangle(bounds.X + 4, bounds.Y + 3, bounds.Width - 8, Math.Max(7, bounds.Height / 2 - 2)), Math.Max(4, bounds.Height / 4));
        using var glossBrush = new SolidBrush(Color.FromArgb(65, Color.White));
        e.Graphics.FillPath(glossBrush, glossPath);

        using var outlinePen = new Pen(Color.FromArgb(170, Color.White), hovered ? 2F : 1.4F);
        e.Graphics.DrawPath(outlinePen, pillPath);

        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var textFont = FittedFont(e.Graphics, Text, Font, bounds.Size);
        e.Graphics.DrawString(Text, textFont, textBrush, bounds, format);
    }

    private (Color Left, Color Right) ButtonGradientColors()
    {
        return (Tag?.ToString() ?? "Primary") switch
        {
            "Secondary" => (Palette.Secondary, Blend(Palette.Secondary, Palette.Primary, 0.28F)),
            "Danger" => (Palette.Danger, Blend(Palette.Danger, Palette.Primary, 0.18F)),
            _ => (Palette.Primary, Palette.Secondary)
        };
    }

    private static Font FittedFont(Graphics graphics, string text, Font baseFont, Size bounds)
    {
        var fontSize = baseFont.Size;
        while (fontSize > 7F)
        {
            var proposed = new Font(baseFont.FontFamily, fontSize, baseFont.Style);
            var measured = graphics.MeasureString(text, proposed);
            if (measured.Width <= bounds.Width - 14 && measured.Height <= bounds.Height - 4) return proposed;
            proposed.Dispose();
            fontSize -= 0.5F;
        }
        return new Font(baseFont.FontFamily, 7F, baseFont.Style);
    }

    private static Color Blend(Color left, Color right, float amount)
    {
        amount = Math.Clamp(amount, 0F, 1F);
        return Color.FromArgb(
            (int)MathF.Round(left.A + (right.A - left.A) * amount),
            (int)MathF.Round(left.R + (right.R - left.R) * amount),
            (int)MathF.Round(left.G + (right.G - left.G) * amount),
            (int)MathF.Round(left.B + (right.B - left.B) * amount));
    }

    private void UpdateButtonRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), Height / 2);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
        radius = Math.Min(radius, Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 2));
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

internal readonly record struct TopNavigationBandrollMetrics(Rectangle Bounds, bool Visible)
{
    public static TopNavigationBandrollMetrics Calculate(int clientWidth, int clientHeight, int leftEdge, int buttonHeight, int topY, int margin)
    {
        var mascotReserve = Math.Clamp(clientWidth / 11, 84, 120);
        var right = clientWidth - Math.Max(24, margin) - mascotReserve;
        var available = right - leftEdge;
        if (available <= 0) return new TopNavigationBandrollMetrics(Rectangle.Empty, false);

        var normalHeight = Math.Clamp((int)MathF.Round(buttonHeight * 1.55F), 52, 64);
        var compactHeight = Math.Clamp(buttonHeight, 32, 40);
        var maxWidth = Math.Max(420, clientWidth / 2);
        var width = available >= 260
            ? Math.Clamp(available - 12, 260, maxWidth)
            : Math.Clamp(available - 4, 72, Math.Max(72, available));
        var height = available >= 180 ? normalHeight : compactHeight;
        if (width < 72) return new TopNavigationBandrollMetrics(Rectangle.Empty, false);

        return new TopNavigationBandrollMetrics(
            new Rectangle(right - width, Math.Max(12, topY + (buttonHeight - height) / 2), width, height),
            true);
    }
}

internal sealed class NewsBandrollControl : Control
{
    private readonly System.Windows.Forms.Timer rollTimer = new();
    private readonly List<NewsBandrollSlide> slides = [];
    private int currentIndex;
    private int nextIndex;
    private bool animating;
    private DateTime animationStartUtc;
    private DateTime nextRollUtc = DateTime.UtcNow.AddSeconds(4);
    private const int AnimationMilliseconds = 620;
    private static readonly TimeSpan DisplayInterval = TimeSpan.FromSeconds(5);

    public event EventHandler<string>? ItemClicked;
    public ThemePalette Palette { get; set; } = new(Color.White, Color.White, Color.White, Color.LightGray, Color.Black, Color.Gray, Color.HotPink, Color.CornflowerBlue, Color.IndianRed, Color.White);
    public bool HasSlides => slides.Count > 0;

    public NewsBandrollControl()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        rollTimer.Interval = 30;
        rollTimer.Tick += (_, _) => UpdateRollAnimation();
        rollTimer.Start();
    }

    public void SetSlides(IReadOnlyList<NewsBandrollSlide> newsSlides)
    {
        foreach (var slide in slides)
        {
            slide.Image.Dispose();
        }

        slides.Clear();
        slides.AddRange(newsSlides.Where(slide => slide.Image.Width > 0 && slide.Image.Height > 0));
        currentIndex = 0;
        nextIndex = slides.Count > 1 ? 1 : 0;
        animating = false;
        nextRollUtc = DateTime.UtcNow.AddSeconds(4);
        Visible = HasSlides;
        Invalidate();
    }

    private void UpdateRollAnimation()
    {
        if (slides.Count <= 1)
        {
            animating = false;
            return;
        }

        var now = DateTime.UtcNow;
        if (!animating && now >= nextRollUtc)
        {
            nextIndex = (currentIndex + 1) % slides.Count;
            animationStartUtc = now;
            animating = true;
        }

        if (animating && (now - animationStartUtc).TotalMilliseconds >= AnimationMilliseconds)
        {
            currentIndex = nextIndex;
            animating = false;
            nextRollUtc = now + DisplayInterval;
        }

        if (Visible) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var bandPath = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
        using var bandBrush = new SolidBrush(Color.FromArgb(210, Palette.Card));
        using var borderPen = new Pen(Color.FromArgb(190, Palette.Border), 1);
        e.Graphics.FillPath(bandBrush, bandPath);
        e.Graphics.DrawPath(borderPen, bandPath);

        var imageBounds = new Rectangle(6, 5, Math.Max(1, Width - 12), Math.Max(1, Height - 10));
        using var clipPath = RoundedRectangle(imageBounds, Math.Max(8, imageBounds.Height / 2));
        using var clipRegion = new Region(clipPath);
        var oldClip = e.Graphics.Clip;
        e.Graphics.Clip = clipRegion;

        if (slides.Count == 0)
        {
            using var emptyBrush = new SolidBrush(Color.FromArgb(120, Palette.ListBack));
            e.Graphics.FillRectangle(emptyBrush, imageBounds);
        }
        else if (!animating)
        {
            DrawSlide(e.Graphics, slides[currentIndex], imageBounds, 0);
        }
        else
        {
            var progress = Math.Clamp((float)(DateTime.UtcNow - animationStartUtc).TotalMilliseconds / AnimationMilliseconds, 0F, 1F);
            progress = EaseOutCubic(progress);
            DrawSlide(e.Graphics, slides[currentIndex], imageBounds, (int)MathF.Round(-progress * imageBounds.Width));
            DrawSlide(e.Graphics, slides[nextIndex], imageBounds, (int)MathF.Round((1F - progress) * imageBounds.Width));
        }

        e.Graphics.Clip = oldClip;
        clipRegion.Dispose();
        oldClip.Dispose();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (slides.Count == 0) return;
        var slide = slides[Math.Clamp(currentIndex, 0, slides.Count - 1)];
        ItemClicked?.Invoke(this, slide.Url);
    }

    private static void DrawSlide(Graphics graphics, NewsBandrollSlide slide, Rectangle bounds, int xOffset)
    {
        var target = new Rectangle(bounds.X + xOffset, bounds.Y, bounds.Width, bounds.Height);
        var source = CoverSourceRectangle(slide.Image.Size, bounds.Size);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(slide.Image, target, source, GraphicsUnit.Pixel);
        using var edgeBrush = new LinearGradientBrush(target, Color.FromArgb(80, Color.Black), Color.Transparent, LinearGradientMode.Horizontal);
        graphics.FillRectangle(edgeBrush, target);
    }

    private static float EaseOutCubic(float progress)
    {
        progress = Math.Clamp(progress, 0F, 1F);
        return 1F - MathF.Pow(1F - progress, 3F);
    }

    private static Rectangle CoverSourceRectangle(Size imageSize, Size targetSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0 || targetSize.Width <= 0 || targetSize.Height <= 0)
        {
            return new Rectangle(Point.Empty, imageSize);
        }

        var imageRatio = imageSize.Width / (float)imageSize.Height;
        var targetRatio = targetSize.Width / (float)targetSize.Height;
        if (imageRatio > targetRatio)
        {
            var sourceWidth = (int)MathF.Round(imageSize.Height * targetRatio);
            var x = Math.Max(0, (imageSize.Width - sourceWidth) / 2);
            return new Rectangle(x, 0, Math.Min(sourceWidth, imageSize.Width), imageSize.Height);
        }

        var sourceHeight = (int)MathF.Round(imageSize.Width / targetRatio);
        var y = Math.Max(0, (imageSize.Height - sourceHeight) / 2);
        return new Rectangle(0, y, imageSize.Width, Math.Min(sourceHeight, imageSize.Height));
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
        radius = Math.Min(radius, Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 2));
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            rollTimer.Stop();
            rollTimer.Dispose();
            foreach (var slide in slides)
            {
                slide.Image.Dispose();
            }
        }
        base.Dispose(disposing);
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
