using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

internal static class ArtemisWelcomeContent
{
    public static readonly string[] CuteQuotes =
    [
        "May your crystals stay bright and your queue stay short!",
        "A brave little potato can cross all of Eorzea.",
        "Even the smallest adventurer can carry a great light.",
        "Kupo! Today feels perfect for an adventure.",
        "The moon is watching—give it your best smile!",
        "A chocobo ride fixes almost everything.",
        "By the Twelve, you look ready for a little mischief.",
        "Warm cocoa and rested EXP—an adventurer's dream.",
        "Keep your courage close and your minions closer.",
        "Every grand journey begins with one tiny step.",
        "The Crystal has excellent taste—it chose you!",
        "Eorzea is brighter whenever you log in."
    ];

    public static string PickQuote() => CuteQuotes[Random.Shared.Next(CuteQuotes.Length)];
}

internal static class ArtemisSpriteSheetLayout
{
    public const int Columns = 4;
    public const int Rows = 3;
    public const int FrameCount = Columns * Rows;

    public static Rectangle SourceFrameBounds(Size sheetSize, int frameIndex)
    {
        frameIndex = Math.Clamp(frameIndex, 0, FrameCount - 1);
        var column = frameIndex % Columns;
        var row = frameIndex / Columns;
        var left = column * sheetSize.Width / Columns;
        var right = (column + 1) * sheetSize.Width / Columns;
        var top = row * sheetSize.Height / Rows;
        var bottom = (row + 1) * sheetSize.Height / Rows;
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

}

internal readonly record struct ArtemisWelcomeFrame(ArtemisAnimationState State, int FrameIndex);

internal static class ArtemisWelcomeTimeline
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    public static ArtemisWelcomeFrame FrameAt(TimeSpan elapsed, bool reduceMotion = false)
    {
        if (reduceMotion) return new ArtemisWelcomeFrame(ArtemisAnimationState.Idle, 0);
        var milliseconds = Math.Clamp(elapsed.TotalMilliseconds, 0, Duration.TotalMilliseconds);
        if (milliseconds < 520)
        {
            return new ArtemisWelcomeFrame(
                ArtemisAnimationState.Idle,
                Math.Min(4, (int)(milliseconds / 104)));
        }
        if (milliseconds < 2160)
        {
            var waveProgress = (milliseconds - 520) / 1640;
            return new ArtemisWelcomeFrame(
                ArtemisAnimationState.Wave,
                Math.Clamp((int)(waveProgress * ArtemisSpriteSheetLayout.FrameCount), 0, ArtemisSpriteSheetLayout.FrameCount - 1));
        }

        var settleProgress = (milliseconds - 2160) / 840;
        return new ArtemisWelcomeFrame(
            ArtemisAnimationState.Idle,
            Math.Clamp(7 + (int)(settleProgress * 5), 7, ArtemisSpriteSheetLayout.FrameCount - 1));
    }
}

internal sealed class StartupApplicationContext : ApplicationContext
{
    private readonly ArtemisWelcomeForm welcomeForm;
    private MainForm? mainForm;
    private bool startingMainForm;

    public StartupApplicationContext()
    {
        welcomeForm = new ArtemisWelcomeForm();
        welcomeForm.FormClosed += (_, _) =>
        {
            if (mainForm is null && !startingMainForm) ExitThread();
        };
        welcomeForm.Shown += (_, _) => welcomeForm.BeginInvoke(new Action(async () => await StartMainFormAsync()));
        welcomeForm.Show();
    }

