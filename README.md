# What Song? for Jellyfin

Pause a movie or TV episode in Jellyfin Web to see the song playing in that scene. The overlay reads a timeline stored beside the video. It updates when you seek while paused and disappears when playback resumes.

**Compatibility** · Jellyfin Server and Web 10.11.11, using the browser’s built-in HTML video player or Jellyfin Media Player 1.12 with its MPV player. The plugin appears as **Song Pause** in the Jellyfin dashboard.

[Инструкция на русском](README.ru.md)

## Install

1. Open **Dashboard → Plugins → Repositories → Add**. Name the repository `What Song?` and enter this manifest URL:

   ```text
   https://raw.githubusercontent.com/gutjei/jellyfin-what-song/main/manifest.json
   ```

2. Open **Catalog**, install **Song Pause**, and restart Jellyfin.
3. Reload Jellyfin Web. Add a timeline beside a video and pause playback. The plugin registers its overlay when the server serves the bundled web client.

**Offline installation.** Extract the DLL from the [1.2.1.0 package](packages/SongPause_1.2.1.0.zip) into `SongPause_1.2.1.0` under the server's plugins directory and restart. Uninstall through the plugin dashboard. If you added Song Pause to `index.html` and `config.json` manually for version 1.0, you can remove those entries.

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
dotnet publish Jellyfin.Plugin.SongPause.csproj -c Release -o dist
```

The output is `dist/Jellyfin.Plugin.SongPause.dll`. It includes the browser script. The server resolves the video path from the signed-in user’s library and reads the timeline on demand.

The pause interaction was inspired by [JRay](https://gitea.tourolle.paris/dtourolle/jRay/src/branch/master/README.md), which displays the actors in a scene.
## Package a catalog update

Increment the four-part project version, publish the DLL, then run `python tools/package.py "Release notes"`. Commit the new versioned ZIP and `manifest.json` together. Keep older ZIPs available for servers pinned to an earlier release.
