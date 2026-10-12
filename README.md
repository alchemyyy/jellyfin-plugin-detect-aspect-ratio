<h1 align="center">
  Jellyfin Plugin - Detect Aspect Ratio<br>
  <sub><sup><sup>Solving the issue of double-letterboxing (AKA black bars) by detecting them in videos from their Trickplay images.</sup></sup></sub>
</h1>

A plugin form of my PR here: [jellyfin/jellyfin#16399](https://github.com/jellyfin/jellyfin/pull/16399), and its web client counterpart [jellyfin/jellyfin-web#7671](https://github.com/jellyfin/jellyfin-web/pull/7671).

https://github.com/user-attachments/assets/dcab3e41-35b4-4e13-aa41-e321aaf14631

## How

Many video files carry black bars inside the frame: a 2.39:1 film in a 16:9 frame is letterboxed, 4:3 content in a 16:9 frame is pillarboxed, and some files are both.
Whenever the screen's shape differs from the frame's, the player adds bars of its own around the encoded ones, and the picture ends up boxed in on every side.

None of Jellyfin Web's own aspect ratios know where the picture is:

- **Auto** fits the whole frame, encoded bars included.
- **Zoom** crops the frame to the shape of the screen rather than to the picture. It removes the encoded bars only when the screen happens to match the picture; otherwise it leaves part of the bars, cuts into the picture, or both.
- **Stretch** distorts the picture.

**Detected** crops the frame to the picture inside its bars, then fits that picture to the window, for letterboxes, pillarboxes, and both at once.

## Requirements

- Jellyfin 12.1.0 or newer.
- Trickplay images. Turn on **Enable trickplay image extraction** in each library's settings, and let the **Generate Trickplay Images** scheduled task run. Videos without trickplay images are not measured and play as they always did.
- Jellyfin Web, in a browser that can import JavaScript modules.
- Optional: [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation). When it is installed, the plugin edits Jellyfin Web through it; otherwise the plugin makes the same edits itself.

## Installation

1. In **Dashboard > Plugins**, add this plugin repository:

   ```text
   https://raw.githubusercontent.com/alchemyyy/jellyfin-plugin-detect-aspect-ratio/main/manifest.json
   ```

2. Install **Detect Aspect Ratio** from the catalog, then restart the server.
3. Reload Jellyfin Web.

The plugin has no settings.
After the restart it measures every video that already has trickplay images, as described in [When videos are measured](#when-videos-are-measured).

To install by hand, download the zip from [Releases](https://github.com/alchemyyy/jellyfin-plugin-detect-aspect-ratio/releases), extract it into a new folder in the server's `plugins` folder, and restart the server.

To uninstall, remove the plugin in **Dashboard > Plugins**, restart the server, and reload Jellyfin Web.
The measurements stay in the server's data folder until you delete them; see [When videos are measured](#when-videos-are-measured).

## How it works

The main feature of this plugin is it doesn't need to perform a complete its own high-resolution snapshotting of a video file. 
Trickplay is an extremely popular feature and already in official Jellyfin, and we can get away with figuring out an aspect ratio even from those tiny compressed thumbnails, with a bit of cheating.
The trick is that there are only so many aspect ratios for films / broadcast media. We can make up the precision loss from the low resolution by snapping our results to one of these known aspect ratios.

1. A pixel is lit when its BT.601 luma is above 15 of 255, which tolerates JPEG noise in black areas.
2. For each pixel position of the thumbnail grid, the plugin counts the thumbnails in which it is lit.
3. A position is picture when it is lit in at least 5% of the thumbnails, so brief changes such as opening logos and IMAX sequences do not count.
4. A row or column is picture when at least 25% of its positions are, because JPEG artifacts light single pixels inside thin bars.
5. The span of picture rows and columns is the picture. When it covers more than 98% of the thumbnail in both directions, the video has no black bars.
6. The picture's width to height ratio, rounded to 3 decimals, snaps to the nearest [standard aspect ratio](#standard-aspect-ratios) within 0.075, such as 2.39 "Modern Anamorphic Scope". Jellyfin Web crops to the snapped ratio only.

This algorithm behaves conservatively. It prefers to leave a sliver of black bar as opposed to cropping any part of the actual video frame.
The thresholds live in [BlackBarAccumulator.cs](Jellyfin.Plugin.DetectAspectRatio/Analysis/BlackBarAccumulator.cs) and [AspectRatioFunctions.cs](Jellyfin.Plugin.DetectAspectRatio/Analysis/AspectRatioFunctions.cs).

### When videos are measured

The scheduled task **Compute Aspect Ratios from Trickplay Data**, in the Library category of **Dashboard > Scheduled Tasks**, measures every video that has trickplay images, so playback never waits for a measurement. It runs:

- once on its own, on a server where it never ran, such as after the first install;
- daily at 4 AM, an hour after Jellyfin's default time for Generate Trickplay Images;
- after every run of Generate Trickplay Images.

A video the task has not reached yet, such as one whose trickplay images a library scan just extracted, is measured the first time Jellyfin Web asks for it.
A measurement is kept until the video's trickplay images change, the video file changes, or a plugin update changes the measuring method; then the video is measured again.
The task also deletes the measurements of videos that no longer have trickplay images.

Measurements are JSON files named by item id, in `detect-aspect-ratio` inside the server's data folder, the folder that holds `jellyfin.db`.

### Playback

The plugin serves a small add-on module to Jellyfin Web.
When a video starts, the add-on asks the plugin's [API](#api) for its measurement and adds Detected to the player's aspect ratio menu.
With Detected, it sizes the video element to the snapped ratio fitted to the window, and crops the frame to that element with `object-fit: cover`, so the encoded bars fall outside it.

## API

```http
GET /DetectAspectRatio/Items/{itemId}
```

```json
{
    "ItemId": "8c4b2f0e6a1d4e3f9b7a5c2d1e0f3a4b",
    "IsMeasured": true,
    "AspectRatio": 2.388,
    "SnappedAspectRatio": 2.39,
    "SnappedAspectRatioName": "Modern Anamorphic Scope"
}
```

| Field | Meaning |
| --- | --- |
| `IsMeasured` | `false` when the video has no readable trickplay images |
| `AspectRatio` | The measured width to height ratio of the picture inside the bars, `0` when the video has none |
| `SnappedAspectRatio` | The standard ratio nearest the measurement, which Jellyfin Web crops to, `0` when none is within 0.075 |
| `SnappedAspectRatioName` | The name of the snapped ratio, `null` when there is none |

## Limitations

- A video gets one ratio. Picture that shows in fewer than 5% of the thumbnails, such as a short IMAX sequence, is cropped while it plays; when it shows more often, it becomes part of the measured picture, and the rest of the video keeps part of its bars.
- The crop is centered, so bars of unequal size may leave a sliver of bar on one side and cut a sliver of picture on the other.
- Bars must be black. Gray or colored mattes are picture.
- A measured ratio further than 0.075 from every standard ratio is not cropped.
- Apps that play video with their own player instead of Jellyfin Web's are unaffected; they can read the [API](#api).
- The Detected label is English only, because plugins cannot add strings to Jellyfin Web's translations.

## Troubleshooting

- **Detected is not in the menu.** The video has no measurement. Query the [API](#api) for it: `IsMeasured: false` means it has no readable trickplay images, so run Generate Trickplay Images, then Compute Aspect Ratios from Trickplay Data.
- **Detected is in the menu, but nothing is cropped.** The video has no black bars, or its ratio is near no standard ratio; `SnappedAspectRatio` is `0` either way.
- **Nothing changes at all.** Reload Jellyfin Web and check the browser console for `Detect Aspect Ratio` or `[DetectAspectRatio]` messages. A reverse proxy's Content-Security-Policy must allow inline scripts, as Jellyfin's documented policy does, and a customized `config.json` must keep its `plugins` list.
- **Server log.** The plugin's messages mention `Detect Aspect Ratio`, including one per measured video, such as `Detect Aspect Ratio measured <video> at 2.388:1 inside its black bars (<n> ms)`.