    private async Task StartMainFormAsync()
    {
        if (startingMainForm) return;
        startingMainForm = true;
        try
        {
            await Task.Yield();
            mainForm = new MainForm { Opacity = 0 };
            MainForm = mainForm;
            mainForm.FormClosed += (_, _) => ExitThread();
            await welcomeForm.WaitForIntroAsync();
            if (welcomeForm.IsDisposed)
            {
                mainForm.Close();
                ExitThread();
                return;
            }
            mainForm.Show();
            await welcomeForm.CrossFadeToAsync(mainForm);
        }
        catch (Exception ex)
        {
            if (!welcomeForm.IsDisposed) welcomeForm.Close();
            MessageBox.Show($"Potato Launcher could not start.\n\n{ex.Message}", "Potato Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitThread();
        }
    }
}

internal sealed class ArtemisWelcomeForm : Form
{
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 16 };
    private readonly TaskCompletionSource introCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch animationClock = new();
    private readonly string quote = ArtemisWelcomeContent.PickQuote();
    private readonly bool reduceMotion = !ClientAreaAnimationsEnabled();
    private Bitmap? idleSpriteSheet;
    private Bitmap? waveSpriteSheet;
    private int animationTick;

    public ArtemisWelcomeForm()
    {
        Text = "Potato Launcher";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 420);
        ShowInTaskbar = true;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.FromArgb(25, 20, 46);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        LoadSpriteSheets();

