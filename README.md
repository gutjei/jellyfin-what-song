# What Song? for Jellyfin

Pause a movie or TV episode in Jellyfin Web to see the song playing in that scene. The overlay reads a timeline stored beside the video. It updates when you seek while paused and disappears when playback resumes.

**Compatibility** · Jellyfin Server and Web 10.11.11, using the browser’s built-in HTML video player. The plugin appears as **Song Pause** in the Jellyfin dashboard.

[Инструкция на русском](README.ru.md)

## Install

1. Build the DLL using the command below, or download the `SongPause` artifact from a successful GitHub Actions build and unzip it.
2. Create a `SongPause` folder under the server’s plugins directory and copy `Jellyfin.Plugin.SongPause.dll` into it. In the official Docker image, the destination is `/config/plugins/SongPause/`. Restart Jellyfin and check that **Song Pause** appears under **Dashboard → Plugins**.
3. In the `jellyfin-web` directory served by your server, add this line immediately after the opening `<head>` tag in `index.html`:

   ```html
   <script src="../SongPause/client.js"></script>
   ```

4. In the same directory, append `"SongPause"` to the existing `plugins` array in `config.json`. Reload the browser with a hard refresh.

A Jellyfin Web update can replace these two files. Reapply the edits after an upgrade, or persist the edited files as bind mounts in Docker. To uninstall, remove the script tag, the array entry and the plugin directory, then restart Jellyfin.

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