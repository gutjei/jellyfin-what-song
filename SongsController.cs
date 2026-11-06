using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SongPause;

[ApiController]
[Route("SongPause")]
public sealed class SongsController(ILibraryManager library, IUserManager users, ILogger<SongsController> log,
    IAuthorizationContext authorization, ISessionManager sessions, WebClientRegistry webClients) : ControllerBase
{
    [Authorize]
    [HttpPost("web-client")]
    public async Task<IActionResult> RegisterWebClient()
    {
        var auth = await authorization.GetAuthorizationInfo(Request);
        var session = sessions.GetSession(auth.DeviceId, auth.Client, auth.Version);
        if (session is null || session.UserId == Guid.Empty || session.UserId != auth.UserId)
            return Forbid();
        webClients.Register(session.Id);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("client.js")]
    public IActionResult Client() => File(typeof(Plugin).Assembly.GetManifestResourceStream("SongPause.Client")!, "text/javascript");

    [Authorize]
    [HttpGet("{sourceId:guid}/songs")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> Songs(Guid sourceId, [FromQuery] long? positionMs, [FromQuery] bool details, CancellationToken cancellationToken)
    {
        if (sourceId == Guid.Empty || positionMs is null or < 0)
            return BadRequest();
        if (!Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var userId) || users.GetUserById(userId) is not { } user)
            return Forbid();

        var item = library.GetItemById<BaseItem>(sourceId, user);
        if (item is not (Movie or Episode) || !Path.IsPathFullyQualified(item.Path ?? ""))
            return NotFound();

        var path = Path.ChangeExtension(item.Path, ".songs.json");
        try
        {
            using var stream = System.IO.File.OpenRead(path!);
            if (stream.Length > 1024 * 1024)
                throw new InvalidDataException("Timeline exceeds 1 MiB.");
            var songs = await SongTimeline.At(stream, positionMs.Value, cancellationToken);
            return details ? Ok(songs) : Ok(songs.Select(song =>
                string.IsNullOrWhiteSpace(song.Artist) ? song.Title : $"{song.Artist} — {song.Title}"));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Ok(Array.Empty<string>());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            log.LogWarning(e, "Cannot read song timeline for {ItemId}", sourceId);
            return UnprocessableEntity();
        }
    }
}
