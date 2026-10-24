---
version: 1
slug: "assets-resources-moonkov-ui-raidmapview-uxml"
primary_target: "Assets/Resources/Moonkov/UI/RaidMapView.uxml"
related_targets: ["Assets/Resources/Moonkov/UI/RaidMapView.uss","Assets/Scripts/Gameplay/UI/RaidMapView.cs","Assets/Scripts/Gameplay/UI/BountyClashMapView.cs","Assets/Scripts/Gameplay/UI/RaidHUD.Map.cs","Assets/Resources/Moonkov/Maps/TheophilusSatellite.png"]
---

# Tactical satellite map

Mode: Operate. Native Unity desktop UI Toolkit. The user rejected the earlier grey-green presentation as too AI-like, chose a Google/高德 satellite-map direction, then explicitly requested reuse of a real project's frontend source. The current controls adapt actual MapLibre GL JS CSS and SVG assets.

## Direction contract

THESIS: Natural satellite imagery is the main interface: recognize terrain and buildings, locate yourself, and choose a destination through sparse labels and small familiar map controls.
OWN-WORLD: The user's final choice is a dark theme: natural scene RGB, charcoal (#202124) floating controls, light (#e8eaed) text/SVGs, blue focus and location dot/heading cone, and a red personal pin. Existing CJK localization and authorization boundaries remain. Global DESIGN.md, PRODUCT.md and `.impeccable/design.json` are not replaced.
FIRST VIEWPORT: Edge-to-edge map; small place/info control at top-left, close at top-right, icon navigation at bottom-right and an optional supply-layer control at bottom-left. No main sidebar, header or footer. Details are closed by default at every width. First open centers the player at 3x; full map remains available.
SIGNATURE: Actual 4096px north-up orthographic scene imagery, sharing one XZ projection with markers and navigation. Natural RGB replaces the earlier grayscale histogram treatment. No illustrative substitute or runtime capture camera.
INFORMATION: Sparse place/extraction names by default. Supply markers start off; the supply-layer toggle controls actual markers. Optional details retain extraction readouts, cache counts/nearest, owned coordinates/heading, cache hover and authorized bounty/team data. Rival carriers remain coarse-only.
QUALITY BAR: Terrain and buildings remain recognizable at 1280×720 and 800×600; small controls remain legible and usable; optional information does not dominate the imagery. Google/高德 is a direction reference, not an imported map source or a claim of a matching implementation.
VALIDATION: All four current C# sources compiled and loaded in the existing Editor; Unity CLI fixture captures succeeded with empty diagnostics, and earlier isolated Roslyn compilation passed. `.impeccable/review/maplibre-800-map.png` (800×450 logical; 1600×900 image) and `maplibre-600-details.png` (600×450 logical; 1200×900 image, details and supplies enabled) were directly inspected and independently reviewed as SHIP, with no blocking clipping, text or control overlap. The open info panel can obscure part of the scale line, a minor optional refinement. Native callbacks toggled details/supplies; these fixtures do not establish physical-input, Play, network or performance acceptance, nor the untested 1280×720/800×600 layouts.

## Source provenance — current revision

MapLibre GL JS is pinned to [`3ea36bb61fd93ed9f67e38091bdd8f12948b6cf1`](https://github.com/maplibre/maplibre-gl-js/tree/3ea36bb61fd93ed9f67e38091bdd8f12948b6cf1). Control geometry, separators and interaction states from `src/css/maplibre-gl.css` are adapted into native USS with the user's dark palette. Four SVGs (zoom-in, zoom-out, geolocate and fullscreen) are installed under `Assets/Resources/Moonkov/UI/MapControls`; geolocate omits the disabled-state stroke path to match its enabled state, and all four use light fills while retaining upstream path geometry. SVG padding compensates Unity's native trimming. Upstream CSS, BSD-3-Clause license and adaptation notes are retained in `ThirdPartyNotices/MapLibre`; the full license is also bundled at `Assets/StreamingAssets/ThirdPartyNotices/MapLibre.txt`. This ports frontend control assets; no web map engine, webview or external imagery is introduced.

Natural RGB imagery was rebuilt through `BakeScene` / `OpenPreviewScene` while the active scene remained MainMenu. The temporary review helper has been removed from Assets after Editor crashes; its original source is retained locally under ignored Library/BuildValidation/MapUiCrashEvidence. A root source-font scope avoids the previous SDF artifact.

## 2026-10-24 earlier implementation record — rejected and superseded

The following records the earlier grey-green/grayscale iteration and its evidence only. The user rejected its visual treatment; its layout, palette and acceptance statements do not define the current contract above.

- The formal scene was orthographically rendered into `Assets/Resources/Moonkov/Maps/TheophilusSatellite.png` (4096×4096), with a 0.5–99.5 percentile grayscale stretch. +Z is north; JSON bounds and embedded source metadata identify the scene. Runtime loads this static texture and shares the marker XZ projection; a grid remains only the unavailable-imagery fallback.
- Extraction readouts, cache category/available counts, nearest cache, owned coordinates/heading, cache hover details and one personal pin extend the existing map. The static MC00 landmark uses the actual scene root transform recorded in the bounds JSON.
- Labels clamp their final rectangles after placement offsets, including the MC00 landmark, extraction, player, nearest-cache and personal-pin labels. That final adjustment has no subsequent visual capture yet.
- The user required a real scene thumbnail and inspection of real satellite references, rejecting the earlier no-image grid presentation. The [LROC QuickMap article](https://www.lroc.im-ldi.com/news/1167) and [Theophilus WAC image](https://lroc.sese.asu.edu/news/uploads/LROCiotw/Theophilus_WAC.png) informed the aesthetic only; neither is imported game imagery.
- This record documents a local extension of the incumbent design system. Global `DESIGN.md`, `PRODUCT.md` and `.impeccable/design.json` remain outside this extension.

STABILITY LIMIT: Three subsequent Editor crash logs contain D3D12 fence/command-list failures. Manual reflection-based Panel.Repaint/Render capture is disabled, including LanRoomPreview.CaptureOpen. Earlier visual SHIP findings do not establish Editor stability. No capture, bake, restart or crash reproduction was performed after this safety change; the native crash root cause remains unproven.
