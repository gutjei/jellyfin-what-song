// Jellyfin Web 10.11 loads this factory by its name in config.json.
window.SongPause = () => class SongPause {
    constructor({ playbackManager, events, ServerConnections }) {
        this.id = 'SongPause';
        this.name = 'Song Pause';
        let generation = 0;
        let panel;
        let nativePlayer;
        let nativePosition;
        const playerEvents = ['pause', 'timeupdate', 'playing', 'unpause', 'seeked'];

        function registerWebClient() {
            try {
                const player = playbackManager.getCurrentPlayer();
                if (!player?.isLocalPlayer) return;
                const item = playbackManager.getPlayerState(player)?.NowPlayingItem;
                if (!item?.ServerId) return;
                const api = ServerConnections.getApiClient(item.ServerId);
                Promise.resolve(api.ajax({ type: 'POST', url: api.getUrl('SongPause/web-client') }))
                    .catch(error => console.debug('Song Pause registration:', error));
            } catch (error) {
                console.debug('Song Pause registration:', error);
            }
        }

        function clear() {
            generation++;
            nativePosition = undefined;
            panel?.remove();
            panel = undefined;
        }

        async function show(video) {
            let request;
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
                if (!sourceId || !Number.isSafeInteger(positionMs) || positionMs < 0) { clear(); return; }
                const position = sourceId + '/' + positionMs;
                if (nativePosition === position) return;
                clear();
                nativePosition = position;
                request = generation;
                const api = ServerConnections.getApiClient(item.ServerId);
                const songs = await api.ajax({
                    type: 'GET',
                    dataType: 'json',
                    url: api.getUrl('SongPause/' + encodeURIComponent(sourceId) + '/songs', { positionMs, details: true })
                });
                if (request !== generation || player !== playbackManager.getCurrentPlayer()
                    || !playbackManager.getPlayerState(player)?.PlayState?.IsPaused || !songs.length) return;
                if (video && (!video.isConnected || video.seeking || !video.paused)) return;
                panel = document.createElement('aside');
                panel.setAttribute('role', 'status');
                panel.style.cssText = 'position:fixed;top:clamp(48px,5vh,72px);right:clamp(16px,3vw,64px);width:max-content;max-width:min(clamp(360px,40.5vw,630px),calc(100vw - 32px));max-height:65vh;overflow:hidden;box-sizing:border-box;z-index:100000;pointer-events:none;color:#fff;font:400 clamp(14px,1.15vw,20px)/1.4 sans-serif;overflow-wrap:anywhere';
                const heading = document.createElement('div');
                heading.textContent = 'Сейчас играет';
                heading.style.cssText = 'font-weight:600;margin-bottom:.5em;text-shadow:0 1px 4px #000';
                panel.append(heading);
                for (const song of songs) {
                    const card = document.createElement('div');
                    card.style.cssText = 'background:rgba(28,28,28,.92);padding:.9em 1.1em;border-radius:.65em;margin-bottom:.5em;box-shadow:0 4px 18px #0003';
                    const title = document.createElement('div');
                    title.textContent = song.title;
                    title.style.fontWeight = '600';
                    card.append(title);
                    if (song.artist) {
                        const artist = document.createElement('div');
                        artist.textContent = song.artist;
                        artist.style.cssText = 'color:#b8b8b8;font-size:.9em;margin-top:.2em';
                        card.append(artist);
                    }
                    panel.append(card);
                }
                (document.fullscreenElement || document.body).append(panel);
            } catch (error) {
                if (request === generation) nativePosition = undefined;
                console.debug('Song Pause:', error);
            }
        }

        // All local player adapters use Jellyfin's event bus, regardless of engine.
        function nativeUpdate() {
            if (!nativePlayer || nativePlayer !== playbackManager.getCurrentPlayer()) return;
            // A hidden HTML video may remain mounted while a native engine is active.
            // Bus events use the active player's clock; DOM seeks pass their own video.
            void show();
        }

        function unbindNativePlayer() {
            clear();
            if (nativePlayer) {
                for (const event of playerEvents) {
                    events.off(nativePlayer, event, nativeUpdate);
                }
                events.off(nativePlayer, 'stopped', onStop);
                events.off(nativePlayer, 'seeking', clear);
            }
            nativePlayer = undefined;
        }

        function bindNativePlayer() {
            unbindNativePlayer();
            const player = playbackManager.getCurrentPlayer();
            nativePlayer = player?.isLocalPlayer ? player : undefined;
            if (nativePlayer) {
                registerWebClient();
                for (const event of playerEvents) {
                    events.on(nativePlayer, event, nativeUpdate);
                }
                events.on(nativePlayer, 'stopped', onStop);
                events.on(nativePlayer, 'seeking', clear);
                nativeUpdate();
            }
        }

        function onStop() {
            unbindNativePlayer();
        }

        for (const event of ['pause', 'seeked', 'play', 'playing', 'seeking', 'ended', 'emptied']) {
            document.addEventListener(event, ({ target }) => {
                if (!(target instanceof HTMLVideoElement) || !target.closest('.videoPlayerContainer')) return;
                if (event === 'pause' || event === 'seeked') void show(target);
                else clear();
            }, true);
        }
        events.on(playbackManager, 'playbackstop', unbindNativePlayer);
        events.on(playbackManager, 'playerchange', bindNativePlayer);
        events.on(playbackManager, 'playbackstart', bindNativePlayer);
        window.addEventListener('pagehide', unbindNativePlayer);
        window.addEventListener('hashchange', clear);
        window.setInterval(registerWebClient, 30000);
        bindNativePlayer();
    }
};
