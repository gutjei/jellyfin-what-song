using System.Threading.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SongPause;

// Native clients never load client.js. Use the session protocol they already speak.
internal sealed class SessionSongNotifier(ISessionManager sessions, ILibraryManager library,
    IUserManager users, WebClientRegistry webClients, ILogger<SessionSongNotifier> log) : BackgroundService
{
    private sealed record Pause(SessionInfo Session, Guid ItemId, string? MediaSourceId, long PositionMs);
    private readonly object gate = new();
    private readonly Dictionary<string, Pause> current = new();
    private readonly Dictionary<string, Pause> pending = new();
    // Coalesce wakeups, not session updates: every session keeps its latest position.
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(1);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        sessions.PlaybackStart += OnProgress;
        sessions.PlaybackProgress += OnProgress;
        sessions.PlaybackStopped += OnStopped;
        sessions.SessionEnded += OnSessionEnded;
        return base.StartAsync(cancellationToken);
    }

    private void OnProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.IsAutomated || e.Session?.Id is not { Length: > 0 } id) return;
        lock (gate)
        {
            if (!e.IsPaused || e.Item is not (Movie or Episode) || e.PlaybackPositionTicks is null or < 0
                || webClients.IsActive(id))
            {
                current.Remove(id);
                pending.Remove(id);
                return;
            }
            if (!e.Session.SupportsRemoteControl
                || e.Session.Capabilities?.SupportedCommands?.Contains(GeneralCommandType.DisplayMessage) != true)
            {
                current.Remove(id);
                pending.Remove(id);
                log.LogDebug("Song Pause: session {SessionId} cannot receive DisplayMessage; check client capabilities and WebSocket", id);
                return;
            }
            var pause = new Pause(e.Session, e.Item.Id, e.MediaSourceId,
                e.PlaybackPositionTicks.Value / TimeSpan.TicksPerMillisecond);
            if (current.TryGetValue(id, out var previous) && previous == pause) return;
            current[id] = pending[id] = pause;
            wake.Writer.TryWrite(true);
        }
    }

    private void Forget(string? id)
    {
        if (id is null) return;
        lock (gate)
        {
            current.Remove(id);
            pending.Remove(id);
        }
    }

    private void OnStopped(object? sender, PlaybackStopEventArgs e) => Forget(e.Session?.Id);
    private void OnSessionEnded(object? sender, SessionEventArgs e) => Forget(e.SessionInfo?.Id);

    private bool IsCurrent(Pause pause)
    {
        lock (gate)
            return current.TryGetValue(pause.Session.Id, out var latest) && ReferenceEquals(latest, pause)
                && !webClients.IsActive(pause.Session.Id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await wake.Reader.WaitToReadAsync(stoppingToken))
            {
                wake.Reader.TryRead(out _);
                // Let the web overlay register and coalesce rapid paused seeks.
                await Task.Delay(300, stoppingToken);
                Pause[] batch;
                lock (gate)
                {
                    batch = pending.Values.ToArray();
                    pending.Clear();
                }
                foreach (var pause in batch)
                {
                    if (!IsCurrent(pause)) continue;
                    try
                    {
                        await Notify(pause, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        log.LogWarning(error, "Cannot send song notification to session {SessionId}", pause.Session.Id);
                        lock (gate)
                            if (current.TryGetValue(pause.Session.Id, out var latest) && ReferenceEquals(latest, pause))
                                current.Remove(pause.Session.Id); // Retry on the next real client report.
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task Notify(Pause pause, CancellationToken token)
    {
        var sourceId = pause.ItemId;
        // Never use the primary edition's timeline for an unknown alternate source.
        if (!string.IsNullOrEmpty(pause.MediaSourceId) && !Guid.TryParse(pause.MediaSourceId, out sourceId)) return;
        var user = users.GetUserById(pause.Session.UserId);
        if (user is null) return;
        var item = library.GetItemById<BaseItem>(sourceId, user);
        if (item is not (Movie or Episode) || !Path.IsPathFullyQualified(item.Path ?? "")) return;
        List<SongAtTime> songs;
        try
        {
            using var stream = File.OpenRead(Path.ChangeExtension(item.Path, ".songs.json")!);
            if (stream.Length > 1024 * 1024) throw new InvalidDataException("Timeline exceeds 1 MiB.");
            songs = await SongTimeline.At(stream, pause.PositionMs, token);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return; }
        if (songs.Count == 0 || !IsCurrent(pause) || !pause.Session.SupportsRemoteControl) return;
        // Android TV renders this as a short toast. The protocol has no dismiss command.
        using var delivery = CancellationTokenSource.CreateLinkedTokenSource(token);
        delivery.CancelAfter(TimeSpan.FromSeconds(5));
        await sessions.SendMessageCommand(null!, pause.Session.Id, new MessageCommand
        {
            Header = "Сейчас играет",
            Text = string.Join("\n", songs.Select(song => string.IsNullOrWhiteSpace(song.Artist)
                ? song.Title : $"{song.Artist} — {song.Title}")),
            TimeoutMs = 5000
        }, delivery.Token);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        sessions.PlaybackStart -= OnProgress;
        sessions.PlaybackProgress -= OnProgress;
        sessions.PlaybackStopped -= OnStopped;
        sessions.SessionEnded -= OnSessionEnded;
        return base.StopAsync(cancellationToken);
    }
}
