// Tests of the client add-on, run with Node's built-in test runner: node --test "Jellyfin.Plugin.DetectAspectRatio.Tests/Client/*.test.mjs"

import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, mock, test } from 'node:test';

import DetectAspectRatio, {
    addDetectedAspectRatio,
    applyCrop,
    createCropAspectRatioLoader,
    findVideoElement,
    getCropAspectRatio,
    patchVideoPlayer,
    resolveAspectRatio,
    whenVideoPlayersRegistered
} from '../../Jellyfin.Plugin.DetectAspectRatio/Client/detectAspectRatio.js';

const SCOPE_SOURCE_ID = '0123456789abcdef0123456789abcdef';
const FLAT_SOURCE_ID = 'fedcba98-7654-3210-fedc-ba9876543210';
const VALID_OBJECT_FITS = new Set(['contain', 'cover', 'fill', 'none', 'scale-down']);

/** Stands in for CSSStyleDeclaration: assigning an invalid object-fit is ignored, as browsers do. */
function createStyle() {
    const declarations = new Map();
    const style = {
        setProperty(name, value, priority = '') {
            declarations.set(name, { value, priority });
        },
        removeProperty(name) {
            declarations.delete(name);
        },
        getPropertyValue(name) {
            return declarations.has(name) ? declarations.get(name).value : '';
        },
        getPropertyPriority(name) {
            return declarations.has(name) ? declarations.get(name).priority : '';
        }
    };
    return new Proxy(style, {
        set(target, property, value) {
            if (property === 'object-fit' && VALID_OBJECT_FITS.has(value)) {
                declarations.set(property, { value, priority: '' });
            }
            return true;
        }
    });
}

function createAppSettings() {
    let savedAspectRatio = '';
    return {
        aspectRatio(value) {
            if (value !== undefined) {
                savedAspectRatio = value;
                return undefined;
            }
            return savedAspectRatio;
        }
    };
}

/** Mirrors the stock HtmlVideoPlayer: its element and aspect ratio code are private. */
class StockVideoPlayer {
    id = 'htmlvideoplayer';
    #appSettings;
    #element;
    #elementReady;

    constructor(appSettings, element, elementReady = Promise.resolve()) {
        this.#appSettings = appSettings;
        this.#element = element;
        this.#elementReady = elementReady;
    }

    async play(options) {
        const element = await this.createMediaElement(options);
        this.#applyAspectRatio(options.aspectRatio || this.getAspectRatio());
        return element;
    }

    createMediaElement() {
        return this.#elementReady.then(() => this.#element);
    }

    setAspectRatio(value) {
        this.#appSettings.aspectRatio(value);
        this.#applyAspectRatio(value);
    }

    getAspectRatio() {
        return this.#appSettings.aspectRatio() || 'auto';
    }

    getSupportedAspectRatios() {
        return [{ name: 'Auto', id: 'auto' }, { name: 'Zoom', id: 'cover' }, { name: 'Stretch', id: 'fill' }];
    }

    #applyAspectRatio(value) {
        if (value === 'auto') {
            this.#element.style.removeProperty('object-fit');
        } else {
            this.#element.style['object-fit'] = value;
        }
    }
}

/** Mirrors the WebGPU player: it hands its aspect ratio calls to a private HTML player and does not hand out its element. */
class DelegatingVideoPlayer {
    id = 'webgpuplayer';
    #delegate;

    constructor(appSettings, element) {
        this.#delegate = new StockVideoPlayer(appSettings, element);
    }

    play(options) {
        return this.#delegate.play(options);
    }

    setAspectRatio(value) {
        this.#delegate.setAspectRatio(value);
    }

    getAspectRatio() {
        return this.#delegate.getAspectRatio();
    }

    getSupportedAspectRatios() {
        return this.#delegate.getSupportedAspectRatios();
    }
}

