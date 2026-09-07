using System.Drawing;

namespace PotatoLauncher.Tests;

public class LayoutTests
{
    [Fact]
    public void AccountPanelMaximum_UsesAvailableWideScreenSpace()
    {
        var metrics = LauncherLayoutMetrics.Calculate(clientWidth: 2048, clientHeight: 1060, requestedAccountWidth: 1200);

        Assert.True(metrics.AccountWidth >= 1000);
        Assert.True(metrics.BandWidth >= 420);
    }

    [Fact]
    public void LauncherLayoutMetrics_CentersAndCapsVeryWideLayouts()
    {
        var metrics = LauncherLayoutMetrics.Calculate(clientWidth: 2048, clientHeight: 1060, requestedAccountWidth: 330);

        Assert.True(metrics.Margin > 160);
        Assert.True(metrics.AccountWidth >= 420);
        Assert.True(metrics.BandWidth <= 1180);
        Assert.True(metrics.ContentHeight <= 920);
    }

    [Fact]
    public void LauncherLayoutMetrics_KeepsMinimumLayoutUsable()
    {
        var metrics = LauncherLayoutMetrics.Calculate(clientWidth: 860, clientHeight: 620, requestedAccountWidth: 0);

        Assert.InRange(metrics.AccountWidth, 300, 380);
        Assert.True(metrics.BandWidth >= 420);
        Assert.True(metrics.ContentHeight >= 390);
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(0)]
    [InlineData(5000)]
    public void LauncherLayoutMetrics_ClampPanelsToUsableWidths(int requestedWidth)
    {
        var metrics = LauncherLayoutMetrics.Calculate(clientWidth: 990, clientHeight: 700, requestedAccountWidth: requestedWidth);

        Assert.True(metrics.AccountWidth >= 300);
        Assert.True(metrics.BandWidth >= 420);
        Assert.InRange(metrics.Gap, 16, 22);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(120, 40)]
    [InlineData(600, 400)]
    public void LauncherLayoutMetrics_HandlesMinimizedOrTinyClientSizes(int clientWidth, int clientHeight)
    {
        var exception = Record.Exception(() => LauncherLayoutMetrics.Calculate(clientWidth, clientHeight, requestedAccountWidth: 330));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(560)]
    [InlineData(760)]
    [InlineData(980)]
    public void BandMemberListMetrics_FitsColumnsInsideMemberList(int bandCardWidth)
    {
        var metrics = BandMemberListMetrics.Calculate(bandCardWidth);

        Assert.True(metrics.MemberWidth > 0);
        Assert.True(metrics.MemberColumnWidth >= BandMemberListMetrics.MinimumColumnWidth);
        Assert.True(metrics.ColumnCount >= 1);
        Assert.True(metrics.MemberColumnWidth * metrics.ColumnCount <= metrics.MemberWidth);
    }

    [Fact]
    public void BandMemberListMetrics_UsesResponsiveColumnWidthInsteadOfOldFixedWidth()
    {
        var metrics = BandMemberListMetrics.Calculate(760);

        Assert.True(metrics.MemberColumnWidth > 220);
        Assert.True(metrics.ListGap < 20);
    }

    [Theory]
    [InlineData(320, 44, 1)]
    [InlineData(540, 44, 2)]
    [InlineData(780, 44, 3)]
    public void BandChecklistLayoutMetrics_UsesVerticalScrollingColumns(int width, int itemCount, int expectedColumns)
    {
        var metrics = BandChecklistLayoutMetrics.Calculate(width, itemCount);

        Assert.Equal(expectedColumns, metrics.ColumnCount);
        Assert.True(metrics.ContentWidth <= width);
        Assert.True(metrics.ScrollHeight > 0);
    }

    [Fact]
    public void BandChecklistLayoutMetrics_KeepsRowsTallEnoughForCheckboxAndText()
    {
        Assert.True(BandChecklistLayoutMetrics.RowHeight >= 26);
        Assert.True(BandChecklistLayoutMetrics.CheckSize <= BandChecklistLayoutMetrics.RowHeight - 6);
    }

