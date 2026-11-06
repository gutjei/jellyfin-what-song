# What Song? for Jellyfin

Pause a movie or TV episode in Jellyfin Web to see the song playing in that scene. The overlay reads a timeline stored beside the video. It updates when you seek while paused and disappears when playback resumes.

**Compatibility** · Jellyfin Server and Web 10.11.11. Web overlays use Jellyfin's local player events without an MPV/VLC name whitelist. Native clients receive a standard `DisplayMessage` if they report pause/position and support remote messages over a working WebSocket. Android TV renders a short toast, not a persistent overlay; its duration and appearance belong to the client, and the protocol has no dismiss command. The plugin appears as **Song Pause** in the Jellyfin dashboard.

**External Android players:** launching VLC/MPV as a separate app through an Android Intent does not report intermediate pauses/seeks to Jellyfin. The stock Android TV client reports the result after returning from the external player. Pause overlays in this scenario require player/client integration; a server plugin alone cannot provide them. An embedded playback engine is a different scenario from a separate application. Android TV behavior was checked against upstream source, not a physical TV.

[Инструкция на русском](README.ru.md)

## Install

1. Open **Dashboard → Plugins → Repositories → Add**. Name the repository `What Song?` and enter this manifest URL:

   ```text
   https://raw.githubusercontent.com/gutjei/jellyfin-what-song/main/manifest.json
   ```

2. Open **Catalog**, install **Song Pause**, and restart Jellyfin.
3. Reload Jellyfin Web. Add a timeline beside a video and pause playback. The plugin registers its overlay when the server serves the bundled web client.

**Development build (1.3.0.0).** Download the `SongPause` artifact from a successful [Build workflow](https://github.com/gutjei/jellyfin-what-song/actions/workflows/build.yml) run, or build locally and run `python tools/package.py`. The ZIP is generated in `dist/`; it is not stored in Git. The plugin catalog remains on the last published release until the new release asset is uploaded and its catalog entry is published.

**Offline installation.** Extract the DLL from `SongPause_1.3.0.0.zip` into `SongPause_1.3.0.0` under the server's plugins directory and restart. Uninstall through the plugin dashboard. If you added Song Pause to `index.html` and `config.json` manually for version 1.0, you can remove those entries.

## Jellyfin Media Player

Use the web client provided by your Jellyfin server. If your desktop app uses a bundled web client, launch it with `--force-external-webclient`. Restart the app after updating the server plugin so it loads the new script. The song card appears over MPV video when paused and updates when you seek while paused.

## Add songs to a video

Place `Movie.songs.json` next to `Movie.mkv`, or `Show S01E02.songs.json` next to `Show S01E02.mp4`. Each cut of a movie uses the timeline named after its own video file.

```json
{
  "version": 1,
  "songs": [
    { "startMs": 5000, "endMs": 20000, "artist": "Quiet Shore", "title": "First Snow" }
  ]
}
```

Times are integer milliseconds from the start of the video. The start is inclusive and the end is exclusive. Each interval needs a nonnegative start, a later end and a nonempty title. The artist is optional. Overlapping intervals display all matching songs in file order. Use UTF-8 JSON with case-sensitive field names and a maximum file size of 1 MiB.

**Try it.** Copy [the example timeline](examples/Demo.songs.json) beside a video longer than 32 seconds and rename it to match. Pause at 10 seconds to see one track, seek to 19 seconds to see both, then seek to 32 seconds to clear the overlay. Edits apply on the next pause or seek.

## Build

With the .NET 9 SDK installed, run from the repository root:

```sh
node --test tests/song-pause.test.cjs
dotnet test tests/SongPause.Tests.csproj -c Release
dotnet publish Jellyfin.Plugin.SongPause.csproj -c Release -o dist
```

Tests simulate the player and session contracts, including late responses, paused seeks, edition selection and isolated sessions. They do not run physical Android TV devices or external VLC/MPV applications. The web overlay renews a 90-second session lease every 30 seconds to suppress duplicate native notifications. Native notifications use real client progress, not Jellyfin's estimated automatic progress.

For native-client troubleshooting, check the session's `SupportedCommands` for `DisplayMessage`, `SupportsRemoteControl=true`, and real pause/position reports. A debug log entry containing `cannot receive DisplayMessage` indicates missing messaging capability or a disconnected WebSocket.

The output is `dist/Jellyfin.Plugin.SongPause.dll`. It includes the browser script. The server resolves the video path from the signed-in user’s library and reads the timeline on demand.

The pause interaction was inspired by [JRay](https://gitea.tourolle.paris/dtourolle/jRay/src/branch/master/README.md), which displays the actors in a scene.
## Publish a release

Commit the four-part version in the project file to main, then push a matching tag.

```sh
git tag v1.3.0.0
git push origin v1.3.0.0
```

The Release workflow runs the tests, builds the DLL, packages it, and publishes the ZIP in GitHub Releases. It downloads the public asset and compares it with the build output before committing its entry to the root manifest. Existing catalog entries are retained. Re-running a release verifies an existing asset before continuing. Branch protection must allow the workflow's GitHub token to update manifest.json on main.

For local package inspection, run `python tools/package.py "Release notes"` after publishing the DLL. The resulting ZIP and candidate catalog are written under ignored `dist/`.