        animationTimer.Tick += (_, _) => AdvanceAnimation();
        Shown += (_, _) =>
        {
            animationClock.Start();
            animationTimer.Start();
            Invalidate();
        };
        MouseClick += (_, _) => SkipIntro();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Escape or Keys.Enter or Keys.Space) SkipIntro();
        };
    }

    public Task WaitForIntroAsync() => introCompleted.Task;

    public async Task CrossFadeToAsync(Form mainForm)
    {
        animationTimer.Stop();
        TopMost = false;
        const int durationMilliseconds = 620;
        const int frameMilliseconds = 16;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < durationMilliseconds && !IsDisposed && !mainForm.IsDisposed)
        {
            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / durationMilliseconds, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            Opacity = Math.Clamp(1 - eased, 0, 1);
            mainForm.Opacity = Math.Clamp(eased, 0, 1);
            await Task.Delay(frameMilliseconds);
        }
        if (!mainForm.IsDisposed) mainForm.Opacity = 1;
        if (!IsDisposed) Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var background = new LinearGradientBrush(ClientRectangle, Color.FromArgb(34, 27, 61), Color.FromArgb(83, 39, 91), 18F);
        graphics.FillRectangle(background, ClientRectangle);
        DrawGlow(graphics);
        DrawStars(graphics);
        DrawPet(graphics);
        DrawSpeechBubble(graphics);
        DrawLoadingCaption(graphics);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        animationTimer.Stop();
        animationTimer.Dispose();
        idleSpriteSheet?.Dispose();
        waveSpriteSheet?.Dispose();
        introCompleted.TrySetResult();
        base.OnFormClosed(e);
    }

    private void AdvanceAnimation()
    {
        animationTick++;
        Invalidate();
        if (animationClock.Elapsed >= ArtemisWelcomeTimeline.Duration)
        {
            animationTimer.Stop();
            introCompleted.TrySetResult();
        }
    }

    private void SkipIntro()
    {
        animationTimer.Stop();
        introCompleted.TrySetResult();
    }

    private void DrawGlow(Graphics graphics)
    {
        var glowBounds = new Rectangle(-80, 178, 430, 330);
        using var path = new GraphicsPath();
        path.AddEllipse(glowBounds);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(94, 234, 96, 154),
            SurroundColors = [Color.FromArgb(0, 234, 96, 154)]
        };
        graphics.FillEllipse(brush, glowBounds);
    }

    private void DrawStars(Graphics graphics)
    {
        var stars = new[]
        {
            new Point(42, 58), new Point(92, 28), new Point(304, 48), new Point(670, 58),
            new Point(642, 342), new Point(368, 372), new Point(688, 222), new Point(24, 320)
        };
        for (var index = 0; index < stars.Length; index++)
        {
            var pulse = 0.55 + 0.45 * Math.Sin(animationClock.Elapsed.TotalSeconds * 2.4 + index);
            var radius = 2 + (int)Math.Round(pulse * 2);
            using var starBrush = new SolidBrush(Color.FromArgb((int)(90 + pulse * 130), 255, 226, 244));
            graphics.FillEllipse(starBrush, stars[index].X - radius, stars[index].Y - radius, radius * 2, radius * 2);
        }
    }

    private void DrawPet(Graphics graphics)
    {
        var welcomeFrame = ArtemisWelcomeTimeline.FrameAt(animationClock.Elapsed, reduceMotion);
        var frame = welcomeFrame.FrameIndex;
        var destination = new Rectangle(44, 72, 286, 286);
        var spriteSheet = welcomeFrame.State == ArtemisAnimationState.Wave ? waveSpriteSheet : idleSpriteSheet;
        if (spriteSheet is not null)
        {
            var source = ArtemisSpriteSheetLayout.SourceFrameBounds(spriteSheet.Size, frame);
            graphics.DrawImage(spriteSheet, destination, source, GraphicsUnit.Pixel);
            return;
        }

        using var fallbackFont = new Font("Segoe UI Emoji", 86F, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(graphics, "🌙", fallbackFont, destination, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawSpeechBubble(Graphics graphics)
    {
        var bubble = new Rectangle(318, 78, 350, 205);
        using var bubblePath = RoundedRectangle(bubble, 26);
        using var shadowPath = RoundedRectangle(new Rectangle(bubble.X + 5, bubble.Y + 8, bubble.Width, bubble.Height), 26);
        using var shadowBrush = new SolidBrush(Color.FromArgb(45, Color.Black));
        graphics.FillPath(shadowBrush, shadowPath);
        using var bubbleBrush = new SolidBrush(Color.FromArgb(248, 255, 250, 254));
        graphics.FillPath(bubbleBrush, bubblePath);
        using var borderPen = new Pen(Color.FromArgb(225, 239, 123, 180), 2F);
        graphics.DrawPath(borderPen, bubblePath);

        var tail = new[] { new Point(321, 218), new Point(286, 244), new Point(329, 247) };
        graphics.FillPolygon(bubbleBrush, tail);
        graphics.DrawLines(borderPen, new Point[] { tail[0], tail[1], tail[2] });

        using var greetingFont = new Font("Segoe UI", 15F, FontStyle.Bold);
        using var quoteFont = new Font("Segoe UI", 12F, FontStyle.Regular);
        using var signatureFont = new Font("Segoe UI", 10F, FontStyle.Italic);
        using var titleBrush = new SolidBrush(Color.FromArgb(177, 42, 105));
        using var textBrush = new SolidBrush(Color.FromArgb(54, 41, 67));
        using var signatureFormat = new StringFormat { Alignment = StringAlignment.Far };
        graphics.DrawString("Welcome back, adventurer!", greetingFont, titleBrush, new RectangleF(342, 102, 302, 34));
        graphics.DrawString(quote, quoteFont, textBrush, new RectangleF(342, 145, 300, 86));
        graphics.DrawString("— Artemis", signatureFont, titleBrush, new RectangleF(342, 238, 294, 24), signatureFormat);
    }

    private void DrawLoadingCaption(Graphics graphics)
    {
        var dots = new string('.', animationTick / 4 % 4);
        using var captionFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        using var hintFont = new Font("Segoe UI", 8.5F, FontStyle.Regular);
        using var captionBrush = new SolidBrush(Color.FromArgb(235, 255, 235, 248));
        using var hintBrush = new SolidBrush(Color.FromArgb(175, 239, 211, 232));
        graphics.DrawString($"Preparing Potato Launcher{dots}", captionFont, captionBrush, new RectangleF(340, 316, 300, 26));
        graphics.DrawString("Click, Enter, or Space to continue", hintFont, hintBrush, new RectangleF(340, 348, 300, 22));
    }

    private void LoadSpriteSheets()
    {
        idleSpriteSheet = TryLoadSpriteSheet(ArtemisAnimationAssets.Idle);
        waveSpriteSheet = TryLoadSpriteSheet(ArtemisAnimationAssets.Wave);
    }

    private static Bitmap? TryLoadSpriteSheet(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var decoded = Image.FromStream(stream);
            return new Bitmap(decoded);
        }
        catch
        {
            return null;
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static bool ClientAreaAnimationsEnabled()
    {
        try
        {
            return SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled;
        }
        catch
        {
            return true;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