    [Theory]
    [InlineData(520, 426)]
    [InlineData(980, 760)]
    [InlineData(1460, 900)]
    public void LoadingOverlayMetrics_CoversBandManagerSurfaceIncludingButtons(int bandCardWidth, int bandCardHeight)
    {
        var metrics = LoadingOverlayMetrics.Calculate(bandCardWidth, bandCardHeight);

        Assert.Equal(new Rectangle(0, 0, bandCardWidth, bandCardHeight), metrics.OverlayBounds);
        Assert.True(metrics.CardBounds.Width <= metrics.OverlayBounds.Width - 24);
        Assert.True(metrics.CardBounds.Height <= metrics.OverlayBounds.Height - 16);
        Assert.True(metrics.QueueBounds.Top >= metrics.TitleBounds.Bottom);
        Assert.True(metrics.QueueBounds.Bottom <= metrics.StatusBounds.Top);
        Assert.True(metrics.CancelBounds.Bottom <= metrics.CardBounds.Height - 12);
    }

    [Theory]
    [InlineData(520, 426)]
    [InlineData(1460, 900)]
    public void LoadingOverlayMetrics_CentersModalWithoutOversizingIt(int bandCardWidth, int bandCardHeight)
    {
        var metrics = LoadingOverlayMetrics.Calculate(bandCardWidth, bandCardHeight);

        Assert.InRange(metrics.CardBounds.Width, 352, 620);
        Assert.InRange(metrics.CardBounds.Height, 228, 460);
        Assert.True(Math.Abs(metrics.CardBounds.Left - ((metrics.OverlayBounds.Width - metrics.CardBounds.Width) / 2)) <= 1);
        Assert.True(Math.Abs(metrics.CardBounds.Top - ((metrics.OverlayBounds.Height - metrics.CardBounds.Height) / 2)) <= 1);
    }

    [Theory]
    [InlineData(520, 426)]
    [InlineData(980, 760)]
    public void LoadingOverlayMetrics_ReservesQueueAreaForBandMemberStatuses(int bandCardWidth, int bandCardHeight)
    {
        var metrics = LoadingOverlayMetrics.Calculate(bandCardWidth, bandCardHeight);

        Assert.True(metrics.QueueBounds.Width > 0);
        Assert.True(metrics.QueueBounds.Height >= 36);
        Assert.True(metrics.StatusBounds.Bottom <= metrics.CancelBounds.Top);
    }

    [Fact]
    public void LoadingOverlayMetrics_QueueModeShowsSeveralBandMembersAtMinimumSize()
    {
        var metrics = LoadingOverlayMetrics.Calculate(520, 426, showQueue: true);

        Assert.Equal(new Rectangle(0, 0, 520, 426), metrics.OverlayBounds);
        Assert.Equal(0, metrics.CardBounds.Left);
        Assert.Equal(0, metrics.CardBounds.Top);
        Assert.Equal(metrics.OverlayBounds.Size, metrics.CardBounds.Size);
        Assert.False(metrics.PictureBounds.IsEmpty);
        Assert.False(metrics.StatusBounds.IsEmpty);
        Assert.True(metrics.QueueBounds.Height >= 96);
        Assert.True(metrics.QueueBounds.Bottom <= metrics.StatusBounds.Top - 6);
        Assert.True(metrics.StatusBounds.Bottom <= metrics.CancelBounds.Top);
    }

    [Theory]
    [InlineData(384)]
    [InlineData(760)]
    [InlineData(1100)]
    public void BandActionButtonMetrics_KeepsButtonsReadableAndWrappedInsidePanel(int availableWidth)
    {
        var metrics = BandActionButtonMetrics.Calculate(availableWidth);

        Assert.InRange(metrics.ButtonHeight, 36, 42);
        Assert.True(metrics.PanelHeight >= metrics.ButtonHeight);
        foreach (var rowWidth in metrics.RowWidths)
        {
            Assert.True(rowWidth <= availableWidth);
        }
        foreach (var width in metrics.ButtonWidths)
        {
            Assert.True(width >= 82);
        }
    }

    [Theory]
    [InlineData(294, 64, 48)]
    [InlineData(420, 74, 54)]
    [InlineData(620, 84, 62)]
    public void AccountRosterLayoutMetrics_ScalesTilesWithAvailablePanelWidth(int gridWidth, int minimumTileWidth, int minimumPortraitSize)
    {
        var metrics = AccountRosterLayoutMetrics.Calculate(gridWidth);

        Assert.True(metrics.TileWidth >= minimumTileWidth);
        Assert.True(metrics.PortraitSize >= minimumPortraitSize);
        Assert.True(metrics.TileHeight > metrics.TileWidth);
        Assert.True(metrics.ColumnCount >= 1);
    }

