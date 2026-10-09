// Detect Aspect Ratio client add-on. The server plugin's bootstrap loads it through Jellyfin Web's window plugin path.
// It adds a Detected aspect ratio to the local video players, which crops the black bars the server measured from trickplay images.
// NOTE: ES2017 syntax only, because every browser that can import this module must also parse it, older TV browsers included

// The stock HTML video player, and the WebGPU Player plugin's player, whose presenter follows its video element's box and object-fit
const VIDEO_PLAYER_IDS = ['htmlvideoplayer', 'webgpuplayer'];
// Both players play into this element; the stock one also hands it out through createMediaElement
const VIDEO_ELEMENT_SELECTOR = '.videoPlayerContainer > video.htmlvideoplayer';
const AUTO_ASPECT_RATIO = 'auto';
const DETECTED_ASPECT_RATIO = 'detected';
// Jellyfin Web has no string for it, and plugins outside its bundle cannot add translations
const DETECTED_ASPECT_RATIO_NAME = 'Detected';
const BLACK_BARS_PATH = 'DetectAspectRatio/Items/';
// Library media sources are item ids; anything else, such as a live stream, has no trickplay images
const ITEM_ID_PATTERN = /^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i;
const LOG_PREFIX = '[DetectAspectRatio]';
// Jellyfin Web registers its players while it loads plugins, all of which finish before its first render
const PLAYER_POLL_INTERVAL_MS = 100;
const PLAYER_POLL_ATTEMPTS = 600;

/**
 * Resolves the aspect ratio the player applies to the current video.
 * @param {string} savedAspectRatio - The saved aspect ratio, empty when none was chosen.
 * @param {number | null} cropAspectRatio - The ratio to crop to, 0 for a video without black bars, or null for an unmeasured video.
 * @returns {string} The aspect ratio id.
 */
export function resolveAspectRatio(savedAspectRatio, cropAspectRatio) {
    const aspectRatio = savedAspectRatio || AUTO_ASPECT_RATIO;
    if (aspectRatio !== AUTO_ASPECT_RATIO && aspectRatio !== DETECTED_ASPECT_RATIO) {
        return aspectRatio;
    }

    // Auto crops every measured video, and a saved Detected shows an unmeasured one as Auto does
    return cropAspectRatio === null ? AUTO_ASPECT_RATIO : DETECTED_ASPECT_RATIO;
}

/**
 * Offers Detected right after Auto for a measured video.
 * @param {Array<{name: string, id: string}>} aspectRatios - The player's own aspect ratios.
 * @param {number | null} cropAspectRatio - The ratio to crop to, 0 for a video without black bars, or null for an unmeasured video.
 * @returns {Array<{name: string, id: string}>} The aspect ratios to offer.
 */
export function addDetectedAspectRatio(aspectRatios, cropAspectRatio) {
    if (cropAspectRatio === null || aspectRatios.some((aspectRatio) => aspectRatio.id === DETECTED_ASPECT_RATIO)) {
        return aspectRatios;
    }

    const detected = { name: DETECTED_ASPECT_RATIO_NAME, id: DETECTED_ASPECT_RATIO };
    const autoIndex = aspectRatios.findIndex((aspectRatio) => aspectRatio.id === AUTO_ASPECT_RATIO);
    return [...aspectRatios.slice(0, autoIndex + 1), detected, ...aspectRatios.slice(autoIndex + 1)];
}

/**
 * Reads the ratio to crop to from the plugin API's black bars of a video.
 * @param {object | null | undefined} blackBars - The black bars of the video.
 * @returns {number | null} The standard ratio to crop to, 0 when the video has no black bars or they match no standard ratio, or null for an unmeasured video.
 */
export function getCropAspectRatio(blackBars) {
    if (!blackBars || blackBars.IsMeasured !== true) {
        return null;
    }

    const snappedAspectRatio = Number(blackBars.SnappedAspectRatio);
    return Number.isFinite(snappedAspectRatio) && snappedAspectRatio > 0 ? snappedAspectRatio : 0;
}

/**
 * Sizes the video element to the picture inside the black bars, fitted to the viewport, so object-fit cover crops exactly the bars.
 * Any other aspect ratio gets the player's own sizing back.
 * @param {HTMLVideoElement} element - The player's video element.
 * @param {string} aspectRatio - The resolved aspect ratio.
 * @param {number | null} cropAspectRatio - The ratio to crop to, 0 for a video without black bars, or null for an unmeasured video.
 */
