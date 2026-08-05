using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

internal enum ArtemisAnimationState
{
    Idle,
    Run,
    Release,
    Wave
}

internal static class ArtemisAnimationAssets
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Potato Launcher Assets", "Assets", "Artemis");
    public static string Idle => Path.Combine(Folder, "artemis-idle-atlas.png");
    public static string Run => Path.Combine(Folder, "artemis-run-atlas.png");
    public static string Release => Path.Combine(Folder, "artemis-release-atlas.png");
    public static string Wave => Path.Combine(Folder, "artemis-wave-atlas.png");

    public static bool AllAnimationSheetsExist() =>
        File.Exists(Idle) && File.Exists(Run) && File.Exists(Release) && File.Exists(Wave);
}

internal static class ArtemisAnimationTiming
{
    public const int FrameCount = 12;

    public static int FrameDurationMilliseconds(ArtemisAnimationState state) => state switch
    {
        ArtemisAnimationState.Run => 48,
        ArtemisAnimationState.Release => 65,
        ArtemisAnimationState.Wave => 82,
        _ => 95
    };

    public static bool Loops(ArtemisAnimationState state) =>
        state is ArtemisAnimationState.Idle or ArtemisAnimationState.Run;

    public static int FrameAt(ArtemisAnimationState state, TimeSpan elapsed)
    {
        var frame = Math.Max(0, (int)(elapsed.TotalMilliseconds / FrameDurationMilliseconds(state)));
        return Loops(state) ? frame % FrameCount : Math.Min(FrameCount - 1, frame);
    }

    public static bool IsComplete(ArtemisAnimationState state, TimeSpan elapsed) =>
        !Loops(state) && elapsed.TotalMilliseconds >= FrameDurationMilliseconds(state) * FrameCount;
}

internal sealed class ArtemisDesktopPetForm : Form
{
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private readonly Dictionary<ArtemisAnimationState, Bitmap> sheets;
    private readonly System.Windows.Forms.Timer frameTimer = new() { Interval = 16 };
    private readonly Stopwatch stateClock = new();
    private readonly ContextMenuStrip petMenu = new();
    private ArtemisAnimationState state = ArtemisAnimationState.Idle;
    private DateTime nextAmbientWaveUtc;
    private bool dragging;
    private bool facingLeft;
    private bool releaseStartedFacingLeft;
    private Point dragOffset;
    private Point lastCursorPosition;
    private int lastFrame = -1;

    public event EventHandler? RestoreRequested;