/** Mirrors Jellyfin Web's Events helper: handlers receive the event first. */
function createEvents() {
    const handlers = new Map();
    return {
        on(target, type, handler) {
            const key = `${type}`;
            if (!handlers.has(target)) {
                handlers.set(target, new Map());
            }
            const targetHandlers = handlers.get(target);
            targetHandlers.set(key, [...(targetHandlers.get(key) || []), handler]);
        },
        trigger(target, type, args = []) {
            const targetHandlers = handlers.get(target);
            for (const handler of (targetHandlers && targetHandlers.get(type)) || []) {
                handler.apply(target, [{ type }, ...args]);
            }
        }
    };
}

/** Records crop ratio requests and settles them on demand. */
function createLoader() {
    const requests = [];
    const load = (item, mediaSourceId) => new Promise((resolve) => {
        requests.push({ item, mediaSourceId, resolve });
    });
    return { load, requests };
}

function createHarness(elementReady) {
    const element = { style: createStyle() };
    const appSettings = createAppSettings();
    const player = new StockVideoPlayer(appSettings, element, elementReady);
    const events = createEvents();
    const loader = createLoader();
    patchVideoPlayer(player, loader.load, events);
    return { element, appSettings, player, events, loader };
}

function playOptions(mediaSourceId, extra = {}) {
    return { item: { Id: mediaSourceId, ServerId: 'server' }, mediaSource: { Id: mediaSourceId }, ...extra };
}

function flush() {
    return new Promise((resolve) => setImmediate(resolve));
}

function cropStyles(element) {
    const style = element.style;
    return {
        width: style.getPropertyValue('width'),
        maxWidth: style.getPropertyValue('max-width'),
        height: style.getPropertyValue('height'),
        maxHeight: style.getPropertyValue('max-height'),
        objectFit: style.getPropertyValue('object-fit'),
        margin: style.getPropertyValue('margin'),
        marginPriority: style.getPropertyPriority('margin')
    };
}

const UNCROPPED = { width: '', maxWidth: '', height: '', maxHeight: '', objectFit: '', margin: '', marginPriority: '' };

describe('resolveAspectRatio', () => {
    test('Auto crops every measured video', () => {
        assert.equal(resolveAspectRatio('auto', 2.39), 'detected');
        assert.equal(resolveAspectRatio('', 2.39), 'detected');
        assert.equal(resolveAspectRatio('auto', 0), 'detected');
        assert.equal(resolveAspectRatio('auto', null), 'auto');
        assert.equal(resolveAspectRatio('', null), 'auto');
    });

    test('a saved Detected falls back to Auto for an unmeasured video', () => {
        assert.equal(resolveAspectRatio('detected', 1.85), 'detected');
        assert.equal(resolveAspectRatio('detected', null), 'auto');
    });

    test('Zoom and Stretch stay as chosen', () => {
        assert.equal(resolveAspectRatio('cover', 2.39), 'cover');
        assert.equal(resolveAspectRatio('fill', null), 'fill');
    });
});

describe('addDetectedAspectRatio', () => {
    const stock = [{ name: 'Auto', id: 'auto' }, { name: 'Zoom', id: 'cover' }, { name: 'Stretch', id: 'fill' }];

    test('lists Detected right after Auto for a measured video', () => {
        assert.deepEqual(addDetectedAspectRatio(stock, 0).map(({ id }) => id), ['auto', 'detected', 'cover', 'fill']);
        assert.deepEqual(addDetectedAspectRatio(stock, 2.39)[1], { name: 'Detected', id: 'detected' });
        assert.equal(stock.length, 3);
    });

    test('leaves the list alone for an unmeasured video, or one that already lists Detected', () => {
        assert.equal(addDetectedAspectRatio(stock, null), stock);
        const listed = addDetectedAspectRatio(stock, 2.39);
        assert.equal(addDetectedAspectRatio(listed, 2.39), listed);
    });

    test('puts Detected first when the player has no Auto', () => {
        assert.deepEqual(addDetectedAspectRatio([{ name: 'Zoom', id: 'cover' }], 2.39).map(({ id }) => id), ['detected', 'cover']);
    });
});

