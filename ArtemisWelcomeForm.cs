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
        "The moon is watching - give it your best smile!",
        "A chocobo ride fixes almost everything.",
        "By the Twelve, you look ready for a little mischief.",
        "Warm cocoa and rested EXP - an adventurer's dream.",
        "Keep your courage close and your minions closer.",
        "Every grand journey begins with one tiny step.",
        "The Crystal has excellent taste - it chose you!",
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
    private static readonly Color TransparentBackground = Color.FromArgb(1, 2, 3);
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 16 };
    private readonly TaskCompletionSource introCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch animationClock = new();
    private readonly string quote = ArtemisWelcomeContent.PickQuote();
    private readonly bool reduceMotion = !ClientAreaAnimationsEnabled();
    private Bitmap? idleSpriteSheet;
    private Bitmap? waveSpriteSheet;

    public ArtemisWelcomeForm()
    {
        Text = "Potato Launcher";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 400);
        ShowInTaskbar = true;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        AllowTransparency = true;
        BackColor = TransparentBackground;
        TransparencyKey = TransparentBackground;
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
        DrawPet(graphics);
        DrawCogwheel(graphics);
        DrawQuote(graphics);
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

    private void DrawPet(Graphics graphics)
    {
        var welcomeFrame = ArtemisWelcomeTimeline.FrameAt(animationClock.Elapsed, reduceMotion);
        var destination = new Rectangle(52, 30, 300, 300);
        var spriteSheet = welcomeFrame.State == ArtemisAnimationState.Wave ? waveSpriteSheet : idleSpriteSheet;
        if (spriteSheet is not null)
        {
            var source = ArtemisSpriteSheetLayout.SourceFrameBounds(spriteSheet.Size, welcomeFrame.FrameIndex);
            graphics.DrawImage(spriteSheet, destination, source, GraphicsUnit.Pixel);
            return;
        }

        using var fallbackFont = new Font("Segoe UI", 42F, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(graphics, "Artemis", fallbackFont, destination, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawCogwheel(Graphics graphics)
    {
        const float centerX = 470F;
        const float centerY = 162F;
        const int toothCount = 12;
        var angle = reduceMotion ? 0F : (float)(animationClock.Elapsed.TotalSeconds * 55);
        var points = new PointF[toothCount * 4];
        for (var index = 0; index < points.Length; index++)
        {
            var radians = index * Math.PI * 2 / points.Length;
            var radius = index % 4 is 0 or 3 ? 66F : 55F;
            points[index] = new PointF((float)Math.Cos(radians) * radius, (float)Math.Sin(radians) * radius);
        }

        var saved = graphics.Save();
        graphics.TranslateTransform(centerX, centerY);
        graphics.RotateTransform(angle);
        using var gearPath = new GraphicsPath();
        gearPath.AddPolygon(points);
        using var glowPen = new Pen(Color.FromArgb(70, 255, 229, 168), 8F) { LineJoin = LineJoin.Round };
        using var gearPen = new Pen(Color.FromArgb(245, 235, 207, 139), 3F) { LineJoin = LineJoin.Round };
        using var detailPen = new Pen(Color.FromArgb(220, 255, 250, 224), 1.5F);
        graphics.DrawPath(glowPen, gearPath);
        graphics.DrawPath(gearPen, gearPath);
        graphics.DrawEllipse(detailPen, -45, -45, 90, 90);
        graphics.DrawEllipse(gearPen, -16, -16, 32, 32);
        for (var spoke = 0; spoke < 8; spoke++)
        {
            var spokeAngle = spoke * Math.PI / 4;
            graphics.DrawLine(
                detailPen,
                (float)Math.Cos(spokeAngle) * 18,
                (float)Math.Sin(spokeAngle) * 18,
                (float)Math.Cos(spokeAngle) * 43,
                (float)Math.Sin(spokeAngle) * 43);
        }
        graphics.Restore(saved);

        var crystal = new[]
        {
            new PointF(centerX, centerY - 12),
            new PointF(centerX + 8, centerY),
            new PointF(centerX, centerY + 12),
            new PointF(centerX - 8, centerY)
        };
        using var crystalBrush = new SolidBrush(Color.FromArgb(240, 255, 250, 226));
        graphics.FillPolygon(crystalBrush, crystal);
    }

    private void DrawQuote(Graphics graphics)
    {
        var text = $"\u201c{quote}\u201d  \u2014 Artemis";
        var bounds = new RectangleF(34, 330, 552, 54);
        using var font = new Font("Segoe UI", 12F, FontStyle.Italic);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var shadowBrush = new SolidBrush(Color.FromArgb(210, 12, 8, 22));
        using var textBrush = new SolidBrush(Color.FromArgb(250, 255, 250, 235));
        graphics.DrawString(text, font, shadowBrush, new RectangleF(bounds.X + 2, bounds.Y + 2, bounds.Width, bounds.Height), format);
        graphics.DrawString(text, font, textBrush, bounds, format);
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
