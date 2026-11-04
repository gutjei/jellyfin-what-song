const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { test } = require('node:test');

function setup() {
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
    const document = {
        addEventListener() {},
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
    const context = { window: { addEventListener() {} }, document, console };
    vm.runInNewContext(fs.readFileSync(require.resolve('../song-pause.js'), 'utf8'), context);
    const player = { id: 'mpvvideoplayer', isLocalPlayer: true, paused: () => paused };
    let paused = false;
    let ticks = 100000000;
    let current = player;
    let resolveRequest;
    const requests = [];
    const manager = {
        getCurrentPlayer: () => current,
        getPlayerState: () => ({
            NowPlayingItem: { Id: 'item', ServerId: 'server', Type: 'Movie' },
            PlayState: { IsPaused: paused, PositionTicks: ticks, MediaSourceId: 'edition' }
        })
    };
    const api = {
        getUrl(route, query) { requests.push({ route, ...query }); return route; },
        ajax() { return new Promise(resolve => { resolveRequest = resolve; }); }
    };
    const Plugin = context.window.SongPause();
    new Plugin({ playbackManager: manager, events, ServerConnections: { getApiClient: () => api } });
    return {
        panels, requests,
        event(name) { events.trigger(player, name); },
        pause() { paused = true; events.trigger(player, 'pause'); },
        seek(ms) { ticks = ms * 10000; events.trigger(player, 'timeupdate'); },
        resume() { paused = false; events.trigger(player, 'unpause'); },
        rebind() { events.trigger(manager, 'playbackstart'); },
        remote() { current = { id: 'remote', isLocalPlayer: false }; events.trigger(manager, 'playerchange'); },
        async reply(songs) { resolveRequest(songs); await new Promise(setImmediate); }
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