describe('getCropAspectRatio', () => {
    test('crops to the snapped ratio of a measured video', () => {
        assert.equal(getCropAspectRatio({ IsMeasured: true, AspectRatio: 2.388, SnappedAspectRatio: 2.39 }), 2.39);
    });

    test('crops nothing from a video without black bars or with an odd ratio', () => {
        assert.equal(getCropAspectRatio({ IsMeasured: true, AspectRatio: 0, SnappedAspectRatio: 0 }), 0);
        assert.equal(getCropAspectRatio({ IsMeasured: true, AspectRatio: 3.2, SnappedAspectRatio: 0 }), 0);
        assert.equal(getCropAspectRatio({ IsMeasured: true }), 0);
    });

    test('has no ratio for an unmeasured video or a malformed answer', () => {
        assert.equal(getCropAspectRatio({ IsMeasured: false, SnappedAspectRatio: 2.39 }), null);
        assert.equal(getCropAspectRatio({ isMeasured: true, snappedAspectRatio: 2.39 }), null);
        assert.equal(getCropAspectRatio(null), null);
        assert.equal(getCropAspectRatio(undefined), null);
    });
});

describe('applyCrop', () => {
    test('sizes the element to the picture, fitted to the viewport, and covers it', () => {
        const element = { style: createStyle() };

        applyCrop(element, 'detected', 2.39);

        assert.deepEqual(cropStyles(element), {
            width: 'calc(100vh * 2.39)',
            maxWidth: '100vw',
            height: 'calc(100vw / 2.39)',
            maxHeight: '100vh',
            objectFit: 'cover',
            margin: 'auto',
            marginPriority: 'important'
        });
    });

    test('restores the player sizing for every other aspect ratio and leaves its object-fit', () => {
        const element = { style: createStyle() };
        applyCrop(element, 'detected', 2.39);

        applyCrop(element, 'cover', 2.39);

        assert.deepEqual(cropStyles(element), { ...UNCROPPED, objectFit: 'cover' });
    });

    test('shows the whole picture for Detected without black bars', () => {
        const element = { style: createStyle() };
        applyCrop(element, 'detected', 2.39);

        applyCrop(element, 'detected', 0);

        assert.deepEqual(cropStyles(element), UNCROPPED);
    });
});

