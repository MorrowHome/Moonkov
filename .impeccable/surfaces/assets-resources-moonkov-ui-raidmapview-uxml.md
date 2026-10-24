---
version: 1
slug: "assets-resources-moonkov-ui-raidmapview-uxml"
primary_target: "Assets/Resources/Moonkov/UI/RaidMapView.uxml"
related_targets: ["Assets/Resources/Moonkov/UI/RaidMapView.uss","Assets/Scripts/Gameplay/UI/RaidMapView.cs","Assets/Scripts/Gameplay/UI/BountyClashMapView.cs","Assets/Scripts/Gameplay/UI/RaidHUD.Map.cs","Assets/Resources/Moonkov/Maps/TheophilusSatellite.png"]
---

# Tactical satellite map

Mode: Operate. Native Unity desktop UI Toolkit; code-led, no generated design comp. The user rejected a grid-only schematic and explicitly chose low-saturation satellite imagery with a compact grey-green toolbar.

## Direction contract

THESIS: Recognize actual terrain and buildings, locate yourself, and choose a supply or extraction destination without the information panel displacing the map.
OWN-WORLD: The project's charcoal/grey-green equipment UI, restrained olive selection, warm-white text, existing CJK localization. This local map extension does not replace inventory DESIGN.md or PRODUCT.md.
FIRST VIEWPORT: The map owns the main height and width. A compact single-row header and toolbar bound it; a 252-unit information sidebar is a plain VisualElement, not a ScrollView, and is collapsible and hidden initially below 1000 units. First open centers the player at 2x; full map remains available.
SIGNATURE: Actual 4096px north-up orthographic imagery of the formal scene, sharing one world-space projection with markers, scale and personal navigation pin. No illustrative substitute or runtime capture camera.
INFORMATION: Extraction distance/radius/hold; nearest available cache and category counts; owned coordinates/heading; hover cache identity; authorized bounty/team data. Rival carriers remain coarse-only.
QUALITY BAR: Terrain and buildings remain recognizable; map dominates at 1280×720 and 800×600; readable CJK controls; sidebar never compresses small-screen map by default; no blue decorative window chrome, metric-card wall or overlapping essential labels.
VALIDATION: Both map scripts passed targeted Unity Roslyn compilation after the final label-clamp change (exit 0, no C# source diagnostics; compiler configuration warning CS2023 only). The baker also compiled in isolation. Real scene satellite texture baked and visually inspected. The 1280×720 native preview at `.impeccable/review/tactical-map-1280.png` uses real scene data plus an illustrative player pose; an independent review confirmed visible terrain/buildings and the chosen direction. It predates the final label-clamp change. An 800×600 capture remains unavailable while the shared Editor recompiles unrelated parallel changes. No compact-layout visual, post-clamp screenshot, whole-project compile, Play, physical input, multiplayer or performance acceptance is claimed.

## 2026-10-24 implementation record

- The formal scene was orthographically rendered into `Assets/Resources/Moonkov/Maps/TheophilusSatellite.png` (4096×4096), with a 0.5–99.5 percentile grayscale stretch. +Z is north; JSON bounds and embedded source metadata identify the scene. Runtime loads this static texture and shares the marker XZ projection; a grid remains only the unavailable-imagery fallback.
- Extraction readouts, cache category/available counts, nearest cache, owned coordinates/heading, cache hover details and one personal pin extend the existing map. The static MC00 landmark uses the actual scene root transform recorded in the bounds JSON.
- Labels clamp their final rectangles after placement offsets, including the MC00 landmark, extraction, player, nearest-cache and personal-pin labels. That final adjustment has no subsequent visual capture yet.
- The user required a real scene thumbnail and inspection of real satellite references, rejecting the earlier no-image grid presentation. The [LROC QuickMap article](https://www.lroc.im-ldi.com/news/1167) and [Theophilus WAC image](https://lroc.sese.asu.edu/news/uploads/LROCiotw/Theophilus_WAC.png) informed the aesthetic only; neither is imported game imagery.
- This record documents a local extension of the incumbent design system. Global `DESIGN.md`, `PRODUCT.md` and `.impeccable/design.json` remain outside this extension.
