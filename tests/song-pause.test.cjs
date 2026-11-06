const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { test } = require('node:test');

function setup(playerId = 'mpvvideoplayer', html = false) {
    const callbacks = new WeakMap();
    const events = {
        on(object, name, callback) {
            if (!callbacks.has(object)) callbacks.set(object, {});
            const names = callbacks.get(object);
            (names[name] ??= new Set()).add(callback);
        },
        off(object, name, callback) { callbacks.get(object)?.[name]?.delete(callback); },
        trigger(object, name) {
            for (const callback of [...(callbacks.get(object)?.[name] ?? [])]) callback({ type: name });
        }
    };
    const panels = new Set();
    const domEvents = {};
    const windowEvents = {};
    class Video {
        paused = false;
        ended = false;
        seeking = false;
        isConnected = true;
        currentTime = 10;
        closest() { return true; }
    }
    const video = html ? new Video() : undefined;
    let heartbeat;
    const document = {
        addEventListener(name, callback) { domEvents[name] = callback; },
        querySelector() { return video; },
        createElement() {
            return {
                style: {}, children: [], setAttribute() {},
                append(child) { this.children.push(child); },
                get textContent() { return this.text ?? this.children.map(child => child.textContent).join(''); },
                set textContent(value) { this.text = value; },
                remove() { panels.delete(this); }
            };
        },
        body: { append(panel) { panels.add(panel); } }
    };
    const context = { window: {
        addEventListener(name, callback) { windowEvents[name] = callback; },
        setInterval(callback) { heartbeat = callback; }
    }, document, console, HTMLVideoElement: Video };
    vm.runInNewContext(fs.readFileSync(process.env.SONG_PAUSE_SCRIPT || require.resolve('../song-pause.js'), 'utf8'), context);
    const player = { id: playerId, isLocalPlayer: true, paused: () => paused, currentTime: () => 10000 };
    let paused = false;
    let ticks = 100000000;
    let current = player;
    const pending = [];
    let registrations = 0;
    const requests = [];
    const manager = {
        getCurrentPlayer: () => current,
        getPlayerState: () => ({
            NowPlayingItem: { Id: 'item', ServerId: 'server', Type: 'Movie' },
            PlayState: { IsPaused: paused, PositionTicks: ticks, MediaSourceId: 'edition' }
        })
    };
    const api = {
        getUrl(route, query) { if (query) requests.push({ route, ...query }); return route; },
        ajax(options) {
            if (options.type === 'POST') { registrations++; return Promise.resolve(); }
            return new Promise((resolve, reject) => { pending.push({ resolve, reject }); });
        }
    };
    const Plugin = context.window.SongPause();
    new Plugin({ playbackManager: manager, events, ServerConnections: { getApiClient: () => api } });
    return {
        panels, requests, video,
        get registrations() { return registrations; },
        heartbeat() { heartbeat(); },
        dom(name) { domEvents[name]({ target: video }); },
        hide() { windowEvents.pagehide(); },
        event(name) { events.trigger(player, name); },
        pause() { paused = true; if (video) video.paused = true; events.trigger(player, 'pause'); },
        seek(ms) { ticks = ms * 10000; events.trigger(player, 'timeupdate'); },
        resume() { paused = false; events.trigger(player, 'unpause'); },
        rebind() { events.trigger(manager, 'playbackstart'); },
        remote() { current = { id: 'remote', isLocalPlayer: false }; events.trigger(manager, 'playerchange'); },
        async reply(songs, index = pending.length - 1) { pending[index].resolve(songs); await new Promise(setImmediate); },
        async fail(index = pending.length - 1) { pending[index].reject(new Error('offline')); await new Promise(setImmediate); }
    };
}

test('MPV pause and paused seek render a song without an HTML video', async () => {
    const app = setup();
    app.pause();
    assert.deepEqual(app.requests[0], { route: 'SongPause/edition/songs', positionMs: 10000, details: true });
    await app.reply([{ title: 'First', artist: 'Artist' }]);
    assert.equal([...app.panels][0].textContent, 'Сейчас играетFirstArtist');
    app.seek(19000);
    assert.equal(app.requests[1].positionMs, 19000);
    await app.reply([{ title: 'Second' }]);
    assert.equal([...app.panels][0].textContent, 'Сейчас играетSecond');
    app.event('timeupdate');
    assert.equal(app.requests.length, 2);
    app.resume();
    assert.equal(app.panels.size, 0);
});