describe('patchVideoPlayer', () => {
    test('crops a measured video once the server answers', async () => {
        const { element, player, loader } = createHarness();

        await player.play(playOptions(SCOPE_SOURCE_ID));

        assert.equal(loader.requests.length, 1);
        assert.equal(loader.requests[0].mediaSourceId, SCOPE_SOURCE_ID);
        assert.deepEqual(loader.requests[0].item, { Id: SCOPE_SOURCE_ID, ServerId: 'server' });
        assert.equal(player.getAspectRatio(), 'auto');
        assert.deepEqual(player.getSupportedAspectRatios().map(({ id }) => id), ['auto', 'cover', 'fill']);
        assert.deepEqual(cropStyles(element), UNCROPPED);

        loader.requests[0].resolve(2.39);
        await flush();

        assert.equal(player.getAspectRatio(), 'detected');
        assert.deepEqual(player.getSupportedAspectRatios().map(({ id }) => id), ['auto', 'detected', 'cover', 'fill']);
        assert.equal(cropStyles(element).width, 'calc(100vh * 2.39)');
        assert.equal(cropStyles(element).objectFit, 'cover');
    });

    test('Zoom and Stretch replace the crop, and Auto brings it back', async () => {
        const { element, player, loader } = createHarness();
        await player.play(playOptions(SCOPE_SOURCE_ID));
        loader.requests[0].resolve(2.39);
        await flush();

        player.setAspectRatio('fill');
        assert.equal(player.getAspectRatio(), 'fill');
        assert.deepEqual(cropStyles(element), { ...UNCROPPED, objectFit: 'fill' });

        player.setAspectRatio('auto');
        assert.equal(player.getAspectRatio(), 'detected');
        assert.equal(cropStyles(element).height, 'calc(100vw / 2.39)');
        assert.equal(cropStyles(element).objectFit, 'cover');
    });

    test('a video without black bars offers Detected and shows the whole picture', async () => {
        const { element, player, loader } = createHarness();
        player.setAspectRatio('cover');
        await player.play(playOptions(SCOPE_SOURCE_ID));
        loader.requests[0].resolve(0);
        await flush();

        player.setAspectRatio('detected');

        assert.equal(player.getAspectRatio(), 'detected');
        assert.deepEqual(cropStyles(element), UNCROPPED);
    });

    test('the next video starts uncropped, and a saved Detected shows an unmeasured one as Auto', async () => {
        const { element, player, loader, appSettings } = createHarness();
        await player.play(playOptions(SCOPE_SOURCE_ID));
        loader.requests[0].resolve(2.39);
        await flush();
        player.setAspectRatio('detected');

        await player.play(playOptions(FLAT_SOURCE_ID));
        assert.deepEqual(cropStyles(element), UNCROPPED);
        loader.requests[1].resolve(null);
        await flush();

        assert.equal(appSettings.aspectRatio(), 'detected');
        assert.equal(player.getAspectRatio(), 'auto');
        assert.deepEqual(player.getSupportedAspectRatios().map(({ id }) => id), ['auto', 'cover', 'fill']);
        assert.deepEqual(cropStyles(element), UNCROPPED);
    });

    test('an answer for an earlier video is dropped', async () => {
        const { element, player, loader } = createHarness();
        await player.play(playOptions(SCOPE_SOURCE_ID));
        await player.play(playOptions(FLAT_SOURCE_ID));

        loader.requests[0].resolve(2.39);
        await flush();
        assert.equal(player.getAspectRatio(), 'auto');
        assert.deepEqual(cropStyles(element), UNCROPPED);

        loader.requests[1].resolve(1.85);
        await flush();
        assert.equal(cropStyles(element).width, 'calc(100vh * 1.85)');
    });

    test('a stream change keeps the crop without asking again', async () => {
        const { element, player, loader } = createHarness();
        await player.play(playOptions(SCOPE_SOURCE_ID));
        loader.requests[0].resolve(2.39);
        await flush();

        const replay = player.play(playOptions(SCOPE_SOURCE_ID, { resetSubtitleOffset: false }));
        assert.equal(cropStyles(element).objectFit, 'cover');
        await replay;

        assert.equal(loader.requests.length, 1);
        assert.equal(player.getAspectRatio(), 'detected');
        assert.equal(cropStyles(element).width, 'calc(100vh * 2.39)');
        assert.equal(cropStyles(element).objectFit, 'cover');
    });

    test('theme videos and other sources are never cropped', async () => {
        const { element, player, loader } = createHarness();

        await player.play(playOptions(SCOPE_SOURCE_ID, { aspectRatio: 'cover', fullscreen: false }));
        await player.play({ item: { Id: 'channel' }, mediaSource: { Id: 'live-stream-1' } });
        await player.play({ item: { Id: 'url' }, url: 'https://example.invalid/video.mp4' });

        assert.equal(loader.requests.length, 0);
        assert.equal(player.getAspectRatio(), 'auto');
        assert.equal(cropStyles(element).width, '');
    });

    test('a stop drops the crop and any late answer', async () => {
        const { player, events, loader } = createHarness();
        await player.play(playOptions(SCOPE_SOURCE_ID));

        events.trigger(player, 'stopped', [{ src: 'stream' }]);
        loader.requests[0].resolve(2.39);
        await flush();

        assert.equal(player.getAspectRatio(), 'auto');
    });

    test('the crop applies once the element exists, even when the answer arrived first', async () => {
        let createElement;
        const { element, player, events, loader } = createHarness(new Promise((resolve) => {
            createElement = resolve;
        }));
        const playback = player.play(playOptions(SCOPE_SOURCE_ID));
        loader.requests[0].resolve(2.39);
        await flush();
        assert.equal(cropStyles(element).width, '');

        createElement();
        await playback;
        events.trigger(player, 'playing');

        assert.equal(cropStyles(element).width, 'calc(100vh * 2.39)');
        assert.equal(cropStyles(element).objectFit, 'cover');
    });

    test('a player that does not hand out its element is cropped through the element finder', async () => {
        const element = { style: createStyle() };
        const player = new DelegatingVideoPlayer(createAppSettings(), element);
        const events = createEvents();
        const loader = createLoader();
        patchVideoPlayer(player, loader.load, events, () => element);

        await player.play(playOptions(SCOPE_SOURCE_ID));
        loader.requests[0].resolve(2.39);
        await flush();

        assert.equal('createMediaElement' in player, false);
        assert.equal(player.getAspectRatio(), 'detected');
        assert.deepEqual(player.getSupportedAspectRatios().map(({ id }) => id), ['auto', 'detected', 'cover', 'fill']);
        assert.equal(cropStyles(element).width, 'calc(100vh * 2.39)');
        assert.equal(cropStyles(element).objectFit, 'cover');

        player.setAspectRatio('fill');
        assert.deepEqual(cropStyles(element), { ...UNCROPPED, objectFit: 'fill' });
    });

    test('the element finder finds nothing outside a browser', () => {
        assert.equal(findVideoElement(), null);
    });

    test('a failing measurement never breaks playback', async () => {
        const element = { style: createStyle() };
        const player = new StockVideoPlayer(createAppSettings(), element);
        const consoleError = mock.method(console, 'error', () => {});
        try {
            patchVideoPlayer(player, () => {
                throw new Error('offline');
            }, createEvents());

            assert.equal(await player.play(playOptions(SCOPE_SOURCE_ID)), element);
            assert.equal(player.getAspectRatio(), 'auto');
            assert.equal(consoleError.mock.callCount(), 1);
        } finally {
            consoleError.mock.restore();
        }
    });
});