export function applyCrop(element, aspectRatio, cropAspectRatio) {
    const style = element.style;
    if (aspectRatio === DETECTED_ASPECT_RATIO && cropAspectRatio > 0) {
        // Fitted size: min(100vw, 100vh * ratio) by min(100vh, 100vw / ratio), spelled with max sizes for browsers without CSS min()
        style.setProperty('width', `calc(100vh * ${cropAspectRatio})`);
        style.setProperty('max-width', '100vw');
        style.setProperty('height', `calc(100vw / ${cropAspectRatio})`);
        style.setProperty('max-height', '100vh');
        style.setProperty('object-fit', 'cover');
        // The player's stylesheet pins the margin with !important, and auto centers the element in its flex container
        style.setProperty('margin', 'auto', 'important');
        return;
    }

    style.removeProperty('width');
    style.removeProperty('max-width');
    style.removeProperty('height');
    style.removeProperty('max-height');
    style.removeProperty('margin');
    // Detected without black bars shows the whole picture, as Auto does; the player sets every other object-fit itself
    if (aspectRatio === DETECTED_ASPECT_RATIO) {
        style.removeProperty('object-fit');
    }
}

/**
 * Adds the Detected aspect ratio to a local video player. Only public methods are wrapped, because the players keep their
 * video element and their own aspect ratio code in private fields.
 * @param {object} player - The player.
 * @param {(item: object, mediaSourceId: string) => Promise<number | null>} loadCropAspectRatio - Loads the ratio to crop a video to.
 * @param {object} events - Jellyfin Web's event helper.
 * @param {() => HTMLVideoElement | null} [findElement] - Finds the video element of a player that does not hand it out.
 */
export function patchVideoPlayer(player, loadCropAspectRatio, events, findElement = findVideoElement) {
    // Crop ratios by media source, kept for the session; a stream change replays the same source
    const cropAspectRatios = new Map();
    const state = {
        element: null,
        cropAspectRatio: null,
        // Advanced by every play and stop, so an answer for an earlier video is dropped
        generation: 0
    };

    const play = player.play;
    const createMediaElement = player.createMediaElement;
    const getAspectRatio = player.getAspectRatio;
    const setAspectRatio = player.setAspectRatio;
    const getSupportedAspectRatios = player.getSupportedAspectRatios;

    const applyCurrentCrop = () => {
        const element = state.element || findElement();
        if (element) {
            applyCrop(element, player.getAspectRatio(), state.cropAspectRatio);
        }
    };

    const startVideo = (options) => {
        state.generation++;
        const mediaSourceId = getMediaSourceId(options);
        state.cropAspectRatio = mediaSourceId !== null && cropAspectRatios.has(mediaSourceId) ? cropAspectRatios.get(mediaSourceId) : null;
        // The player reuses its element, so the previous video's crop must go before this video shows
        applyCurrentCrop();
        if (mediaSourceId === null || cropAspectRatios.has(mediaSourceId)) {
            return;
        }

        const generation = state.generation;
        loadCropAspectRatio(options.item, mediaSourceId).then((cropAspectRatio) => {
            if (cropAspectRatio !== null) {
                cropAspectRatios.set(mediaSourceId, cropAspectRatio);
            }

            if (generation === state.generation) {
                state.cropAspectRatio = cropAspectRatio;
                guard(applyCurrentCrop);
            }
        });
    };

    const stopVideo = () => {
        state.generation++;
        state.cropAspectRatio = null;
    };

    player.play = function (options, ...rest) {
        guard(() => startVideo(options));
        return play.call(this, options, ...rest);
    };

    if (typeof createMediaElement === 'function') {
        player.createMediaElement = function (...args) {
            const result = createMediaElement.apply(this, args);
            Promise.resolve(result).then((element) => {
                state.element = element;
            }, () => {
                // The player's own play() receives the same failure and reports it
            });
            return result;
        };
    }

    player.getAspectRatio = function (...args) {
        return resolveAspectRatio(getAspectRatio.apply(this, args), state.cropAspectRatio);
    };

    player.setAspectRatio = function (...args) {
        const result = setAspectRatio.apply(this, args);
        guard(applyCurrentCrop);
        return result;
    };

    player.getSupportedAspectRatios = function (...args) {
        return addDetectedAspectRatio(getSupportedAspectRatios.apply(this, args), state.cropAspectRatio);
    };

    // The element exists by the time a video plays, even when the crop ratio arrived first
    events.on(player, 'playing', () => guard(applyCurrentCrop));
    events.on(player, 'stopped', () => guard(stopVideo));
}

