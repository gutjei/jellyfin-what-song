// Jellyfin Web 10.11 loads this factory by its name in config.json.
window.SongPause = () => class SongPause {
    constructor({ playbackManager, events, ServerConnections }) {
        this.id = 'SongPause';
        this.name = 'Song Pause';
        let generation = 0;
        let panel;
        let nativePlayer;
        let nativePosition;

        function clear() {
            generation++;
            nativePosition = undefined;
            panel?.remove();
            panel = undefined;
        }

        async function show(video) {
            const player = playbackManager.getCurrentPlayer();
            if (!player?.isLocalPlayer || (video && (!video.paused || video.ended || video.seeking || !video.isConnected))) {
                clear();
                return;
            }

            try {
                const { NowPlayingItem: item, PlayState: state } = playbackManager.getPlayerState(player);
                if (!state.IsPaused || !['Movie', 'Episode'].includes(item?.Type)) {
                    clear();
                    return;
                }
                const sourceId = state.MediaSourceId || item.Id;
                // The HTML video clock updates before Jellyfin's cached clock on seek.
                const offsetMs = video ? (state.PositionTicks - Math.floor(player.currentTime() * 10000)) / 10000 : 0;
                const positionMs = Math.floor(video ? video.currentTime * 1000 + offsetMs : state.PositionTicks / 10000);
                if (!sourceId || !Number.isSafeInteger(positionMs) || positionMs < 0) return;
                const position = sourceId + '/' + positionMs;
                if (!video && nativePosition === position) return;
                clear();
                if (!video) nativePosition = position;
                const request = generation;
                const api = ServerConnections.getApiClient(item.ServerId);
                const songs = await api.ajax({
                    type: 'GET',
                    dataType: 'json',
                    url: api.getUrl('SongPause/' + encodeURIComponent(sourceId) + '/songs', { positionMs })
                });
                if (request !== generation || player !== playbackManager.getCurrentPlayer() || !player.paused() || !songs.length) return;
                if (video && (!video.isConnected || video.seeking || !video.paused)) return;
                panel = document.createElement('aside');
                panel.setAttribute('role', 'status');
                panel.style.cssText = 'position:fixed;left:5vw;bottom:24vh;z-index:100000;pointer-events:none;background:#141414ed;color:white;padding:.7em 1em;border-radius:.55em;max-width:80vw;white-space:pre-line;overflow-wrap:anywhere;font:600 clamp(16px,2vw,28px)/1.4 sans-serif';
                panel.textContent = '♫  ' + songs.join('\n♫  ');
                (document.fullscreenElement || document.body).append(panel);
            } catch (error) {
                nativePosition = undefined;
                console.debug('Song Pause:', error);
            }
        }

        // MPV reports events through Jellyfin's event bus and has no HTML video.
        function nativeUpdate() {
            if (nativePlayer?.paused()) void show();
            else clear();
        }

        function bindNativePlayer() {
            clear();
            if (nativePlayer) {
                for (const event of ['pause', 'timeupdate', 'playing', 'unpause']) {
                    events.off(nativePlayer, event, nativeUpdate);
                }
                events.off(nativePlayer, 'stopped', onStop);
            }
            const player = playbackManager.getCurrentPlayer();
            nativePlayer = player?.id === 'mpvvideoplayer' ? player : undefined;
            if (nativePlayer) {
                for (const event of ['pause', 'timeupdate', 'playing', 'unpause']) {
                    events.on(nativePlayer, event, nativeUpdate);
                }
                events.on(nativePlayer, 'stopped', onStop);
                nativeUpdate();
            }
        }

        function onStop() {
            clear();
        }

        for (const event of ['pause', 'seeked', 'play', 'playing', 'seeking', 'ended', 'emptied']) {
            document.addEventListener(event, ({ target }) => {
                if (!(target instanceof HTMLVideoElement) || !target.closest('.videoPlayerContainer')) return;
                if (event === 'pause' || event === 'seeked') void show(target);
                else clear();
            }, true);
        }
        events.on(playbackManager, 'playbackstop', clear);
        events.on(playbackManager, 'playerchange', bindNativePlayer);
        events.on(playbackManager, 'playbackstart', bindNativePlayer);
        window.addEventListener('pagehide', clear);
        window.addEventListener('hashchange', clear);
        bindNativePlayer();
    }
};
