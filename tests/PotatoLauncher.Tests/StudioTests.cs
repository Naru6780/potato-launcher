using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using PotatoLauncher.Studio;

namespace PotatoLauncher.Tests;

public sealed class StudioTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PotatoStudioTests-" + Guid.NewGuid().ToString("N"));
    public StudioTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void ImportCopiesExactActionsHierarchyAndCustomIconsWithoutChangingSource()
    {
        var path = Path.Combine(directory, "QoLBar.json");
        const string source = """
        {"BarCfgs":[{"n":"Sample (Multi)","sL":[
          {"n":"::-944##Bard","t":1,"m":0,"c":"/cwl2 mopbr /gs change 7\n/cwl2 moprun \"Sample\" ","sL":[
            {"n":"::fn-945##Costumes","t":1,"sL":[{"n":"::-946##Outfit","c":"/gs change 2"}]}]},
          {"n":"::56##Local","c":"/wave"},
          {"n":"Rotate","m":1,"c":"/bow\n/wave"}]}]}
        """;
        File.WriteAllText(path, source);
        var profile = StudioProfiles.Import(path, "Sample (Multi)");
        Assert.Equal(source, File.ReadAllText(path));
        Assert.Equal(new[] { "Bard", "Local", "Rotate" }, profile.Buttons.Select(button => button.Label));
        var bard = profile.Buttons[0];
        Assert.True(bard.IsGroup);
        Assert.Equal("", bard.Command); // Opening the group never sends its old category action.
        Assert.Equal("/cwl2 mopbr /gs change 7\n/cwl2 moprun \"Sample\" ", bard.Children[0].Command);
        Assert.Equal(-944, bard.IconId);
        Assert.Equal(Path.Combine(directory, "QoLBar", "icons", "944.png"), bard.Artwork);
        Assert.Equal("/gs change 2", bard.Children[1].Children[0].Command);
        Assert.Equal(56, profile.Buttons[1].IconId);
        Assert.NotEmpty(profile.Buttons[2].ImportWarning);
        Assert.Equal(new[] { "/cwl2 mopbr /gs change 7", "/cwl2 moprun \"Sample\" " }, StudioProfiles.StepsFor(bard.Children[0]).Select(step => step.Command));
    }

    [Fact]
    public void CustomizationRoundTripsAndSavesRecoverablePreviousProfile()
    {
        var path = Path.Combine(directory, "studio.json");
        var profile = new StudioProfile { Name = "Control the band", TileSize = 144, Animate = false, Buttons = [new() { Label = "Group", IsGroup = true, Children = [new() { Label = "Wave", IconId = 56, Artwork = "C:\\Artwork\\wave.png", Background = "#102030", Foreground = "#EEDDCC", Steps = [new("/wave", 1234), new("/bow", 0)] }] }] };
        StudioProfiles.Save(path, profile);
        var loaded = StudioProfiles.Load(path);
        Assert.Equal(144, loaded.TileSize); Assert.False(loaded.Animate);
        Assert.Equal(profile.Buttons[0].Children[0].Steps, loaded.Buttons[0].Children[0].Steps);
        Assert.Equal("#102030", loaded.Buttons[0].Children[0].Background);
        Assert.Equal("C:\\Artwork\\wave.png", loaded.Buttons[0].Children[0].Artwork);
        loaded.Name = "Renamed";
        StudioProfiles.Save(path, loaded);
        Assert.Equal("Control the band", StudioProfiles.Load(path + ".bak").Name);
        Assert.Equal("Renamed", StudioProfiles.Load(path).Name);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void DuplicateRenewsEveryIdentityAndDoesNotShareChildren()
    {
        var original = new StudioButton { IsGroup = true, Children = [new() { Steps = [new("/wave", 200)] }] };
        var copy = StudioProfiles.Duplicate(original);
        Assert.NotEqual(original.Id, copy.Id); Assert.NotEqual(original.Children[0].Id, copy.Children[0].Id);
        copy.Children[0].Label = "Changed"; Assert.NotEqual(copy.Children[0].Label, original.Children[0].Label);
    }

    [Fact]
    public void BadProfileDoesNotOverwriteExistingProfile()
    {
        var path = Path.Combine(directory, "profile.json");
        var profile = new StudioProfile(); StudioProfiles.Save(path, profile);
        var before = File.ReadAllBytes(path);
        profile.Version = 99;
        Assert.Throws<InvalidDataException>(() => StudioProfiles.Save(path, profile));
        Assert.Equal(before, File.ReadAllBytes(path));
        profile.Version = 1;
        var shared = new StudioButton(); profile.Buttons = [shared, shared];
        Assert.Throws<InvalidDataException>(() => StudioProfiles.Validate(profile));
    }

    [Fact]
    public void ImportRejectsAmbiguousNamesAndOversizedFiles()
    {
        var path = Path.Combine(directory, "bar.json");
        File.WriteAllText(path, "{\"BarCfgs\":[{\"n\":\"x\"},{\"n\":\"x\"}]}");
        Assert.Throws<InvalidDataException>(() => StudioProfiles.Import(path, "x"));
        using (var file = File.Create(path)) file.SetLength(StudioProfiles.MaxFileBytes + 1);
        Assert.Throws<InvalidDataException>(() => StudioProfiles.ReadBounded(path));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("//m\n/wave")]
    [InlineData("/wave\0")]
    [InlineData("/wave\r/bow")]
    [InlineData("/wave\t")]
    [InlineData("/wait 2")]
    [InlineData("/wave <wait.2>")]
    public void CommandValidationRejectsUnsupportedOrUnsafePayloads(string command) => Assert.Throws<InvalidDataException>(() => BridgeProtocol.Commands(command));

    [Fact]
    public void CommandValidationPreservesExactTextAndChecksUtf8Size()
    {
        const string text = "/cwl2 moprun \"Example Macro\" ";
        Assert.Equal(text, Assert.Single(BridgeProtocol.Commands(text)));
        Assert.Throws<InvalidDataException>(() => BridgeProtocol.Commands("/" + new string('é', 250)));
        Assert.Equal("/" + new string('a', 499), Assert.Single(BridgeProtocol.Commands("/" + new string('a', 499))));
    }

    [Fact]
    public async Task SequencePreservesOrderAndPerStepWaitsWithoutFinalWait()
    {
        var events = new List<string>();
        await StudioSequence.RunAsync([new("/wave", 250), new("/bow", 1800), new("/cheer", 900)],
            (command, _) => { events.Add(command); return Task.CompletedTask; },
            (milliseconds, _) => { events.Add("wait:" + milliseconds); return Task.CompletedTask; }, (_, _) => { }, CancellationToken.None);
        Assert.Equal(new[] { "/wave", "wait:250", "/bow", "wait:1800", "/cheer" }, events);
    }

    [Fact]
    public async Task StopDuringWaitNeverSendsRemainingSteps()
    {
        using var stop = new CancellationTokenSource();
        var sent = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StudioSequence.RunAsync([new("/wave", 1000), new("/bow", 0)],
            (command, _) => { sent.Add(command); return Task.CompletedTask; },
            (_, token) => { stop.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, (_, _) => { }, stop.Token));
        Assert.Equal(new[] { "/wave" }, sent);
    }

    [Fact]
    public async Task RejectionOrDisconnectStopsWithoutRetry()
    {
        var calls = 0;
        await Assert.ThrowsAsync<IOException>(() => StudioSequence.RunAsync([new("/wave", 0), new("/bow", 0)],
            (_, _) => { calls++; throw new IOException("Disconnected after possible execution"); }, (_, _) => Task.CompletedTask, (_, _) => { }, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WholeSequenceIsValidatedBeforeFirstStep()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => StudioSequence.RunAsync([new("/wave", 0), new("//m", 0)],
            (_, _) => { calls++; return Task.CompletedTask; }, (_, _) => Task.CompletedTask, (_, _) => { }, CancellationToken.None));
        Assert.Equal(0, calls);
        Assert.Throws<InvalidDataException>(() => StudioSequence.Validate([new("/wave", -1)]));
        Assert.Throws<InvalidDataException>(() => StudioSequence.Validate([new("/wave", 600001)]));
    }

    [Fact]
    public void ReceiverRequiresExplicitArmingAndRejectsReplayOrChangedIdentity()
    {
        var gate = new BridgeGate(123);
        var request = new BridgeRequest(1, Guid.NewGuid(), "execute", 123, gate.Session, "/wave");
        Assert.NotNull(gate.Accept(request));
        gate.Arm(true, 42);
        Assert.NotNull(gate.Accept(request)); // Old unarmed session cannot be reused.
        request = request with { Session = gate.Session };
        Assert.Null(gate.Accept(request)); Assert.NotNull(gate.Accept(request));
        Assert.NotNull(gate.Accept(request with { RequestId = Guid.NewGuid(), StartTicks = 124 }));
        Assert.NotNull(gate.Accept(request with { RequestId = Guid.NewGuid(), Version = 2 }));
        Assert.NotNull(gate.Accept(request with { RequestId = Guid.NewGuid(), Command = "//m" }));
        gate.Update(true, 43);
        Assert.False(gate.Armed); Assert.NotNull(gate.Accept(request with { RequestId = Guid.NewGuid() }));
        gate.Arm(true, 43); gate.Update(false, 0); Assert.False(gate.Armed);
        gate.Arm(true, 43); gate.Reset(); Assert.False(gate.Armed);
    }

    [Fact]
    public async Task ActualNamedPipeTargetsTheExpectedProcessAndTransmitsOnce()
    {
        using var process = Process.GetCurrentProcess();
        var ticks = process.StartTime.ToUniversalTime().Ticks;
        var requests = new List<BridgeRequest>();
        var errors = new List<Exception>();
        using var server = new BridgeServer(process.Id, (request, _) =>
        {
            lock (requests) requests.Add(request);
            return Task.FromResult(new BridgeReply(1, request.RequestId, true, "Submitted", ticks, "test-session", "Fixture"));
        }, ex => { lock (errors) errors.Add(ex); });
        var payload = new BridgeRequest(1, Guid.NewGuid(), "execute", ticks, "test-session", "/wave");
        var reply = await BridgeClient.SendAsync(process.Id, payload, CancellationToken.None);
        Assert.True(reply.Ok); Assert.Equal(payload, Assert.Single(requests));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BridgeClient.SendAsync(process.Id, payload with { StartTicks = ticks - 1 }, CancellationToken.None));
        Assert.Single(requests); Assert.Empty(errors);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1048577)]
    public async Task InvalidFrameSizesAreRejectedBeforeAllocation(int length)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => BridgeProtocol.ReadAsync<BridgeRequest>(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData(1260, 800)]
    [InlineData(1080, 700)]
    public void EditorRendersAndGroupNavigationNeverExecutesCommands(int width, int height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var fixture = new StudioProfile { Name = "Fixture", Buttons = [new() { Label = "Control the band", IsGroup = true, Children = [new() { Label = "Wave", Steps = [new("/wave", 300), new("/bow", 0)] }] }, new() { Label = "Local action", Command = "/wave" }] };
                StudioProfiles.Save(Path.Combine(directory, "Command Studio", "profile.json"), fixture);
                var palette = new ThemePalette(Color.FromArgb(25, 21, 40), Color.Purple, Color.FromArgb(35, 31, 50), Color.MediumPurple, Color.White, Color.Gray, Color.MediumPurple, Color.Pink, Color.Red, Color.FromArgb(35, 31, 50));
                using var form = new CommandStudioForm(directory, palette, connectOnShow: false) { Size = new Size(width, height), ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(-20000, -20000) };
                form.Show(); // Offscreen, fixture-only; Shown never connects to real clients.
                form.PerformLayout();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var field = typeof(CommandStudioForm).GetField("tiles", flags)!;
                var tiles = (System.Windows.Forms.FlowLayoutPanel)field.GetValue(form)!;
                var editorPanel = (System.Windows.Forms.Panel)typeof(CommandStudioForm).GetField("editor", flags)!.GetValue(form)!;
                var treePanel = (System.Windows.Forms.Panel)typeof(CommandStudioForm).GetField("treePanel", flags)!.GetValue(form)!;
                var editTools = (System.Windows.Forms.FlowLayoutPanel)typeof(CommandStudioForm).GetField("editTools", flags)!.GetValue(form)!;
                Assert.False(editorPanel.Visible); Assert.False(treePanel.Visible); Assert.False(editTools.Visible);
                var cleanWidth = tiles.Width;
                Assert.Equal(2, tiles.Controls.Count);
                typeof(CommandStudioForm).GetMethod("Navigate", flags)!.Invoke(form, [fixture.Buttons[0].Id]);
                Assert.Single(tiles.Controls.Cast<System.Windows.Forms.Control>());
                Assert.Equal("Wave", tiles.Controls[0].Text);
                Assert.Null(typeof(CommandStudioForm).GetField("sequence", flags)!.GetValue(form));
                typeof(CommandStudioForm).GetMethod("Back", flags)!.Invoke(form, null);
                Assert.Equal(2, tiles.Controls.Count);
                Assert.True(form.SetEditorMode(true));
                Assert.True(editorPanel.Visible); Assert.True(treePanel.Visible); Assert.True(editTools.Visible);
                Assert.True(tiles.Width < cleanWidth - 500);
                var tree = (System.Windows.Forms.TreeView)typeof(CommandStudioForm).GetField("tree", flags)!.GetValue(form)!;
                tree.SelectedNode = tree.Nodes[1];
                var label = (System.Windows.Forms.TextBox)typeof(CommandStudioForm).GetField("label", flags)!.GetValue(form)!;
                label.Text = "Edited local action";
                var stepGrid = (System.Windows.Forms.DataGridView)typeof(CommandStudioForm).GetField("steps", flags)!.GetValue(form)!;
                stepGrid.Rows[0].Cells[0].Value = "/bow";
                stepGrid.Rows[0].Cells[1].Value = 1234;
                typeof(CommandStudioForm).GetMethod("ApplyEditor", flags)!.Invoke(form, null);
                var saved = StudioProfiles.Load(Path.Combine(directory, "Command Studio", "profile.json"));
                Assert.Equal(fixture.Buttons[0].Id, saved.Buttons[0].Id);
                Assert.Equal("Edited local action", saved.Buttons[1].Label);
                Assert.Equal(new StudioStep("/bow", 1234), Assert.Single(saved.Buttons[1].Steps));
                tree.SelectedNode = tree.Nodes[0]; label.Text = "Band controls";
                typeof(CommandStudioForm).GetMethod("ApplyEditor", flags)!.Invoke(form, null);
                Assert.Equal(fixture.Buttons[0].Id, StudioProfiles.Load(Path.Combine(directory, "Command Studio", "profile.json")).Buttons[0].Id);
                tree.SelectedNode = tree.Nodes[1];
                using var bitmap = new Bitmap(width, height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
                Assert.NotEqual(bitmap.GetPixel(20, 150), bitmap.GetPixel(250, 150));
                var preview = Environment.GetEnvironmentVariable("POTATO_STUDIO_TEST_PREVIEWS");
                if (!string.IsNullOrWhiteSpace(preview)) { Directory.CreateDirectory(preview); bitmap.Save(Path.Combine(preview, $"studio-{width}x{height}.png"), ImageFormat.Png); }
                var targets = (System.Windows.Forms.ComboBox)typeof(CommandStudioForm).GetField("targets", flags)!.GetValue(form)!;
                targets.Items.Add("Fixture origin"); targets.SelectedIndex = 0;
                typeof(CommandStudioForm).GetMethod("Navigate", flags)!.Invoke(form, [fixture.Buttons[0].Id]);
                Assert.True(form.SetEditorMode(false));
                Assert.False(editorPanel.Visible); Assert.False(treePanel.Visible); Assert.False(editTools.Visible);
                Assert.Equal(cleanWidth, tiles.Width);
                Assert.Equal("Fixture origin", targets.SelectedItem);
                Assert.Equal(fixture.Buttons[0].Id, typeof(CommandStudioForm).GetField("groupId", flags)!.GetValue(form));
                using var cleanBitmap = new Bitmap(width, height);
                form.DrawToBitmap(cleanBitmap, new Rectangle(0, 0, width, height));
                if (!string.IsNullOrWhiteSpace(preview)) cleanBitmap.Save(Path.Combine(preview, $"studio-clean-{width}x{height}.png"), ImageFormat.Png);
                // Explicit local-only artwork/import inspection. Never used by CI, never contacts a game,
                // never changes the input config, and no user config/artwork is committed or packaged.
                var localSource = Environment.GetEnvironmentVariable("POTATO_STUDIO_TEST_QOLBAR");
                if (!string.IsNullOrWhiteSpace(preview) && !string.IsNullOrWhiteSpace(localSource))
                {
                    var before = File.ReadAllBytes(localSource);
                    var imported = StudioProfiles.Import(localSource, "Jobs (Multi)");
                    StudioProfiles.Save(Path.Combine(directory, "Command Studio", "profile.json"), imported);
                    using var localForm = new CommandStudioForm(directory, palette, connectOnShow: false) { Size = new Size(width, height), ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(-20000, -20000) };
                    localForm.Show(); localForm.PerformLayout();
                    using var localBitmap = new Bitmap(width, height);
                    localForm.DrawToBitmap(localBitmap, new Rectangle(0, 0, width, height));
                    localBitmap.Save(Path.Combine(preview, $"local-studio-{width}x{height}.png"), ImageFormat.Png);
                    Assert.Equal(before, File.ReadAllBytes(localSource));
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Studio UI rendering timed out."); Assert.Null(failure);
    }

    [Theory]
    [InlineData(System.Windows.Forms.DialogResult.Yes, "Draft label", false)]
    [InlineData(System.Windows.Forms.DialogResult.No, "Saved label", false)]
    [InlineData(System.Windows.Forms.DialogResult.Cancel, "Saved label", true)]
    public void LeavingEditorHandlesSaveDiscardAndCancel(System.Windows.Forms.DialogResult choice, string expectedSaved, bool remainsEditing)
    {
        RunStudioThread(form =>
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.True(form.SetEditorMode(true));
            var label = (System.Windows.Forms.TextBox)typeof(CommandStudioForm).GetField("label", flags)!.GetValue(form)!;
            label.Text = "Draft label";
            Assert.Equal(!remainsEditing, form.SetEditorMode(false, () => choice));
            Assert.Equal(remainsEditing, typeof(CommandStudioForm).GetField("editMode", flags)!.GetValue(form));
            Assert.Equal(expectedSaved, StudioProfiles.Load(Path.Combine(directory, "Command Studio", "profile.json")).Buttons[0].Label);
            if (remainsEditing) { Assert.Equal("Draft label", label.Text); form.SetEditorMode(false, () => System.Windows.Forms.DialogResult.No); }
        });
    }

    [Fact]
    public void EditorActionClicksOnlySelectAndSequencePreventsModeChange()
    {
        RunStudioThread(form =>
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var sequenceField = typeof(CommandStudioForm).GetField("sequence", flags)!;
            var status = (System.Windows.Forms.Label)typeof(CommandStudioForm).GetField("status", flags)!.GetValue(form)!;
            var tiles = (System.Windows.Forms.FlowLayoutPanel)typeof(CommandStudioForm).GetField("tiles", flags)!.GetValue(form)!;
            using (var sequence = new CancellationTokenSource())
            {
                sequenceField.SetValue(form, sequence);
                Assert.False(form.SetEditorMode(true));
                Assert.Contains("Stop the active sequence", status.Text);
                sequenceField.SetValue(form, null);
            }
            Assert.True(form.SetEditorMode(true));
            status.Text = "No command submitted";
            ((System.Windows.Forms.Button)tiles.Controls[0]).PerformClick();
            Assert.Equal("No command submitted", status.Text);
            Assert.Null(sequenceField.GetValue(form));
            Assert.NotNull(typeof(CommandStudioForm).GetField("selectedId", flags)!.GetValue(form));
            Assert.True(form.SetEditorMode(false));
        });
    }

    private void RunStudioThread(Action<CommandStudioForm> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                StudioProfiles.Save(Path.Combine(directory, "Command Studio", "profile.json"), new StudioProfile { Buttons = [new() { Label = "Saved label", Command = "/wave" }] });
                var palette = new ThemePalette(Color.Black, Color.Black, Color.DimGray, Color.Gray, Color.White, Color.Gray, Color.Purple, Color.Pink, Color.Red, Color.DimGray);
                using var form = new CommandStudioForm(directory, palette, connectOnShow: false) { ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(-20000, -20000) };
                form.Show(); check(form);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Studio mode test timed out."); Assert.Null(failure);
    }
}