/**
 * Creates the loader of crop ratios, which asks the plugin API; the server measures an unmeasured video on the spot.
 * @param {object} serverConnections - Jellyfin Web's server connections.
 * @returns {(item: object, mediaSourceId: string) => Promise<number | null>} The loader, which resolves to null on any failure.
 */
export function createCropAspectRatioLoader(serverConnections) {
    return (item, mediaSourceId) => {
        try {
            const apiClient = item && item.ServerId ? serverConnections.getApiClient(item.ServerId) : serverConnections.currentApiClient();
            if (!apiClient) {
                return Promise.resolve(null);
            }

            return apiClient.getJSON(apiClient.getUrl(BLACK_BARS_PATH + encodeURIComponent(mediaSourceId))).then(getCropAspectRatio, (error) => {
                console.warn(LOG_PREFIX, 'No black bars for media source', mediaSourceId, error);
                return null;
            });
        } catch (error) {
            console.error(LOG_PREFIX, error);
            return Promise.resolve(null);
        }
    };
}

/**
 * Calls back with each local video player once Jellyfin Web registers it. The WebGPU player is optional, so only a run
 * without any of them is reported.
 * @param {object} playbackManager - Jellyfin Web's playback manager.
 * @param {(player: object) => void} callback - Receives each player once.
 * @param {number} [attempts] - The checks left.
 * @param {Set<string>} [found] - The ids already called back.
 */
export function whenVideoPlayersRegistered(playbackManager, callback, attempts = PLAYER_POLL_ATTEMPTS, found = new Set()) {
    for (const player of playbackManager.getPlayers()) {
        if (VIDEO_PLAYER_IDS.includes(player.id) && !found.has(player.id)) {
            found.add(player.id);
            guard(() => callback(player));
        }
    }

    if (found.size === VIDEO_PLAYER_IDS.length) {
        return;
    }

    if (attempts <= 1) {
        if (found.size === 0) {
            console.warn(LOG_PREFIX, 'No video player registered, so no video is cropped');
        }
        return;
    }

    setTimeout(() => guard(() => whenVideoPlayersRegistered(playbackManager, callback, attempts - 1, found)), PLAYER_POLL_INTERVAL_MS);
}

/**
 * Finds the video element the local video players play into.
 * @returns {HTMLVideoElement | null} The element, or null when no video is shown.
 */
export function findVideoElement() {
    return typeof document === 'undefined' ? null : document.querySelector(VIDEO_ELEMENT_SELECTOR);
}

/**
 * Plugin constructor for Jellyfin Web's plugin manager, which passes its host modules in a bag. It registers as a plugin of no
 * type the host acts on.
 * @param {object} bag - The host modules.
 */
export default function DetectAspectRatio(bag) {
    this.name = 'Detect Aspect Ratio';
    this.id = 'detectaspectratio';
    guard(() => {
        const loadCropAspectRatio = createCropAspectRatioLoader(bag.ServerConnections);
        whenVideoPlayersRegistered(bag.playbackManager, (player) => patchVideoPlayer(player, loadCropAspectRatio, bag.events));
    });
}

/**
 * Returns the media source of a playback to measure.
 * @param {object | null | undefined} options - The player's play options.
 * @returns {string | null} The media source id, or null for a playback that is never cropped.
 */
function getMediaSourceId(options) {
    // Theme videos force their own aspect ratio
    if (!options || options.aspectRatio || !options.mediaSource) {
        return null;
    }

    const mediaSourceId = options.mediaSource.Id;
    return typeof mediaSourceId === 'string' && ITEM_ID_PATTERN.test(mediaSourceId) ? mediaSourceId : null;
}

/**
 * Runs add-on code that must never break the host: the player's methods and event handlers call into it.
 * @param {() => void} action - The code to run.
 */
function guard(action) {
    try {
        action();
    } catch (error) {
        console.error(LOG_PREFIX, error);
    }
}