describe('createCropAspectRatioLoader', () => {
    function createApiClient(answer) {
        const urls = [];
        return {
            urls,
            getUrl: (path) => `https://jellyfin.invalid/${path}`,
            getJSON(url) {
                urls.push(url);
                return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer);
            }
        };
    }

    test('asks the server of the item for its black bars', async () => {
        const apiClient = createApiClient({ IsMeasured: true, AspectRatio: 2.388, SnappedAspectRatio: 2.39 });
        const serverIds = [];
        const load = createCropAspectRatioLoader({
            getApiClient(serverId) {
                serverIds.push(serverId);
                return apiClient;
            },
            currentApiClient: () => assert.fail('the item names its server')
        });

        assert.equal(await load({ ServerId: 'server' }, SCOPE_SOURCE_ID), 2.39);
        assert.deepEqual(serverIds, ['server']);
        assert.deepEqual(apiClient.urls, [`https://jellyfin.invalid/DetectAspectRatio/Items/${SCOPE_SOURCE_ID}`]);
    });

    test('uses the current server for an item without one', async () => {
        const apiClient = createApiClient({ IsMeasured: true, SnappedAspectRatio: 0 });
        const load = createCropAspectRatioLoader({ currentApiClient: () => apiClient });

        assert.equal(await load({}, SCOPE_SOURCE_ID), 0);
    });

    test('resolves to null when the server or the request fails', async () => {
        const consoleWarn = mock.method(console, 'warn', () => {});
        const consoleError = mock.method(console, 'error', () => {});
        try {
            assert.equal(await createCropAspectRatioLoader({ currentApiClient: () => createApiClient(new Error('404')) })({}, SCOPE_SOURCE_ID), null);
            assert.equal(await createCropAspectRatioLoader({ currentApiClient: () => undefined })({}, SCOPE_SOURCE_ID), null);
            assert.equal(await createCropAspectRatioLoader({
                getApiClient: () => {
                    throw new Error('item or serverId cannot be null');
                }
            })({ ServerId: 'gone' }, SCOPE_SOURCE_ID), null);
        } finally {
            consoleWarn.mock.restore();
            consoleError.mock.restore();
        }
    });
});