    [Theory]
    [InlineData(834, 582)]
    [InlineData(860, 590)]
    [InlineData(990, 620)]
    public void StatusPillLayoutMetrics_KeepsStatusVisibleAtMinimumClientSizes(int clientWidth, int clientHeight)
    {
        var launcher = LauncherLayoutMetrics.Calculate(clientWidth, clientHeight, requestedAccountWidth: 0);
        var status = StatusPillLayoutMetrics.Calculate(clientWidth, clientHeight, launcher);

        Assert.True(status.Bounds.Bottom <= clientHeight - 8);
        Assert.True(status.Bounds.Left >= 8);
        Assert.True(status.Bounds.Right <= clientWidth - 8);
        Assert.InRange(status.Bounds.Width, 300, 370);
    }

    [Fact]
    public void TopNavigationBandrollMetrics_RemainsVisibleAtMinimumWindowSize()
    {
        const int clientWidth = 860;
        const int clientHeight = 620;
        const int buttonHeight = 34;
        const int gap = 12;
        var launcher = LauncherLayoutMetrics.Calculate(clientWidth, clientHeight, requestedAccountWidth: 0);
        var leftEdge = Math.Max(24, launcher.Margin) + 102 + 104 + 110 + 68 + 96 + buttonHeight + gap * 6;

        var bandroll = TopNavigationBandrollMetrics.Calculate(clientWidth, clientHeight, leftEdge, buttonHeight, topY: 20, launcher.Margin);

        Assert.True(bandroll.Visible);
        Assert.False(bandroll.Bounds.IsEmpty);
        Assert.True(bandroll.Bounds.Right <= clientWidth - 24);
        Assert.True(bandroll.Bounds.Width >= 72);
    }

    [Theory]
    [InlineData(844, 581, false)]
    [InlineData(860, 620, false)]
    [InlineData(990, 700, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1920, 1080, true)]
    public void EnlargedNewsFrameReservesSpaceWithoutOverlappingNavigationOrCards(int width, int height, bool expectedInline)
    {
        var launcher = LauncherLayoutMetrics.Calculate(width, height, 0);
        var scale = Math.Clamp(width / 1100F, 1F, 1.16F);
        var buttonHeight = Math.Clamp((int)MathF.Round(34 * scale), 34, 40);
        var gap = Math.Clamp((int)MathF.Round(12 * scale), 12, 16);
        var topY = Math.Clamp(height / 32, 20, 28);
        var leftEdge = Math.Max(24, launcher.Margin) + buttonHeight + gap * 6 +
            new[] { 102, 104, 110, 68, 96 }.Sum(baseWidth => Math.Clamp((int)MathF.Round(baseWidth * scale), baseWidth, baseWidth + 26));
        var banner = TopNavigationBandrollMetrics.Calculate(width, height, leftEdge, buttonHeight, topY, launcher.Margin);
        Assert.True(banner.Visible);
        if (expectedInline) Assert.True(banner.Bounds.Left >= leftEdge);
        else Assert.True(banner.Bounds.Top >= topY + buttonHeight + 8);
        Assert.True(banner.Bounds.Width >= 320);
        Assert.True(banner.Bounds.Height >= 64);
        var expanded = launcher.ReserveHeader(banner.Bounds.Bottom, height);
        Assert.True(expanded.Top >= banner.Bounds.Bottom + 12);
        Assert.True(expanded.ContentHeight >= 390);
        var status = StatusPillLayoutMetrics.Calculate(width, height, expanded);
        Assert.True(status.Bounds.Top >= expanded.Top + expanded.ContentHeight + 10);
        Assert.True(banner.Bounds.Right <= width - Math.Max(24, launcher.Margin) - Math.Clamp(width / 11, 84, 120));
        var image = NewsBandrollControl.FitImageRectangle(new Size(600, 180),
            new Rectangle(6, 5, banner.Bounds.Width - 12, banner.Bounds.Height - 10));
        Assert.True(image.Width >= 180 && image.Height >= 54);
    }

    [Fact]
    public void NewsFrameHidesWhenThereIsNoUsableHeaderSpace()
    {
        var banner = TopNavigationBandrollMetrics.Calculate(300, 300, 620, 34, 20, 24);
        Assert.False(banner.Visible);
        Assert.Equal(Rectangle.Empty, banner.Bounds);
    }
}
