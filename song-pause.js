// Jellyfin Web 10.11 loads this factory by its name in config.json.
window.SongPause = () => class SongPause {
    constructor({ playbackManager, events, ServerConnections }) {
        this.id = 'SongPause';
        this.name = 'Song Pause';
        let generation = 0;
        let panel;

        function clear() {
            generation++;
            panel?.remove();
            panel = undefined;
        }

        async function show(video) {
            clear();
            const request = generation;
            if (!video.paused || video.ended || video.seeking || !video.isConnected) return;
            try {
                const player = playbackManager.getCurrentPlayer();
                if (!player?.isLocalPlayer) return;
                const { NowPlayingItem: item, PlayState: state } = playbackManager.getPlayerState(player);
                if (!['Movie', 'Episode'].includes(item?.Type)) return;
                const sourceId = state.MediaSourceId || item.Id;
                // Jellyfin adds the transcode offset to its cached media clock.
                // During a paused seek the video element already has the new time.
                const offsetMs = (state.PositionTicks - Math.floor(player.currentTime() * 10000)) / 10000;
                const positionMs = Math.floor(video.currentTime * 1000 + offsetMs);
                if (!sourceId || !Number.isSafeInteger(positionMs) || positionMs < 0) return;
                const api = ServerConnections.getApiClient(item.ServerId);
                const songs = await api.ajax({
                    type: 'GET',
                    dataType: 'json',
                    url: api.getUrl('SongPause/' + encodeURIComponent(sourceId) + '/songs', { positionMs })
                });
                if (request !== generation || !video.isConnected || !video.paused || !songs.length) return;
                panel = document.createElement('aside');
                panel.setAttribute('role', 'status');
                panel.style.cssText = 'position:fixed;left:5vw;bottom:24vh;z-index:100000;pointer-events:none;background:#141414ed;color:white;padding:.7em 1em;border-radius:.55em;max-width:80vw;white-space:pre-line;overflow-wrap:anywhere;font:600 clamp(16px,2vw,28px)/1.4 sans-serif';
                panel.textContent = '♫  ' + songs.join('\n♫  ');
                (document.fullscreenElement || document.body).append(panel);
            } catch (error) {
                console.debug('Song Pause:', error);
            }
        }

        for (const event of ['pause', 'seeked', 'play', 'playing', 'seeking', 'ended', 'emptied']) {
            document.addEventListener(event, ({ target }) => {
                if (!(target instanceof HTMLVideoElement) || !target.closest('.videoPlayerContainer')) return;
                if (event === 'pause' || event === 'seeked') void show(target);
                else clear();
            }, true);
        }
        events.on(playbackManager, 'playbackstop', clear);
        events.on(playbackManager, 'playerchange', clear);
        window.addEventListener('pagehide', clear);
        window.addEventListener('hashchange', clear);
    }
};