describe('whenVideoPlayersRegistered', () => {
    const POLL_INTERVAL_MS = 100;

    // A tick fires only the timers due when it starts, so each poll needs its own
    function poll(times) {
        for (let index = 0; index < times; index++) {
            mock.timers.tick(POLL_INTERVAL_MS);
        }
    }

    beforeEach(() => {
        mock.timers.enable({ apis: ['setTimeout'] });
    });

    afterEach(() => {
        mock.timers.reset();
    });

    test('calls back with each video player once it registers', () => {
        const players = [{ id: 'photoplayer' }];
        const found = [];
        whenVideoPlayersRegistered({ getPlayers: () => players }, (player) => found.push(player.id));

        poll(3);
        assert.deepEqual(found, []);
        players.push({ id: 'htmlvideoplayer' });
        poll(2);
        assert.deepEqual(found, ['htmlvideoplayer']);
        players.push({ id: 'webgpuplayer' });
        poll(10);

        assert.deepEqual(found, ['htmlvideoplayer', 'webgpuplayer']);
    });

    test('stops once both video players are found', () => {
        let checks = 0;
        whenVideoPlayersRegistered({
            getPlayers: () => {
                checks++;
                return [{ id: 'webgpuplayer' }, { id: 'htmlvideoplayer' }];
            }
        }, () => {});

        poll(10);

        assert.equal(checks, 1);
    });

    test('gives up after its attempts, and warns only when no video player registered', () => {
        const consoleWarn = mock.method(console, 'warn', () => {});
        try {
            let checks = 0;
            whenVideoPlayersRegistered({
                getPlayers: () => {
                    checks++;
                    return [];
                }
            }, () => assert.fail('no player registered'), 3);
            poll(10);
            assert.equal(checks, 3);
            assert.equal(consoleWarn.mock.callCount(), 1);

            // The WebGPU player is a separate plugin, so the HTML video player alone is a complete setup
            const found = [];
            whenVideoPlayersRegistered({ getPlayers: () => [{ id: 'htmlvideoplayer' }] }, (player) => found.push(player.id), 3);
            poll(10);
            assert.deepEqual(found, ['htmlvideoplayer']);
            assert.equal(consoleWarn.mock.callCount(), 1);
        } finally {
            consoleWarn.mock.restore();
        }
    });
});

describe('DetectAspectRatio', () => {
    test('patches the registered video players and registers as a plugin of no type', () => {
        const stockPlayer = new StockVideoPlayer(createAppSettings(), { style: createStyle() });
        const webGPUPlayer = new DelegatingVideoPlayer(createAppSettings(), { style: createStyle() });
        const stockAspectRatios = stockPlayer.getSupportedAspectRatios;
        const webGPUAspectRatios = webGPUPlayer.getSupportedAspectRatios;

        const plugin = new DetectAspectRatio({
            events: createEvents(),
            playbackManager: { getPlayers: () => [webGPUPlayer, stockPlayer] },
            ServerConnections: {}
        });

        assert.equal(plugin.id, 'detectaspectratio');
        assert.equal(plugin.name, 'Detect Aspect Ratio');
        assert.equal(plugin.type, undefined);
        assert.notEqual(stockPlayer.getSupportedAspectRatios, stockAspectRatios);
        assert.notEqual(webGPUPlayer.getSupportedAspectRatios, webGPUAspectRatios);
    });

    test('never throws into the plugin manager', () => {
        const consoleError = mock.method(console, 'error', () => {});
        try {
            const plugin = new DetectAspectRatio({});

            assert.equal(plugin.id, 'detectaspectratio');
            assert.equal(consoleError.mock.callCount(), 1);
        } finally {
            consoleError.mock.restore();
        }
    });
});