    private ArtemisDesktopPetForm(Dictionary<ArtemisAnimationState, Bitmap> sheets)
    {
        this.sheets = sheets;
        Text = "Artemis";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ClientSize = new Size(270, 300);
        MinimumSize = ClientSize;
        MaximumSize = ClientSize;
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var restoreItem = new ToolStripMenuItem("Restore Potato Launcher");
        restoreItem.Click += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
        var hideItem = new ToolStripMenuItem("Hide Artemis");
        hideItem.Click += (_, _) => Hide();
        petMenu.Items.Add(restoreItem);
        petMenu.Items.Add(new ToolStripSeparator());
        petMenu.Items.Add(hideItem);
        ContextMenuStrip = petMenu;

        frameTimer.Tick += (_, _) => AdvanceAnimation();
        Shown += (_, _) =>
        {
            PlaceNearBottomRight();
            BeginState(ArtemisAnimationState.Idle);
            frameTimer.Start();
            RenderCurrentFrame(force: true);
        };
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                BeginState(ArtemisAnimationState.Idle);
                frameTimer.Start();
                BeginInvoke(new Action(() => RenderCurrentFrame(force: true)));
            }
            else
            {
                frameTimer.Stop();
            }
        };
        DoubleClick += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    public static ArtemisDesktopPetForm? TryCreate()
    {
        if (!ArtemisAnimationAssets.AllAnimationSheetsExist()) return null;
        var loaded = new Dictionary<ArtemisAnimationState, Bitmap>();
        try
        {
            loaded[ArtemisAnimationState.Idle] = LoadBitmap(ArtemisAnimationAssets.Idle);
            loaded[ArtemisAnimationState.Run] = LoadBitmap(ArtemisAnimationAssets.Run);
            loaded[ArtemisAnimationState.Release] = LoadBitmap(ArtemisAnimationAssets.Release);
            loaded[ArtemisAnimationState.Wave] = LoadBitmap(ArtemisAnimationAssets.Wave);
            return new ArtemisDesktopPetForm(loaded);
        }
        catch
        {
            foreach (var bitmap in loaded.Values) bitmap.Dispose();
            return null;
        }
    }

    public void ShowNear(Rectangle workingArea)
    {
        if (Visible) return;
        Location = new Point(workingArea.Right - Width - 28, workingArea.Bottom - Height - 18);
        Show();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= WsExLayered | WsExToolWindow;
            return createParams;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        dragging = true;
        Capture = true;
        lastCursorPosition = Cursor.Position;
        dragOffset = new Point(lastCursorPosition.X - Left, lastCursorPosition.Y - Top);
        BeginState(ArtemisAnimationState.Run);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        var cursor = Cursor.Position;
        var deltaX = cursor.X - lastCursorPosition.X;
        if (Math.Abs(deltaX) >= 2) facingLeft = deltaX < 0;
        lastCursorPosition = cursor;
        Location = ClampToVirtualScreen(new Point(cursor.X - dragOffset.X, cursor.Y - dragOffset.Y));
        RenderCurrentFrame(force: true);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !dragging) return;
        dragging = false;
        Capture = false;
        releaseStartedFacingLeft = facingLeft;
        BeginState(ArtemisAnimationState.Release);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        frameTimer.Stop();
        frameTimer.Dispose();
        stateClock.Stop();
        petMenu.Dispose();
        foreach (var sheet in sheets.Values) sheet.Dispose();
        base.OnFormClosed(e);
    }

    private void AdvanceAnimation()
    {
        if (!Visible || IsDisposed) return;
        if (ArtemisAnimationTiming.IsComplete(state, stateClock.Elapsed))
        {
            BeginState(ArtemisAnimationState.Idle);
        }
        else if (!dragging && state == ArtemisAnimationState.Idle && DateTime.UtcNow >= nextAmbientWaveUtc)
        {
            BeginState(ArtemisAnimationState.Wave);
        }
        RenderCurrentFrame();
    }

    private void BeginState(ArtemisAnimationState nextState)
    {
        state = nextState;
        lastFrame = -1;
        stateClock.Restart();
        if (nextState == ArtemisAnimationState.Idle)
        {
            nextAmbientWaveUtc = DateTime.UtcNow.AddSeconds(Random.Shared.Next(8, 15));
        }
    }

    private void RenderCurrentFrame(bool force = false)
    {
        if (!IsHandleCreated || IsDisposed || !Visible) return;
        var frame = ArtemisAnimationTiming.FrameAt(state, stateClock.Elapsed);
        if (!force && frame == lastFrame) return;
        lastFrame = frame;

        using var rendered = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(rendered))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;

            var source = ArtemisSpriteSheetLayout.SourceFrameBounds(sheets[state].Size, frame);
            var destination = ArtemisSpriteSheetLayout.FitFrameBounds(source.Size, ClientRectangle);
            var flip = state == ArtemisAnimationState.Run
                ? facingLeft
                : state == ArtemisAnimationState.Release && releaseStartedFacingLeft && frame < 8;
            if (flip)
            {
                graphics.TranslateTransform(ClientSize.Width, 0);
                graphics.ScaleTransform(-1, 1);
            }
            graphics.DrawImage(sheets[state], destination, source, GraphicsUnit.Pixel);
        }
        UpdateLayeredBitmap(rendered);
    }

    private void PlaceNearBottomRight()
    {
        if (Location != Point.Empty) return;
        var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.VirtualScreen;
        Location = new Point(area.Right - Width - 28, area.Bottom - Height - 18);
    }

    private Point ClampToVirtualScreen(Point location)
    {
        var area = SystemInformation.VirtualScreen;
        return new Point(
            Math.Clamp(location.X, area.Left - Width / 2, area.Right - Width / 2),
            Math.Clamp(location.Y, area.Top, area.Bottom - Height / 3));
    }

    private void UpdateLayeredBitmap(Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = IntPtr.Zero;
        var previousObject = IntPtr.Zero;
        try
        {
            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = bitmap.Width,
                    Height = -bitmap.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };
            bitmapHandle = CreateDIBSection(screenDc, ref bitmapInfo, 0, out var bitmapBits, IntPtr.Zero, 0);
            if (bitmapHandle == IntPtr.Zero || bitmapBits == IntPtr.Zero) return;
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                var byteCount = Math.Abs(bitmapData.Stride) * bitmap.Height;
                var pixels = new byte[byteCount];
                Marshal.Copy(bitmapData.Scan0, pixels, 0, byteCount);
                Marshal.Copy(pixels, 0, bitmapBits, byteCount);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
            previousObject = SelectObject(memoryDc, bitmapHandle);
            var destination = new NativePoint(Left, Top);
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (previousObject != IntPtr.Zero) SelectObject(memoryDc, previousObject);
            if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static Bitmap LoadBitmap(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var decoded = Image.FromStream(stream);
        return new Bitmap(decoded);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int cx, int cy)
    {
        public int Cx = cx;
        public int Cy = cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc, ref NativePoint destination, ref NativeSize size, IntPtr sourceDc, ref NativePoint source, int colorKey, ref BlendFunction blend, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo bitmapInfo, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
