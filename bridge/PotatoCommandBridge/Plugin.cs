using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using Lumina.Data.Files;
using PotatoLauncher.Studio;

namespace PotatoCommandBridge;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IFramework framework;
    private readonly ICommandManager commands;
    private readonly IClientState client;
    private readonly IPlayerState player;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly IDataManager data;
    private readonly BridgeServer server;
    private readonly long startTicks;
    private readonly BridgeGate gate;
    private bool disposed;

    public Plugin(IFramework framework, ICommandManager commands, IClientState client, IPlayerState player, IChatGui chat, IPluginLog log, IDataManager data)
    {
        this.framework = framework; this.commands = commands; this.client = client;
        this.player = player; this.chat = chat; this.log = log; this.data = data;
        using var process = Process.GetCurrentProcess();
        startTicks = process.StartTime.ToUniversalTime().Ticks;
        gate = new BridgeGate(startTicks);
        commands.AddHandler("/potatobridge", new CommandInfo(OnCommand) { HelpMessage = "on | off | status — explicitly allow this client to receive Command Studio actions. Defaults OFF." });
        framework.Update += OnUpdate;
        server = new BridgeServer(Environment.ProcessId, HandleAsync, ex => log.Warning("Command bridge connection ended: {Type}", ex.GetType().Name));
    }

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) gate.Reset();
        else if (arguments.Trim().Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            gate.Arm(client.IsLoggedIn && player.IsLoaded, player.ContentId);
        }
        chat.Print($"[Potato Bridge] {(gate.Armed ? "ON — launcher commands can execute in this client" : "OFF")}. /potatobridge off stops delivery.");
    }

    private void OnUpdate(IFramework _) => gate.Update(client.IsLoggedIn && player.IsLoaded, player.ContentId);

    private BridgeReply Reply(BridgeRequest request, bool ok, string message) => new(BridgeProtocol.Version, request.RequestId, ok, message, startTicks, gate.Session,
        player.IsLoaded ? player.CharacterName + "@" + player.HomeWorld.Value.Name.ToString() : "Not logged in", gate.Armed);

    private async Task<BridgeReply> HandleAsync(BridgeRequest request, CancellationToken token)
    {
        var first = await framework.RunOnFrameworkThread(() =>
        {
            token.ThrowIfCancellationRequested();
            if (disposed) throw new OperationCanceledException();
            OnUpdate(framework);
            if (gate.HeaderError(request) is { } headerError) return Reply(request, false, headerError);
            if (request.Operation == "status") return Reply(request, true, gate.Armed ? "Ready" : "Run /potatobridge on in this client, then refresh.");
            if (request.Operation == "icon") return Icon(request);
            if (request.Operation != "execute") return Reply(request, false, "Unknown operation.");
            if (gate.Accept(request) is { } error) return Reply(request, false, error);
            return Reply(request, true, "Accepted");
        });
        if (!first.Ok || request.Operation != "execute") return first;
        var lines = BridgeProtocol.Commands(request.Command);
        var submitted = 0;
        foreach (var line in lines)
        {
            var continued = await framework.RunOnFrameworkThread(() =>
            {
                // Cancellation, logout, OFF and plugin unload also cancel queued lines.
                if (token.IsCancellationRequested || disposed) return false;
                OnUpdate(framework);
                if (!gate.Armed || request.Session != gate.Session || !client.IsLoggedIn || !player.IsLoaded) return false;
                Send(line);
                return true;
            });
            if (!continued) break;
            submitted++;
            if (submitted < lines.Length) await Task.Delay(180, token);
        }
        log.Information("Command Studio request {RequestId}: submitted {Count}/{Total} line(s)", request.RequestId, submitted, lines.Length);
        return await framework.RunOnFrameworkThread(() => Reply(request, submitted == lines.Length,
            $"Submitted {submitted}/{lines.Length} line(s) to the game. This does not confirm the action succeeded."));
    }

    private static unsafe void Send(string command)
    {
        var module = UIModule.Instance();
        if (module == null) throw new InvalidOperationException("Game UI is unavailable.");
        using var text = new Utf8String();
        text.SetString(command);
        module->ProcessChatBoxEntry(&text);
    }

    private BridgeReply Icon(BridgeRequest request)
    {
        if (request.IconId is < 1 or > 999999) return Reply(request, false, "Invalid game icon ID.");
        var id = request.IconId;
        var texture = data.GetFile<TexFile>($"ui/icon/{id / 1000 * 1000:D6}/{id:D6}.tex");
        if (texture is null || texture.Header.Width is < 1 or > 256 || texture.Header.Height is < 1 or > 256)
            return Reply(request, false, "Icon is unavailable.");
        var pixels = texture.ImageData;
        if (pixels.Length != texture.Header.Width * texture.Header.Height * 4) return Reply(request, false, "Unsupported icon texture.");
        return Reply(request, true, "Icon") with { Pixels = pixels, Width = texture.Header.Width, Height = texture.Header.Height };
    }

    public void Dispose()
    {
        disposed = true;
        gate.Reset();
        commands.RemoveHandler("/potatobridge");
        framework.Update -= OnUpdate;
        server.Dispose();
    }
}