for (const id of ['vlcvideoplayer', 'native-tv-player', 'future-player']) {
    test(`${id}: pause and seek work without an HTML video or an MPV id`, async () => {
        const app = setup(id);
        app.pause();
        assert.equal(app.requests[0].positionMs, 10000);
        await app.reply([{ title: 'First' }]);
        assert.equal(app.panels.size, 1);
        app.seek(19000);
        await app.reply([{ title: 'Second' }]);
        assert.equal([...app.panels][0].textContent, 'Сейчас играетSecond');
        app.resume();
        assert.equal(app.panels.size, 0);
    });
}

test('HTML seek uses the video clock plus the stream offset', async () => {
    const app = setup('htmlvideoplayer', true);
    app.seek(70000); // Stream began at 60 seconds; cached local clock is 10 seconds.
    app.pause();
    assert.equal(app.requests.at(-1).positionMs, 70000);
    await app.reply([{ title: 'First' }]);
    app.video.currentTime = 19;
    app.dom('seeked');
    assert.equal(app.requests.at(-1).positionMs, 79000);
    await app.reply([{ title: 'After seek' }]);
    assert.equal([...app.panels][0].textContent, 'Сейчас играетAfter seek');
});

test('out-of-order replies never overwrite the latest paused seek', async () => {
    const app = setup('vlcvideoplayer');
    app.pause();
    app.seek(19000);
    await app.reply([{ title: 'New' }], 1);
    await app.reply([{ title: 'Old' }], 0);
    assert.equal([...app.panels][0].textContent, 'Сейчас играетNew');
});

test('an older failed request cannot discard the current position deduplication', async () => {
    const app = setup();
    app.pause();
    app.seek(19000);
    await app.reply([{ title: 'New' }], 1);
    await app.fail(0);
    app.event('timeupdate');
    assert.equal(app.requests.length, 2);
    assert.equal([...app.panels][0].textContent, 'Сейчас играетNew');
});

test('HTML DOM and player-bus events share one request per position', async () => {
    const app = setup('htmlvideoplayer', true);
    app.pause();
    app.dom('pause');
    app.event('timeupdate');
    assert.equal(app.requests.length, 1);
    await app.reply([{ title: 'Song' }]);
    assert.equal(app.panels.size, 1);
});

test('a stale HTML video cannot supply the clock for the active native adapter', async () => {
    const app = setup('vlcvideoplayer', true);
    app.video.currentTime = 900;
    app.video.ended = true;
    app.pause();
    assert.equal(app.requests[0].positionMs, 10000);
    await app.reply([{ title: 'Active player song' }]);
    assert.equal([...app.panels][0].textContent, 'Сейчас играетActive player song');
});

test('failed requests can retry; gaps remove the previous song', async () => {
    const app = setup();
    app.pause();
    await app.fail();
    app.event('timeupdate');
    assert.equal(app.requests.length, 2);
    await app.reply([{ title: 'Song' }]);
    app.seek(32000);
    await app.reply([]);
    assert.equal(app.panels.size, 0);
});

test('stopped players cannot restore the overlay through later timeupdate events', async () => {
    const app = setup();
    app.pause();
    app.event('stopped');
    app.event('timeupdate');
    assert.equal(app.requests.length, 1);
    await app.reply([{ title: 'Old' }]);
    assert.equal(app.panels.size, 0);
});

test('web renderer registers and renews only the local session', () => {
    const app = setup();
    assert.equal(app.registrations, 1);
    app.heartbeat();
    assert.equal(app.registrations, 2);
    app.remote();
    app.heartbeat();
    assert.equal(app.registrations, 2);
});

test('late responses cannot restore the overlay after resume, stop or player change', async () => {
    for (const action of ['resume', 'stop', 'remote']) {
        const app = setup();
        app.pause();
        if (action === 'stop') app.event('stopped');
        else app[action]();
        await app.reply(['Stale']);
        assert.equal(app.panels.size, 0, action);
    }
});

test('repeated player binding removes old MPV listeners', async () => {
    const app = setup();
    app.rebind();
    app.rebind();
    app.pause();
    assert.equal(app.requests.length, 1);
    await app.reply([{ title: 'Song' }]);
    assert.equal(app.panels.size, 1);
});
