---
version: 1
slug: "assets-scripts-ui-game-containerinventoryview-cs"
primary_target: "Assets/Scripts/UI/Game/ContainerInventoryView.cs"
related_targets: ["Assets/Resources/Moonkov/UI/RaidContainerInventory.uxml","Assets/Resources/Moonkov/UI/RaidInventory.uss","Assets/Resources/Moonkov/RaidUI.uxml","Assets/Resources/Moonkov/UI/QuickSlots.uss","Assets/Scripts/UI/Game/ContainerInventoryView.Raid.cs"]
---

# Inventory surfaces

Scope: native Unity UI Toolkit raid inventory, looting and warehouse. Mode: Operate. User direction is explicitly pinned to the supplied Tarkov reference; code-led, no generated design comps.

## Direction contract

THESIS: Keep equipment, personal containers and searched ownership visually separate, and make empty drop targets discoverable.
OWN-WORLD: Dark charcoal and muted olive, source CJK font, existing vector item art and a baked static production-character portrait.
STORY: Players inspect their loadout, place equipment on visible body slots, organize carried storage, then transfer from the right-hand target with authoritative commands.
FIRST VIEWPORT: Joined left portrait/equipment and middle personal storage align vertically; right scrollable loot or warehouse owns the remaining width. Six body targets overlay the static full-body production character. Personal storage is ordered chest rig, pockets, three special tool slots, backpack, secure case. Container equipment uses a compact icon entry beside the content grid. Storage grids proportionally fill the actual middle width; three tool targets remain compact 52×52 squares. Raid quickbar docks below.
FORM: User-pinned Tarkov inventory canon, native UXML/USS/C#; no random concept or approved generated comp.
FINISH: The finish reviewer returned ship for the five native inventory fixtures below. The final built system is recorded in token-bearing DESIGN.md and schemaVersion 2 .impeccable/design.json. FieldOperatorPortrait.png carries baked production-avatar provenance; no external ImageGen asset or runtime portrait camera is involved.

Validation uses native editor panel captures and real inventory command callbacks. No claim of physical mouse, formal-map Play or multiplayer acceptance. Secure container/tool persistence is checked in isolated local profiles, with online backend compilation only.

## Built surface contract

- One compact header row contains equipment/health tabs and close. Repeated headline, pane instructions, persistent tutorial captions and large container cards stay hidden.
- Own equipment has twelve targets: six body slots, chest rig/backpack/secure case entries in personal storage, and three compact special slots. Empty container entries remain visible.
- Own storage scales square cells to the width of each middle-column content row while preserving true capacity, item dimensions and rotation. Tool slots are a fixed-size exception following the user's final correction.
- Left and middle stay joined; the right target scrolls independently. The raid quickbar reserves a 62-unit dock. Warehouse uses more width for its right-hand stash and does not show the raid dock.
- Corpse loot currently shows opponent equipment as compact rows/cards, followed by chest rig/backpack contents and loose items. These reviewed fixtures do not contain an opponent portrait. The later request for an opponent's full-body portrait and overlaid equipment is a separate pending feature.
- Interactions retain authoritative inventory commands, drag preview, R rotation, right-click/Esc cancellation and modifier-click transfer. Failed operations retain the original item and explain the reason; the UI does not simulate transaction success.

## Reviewed evidence and limits

The reviewed files are native Editor panel fixtures using production UXML and presenters, not formal gameplay captures:

- `.impeccable/review/warehouse-1600.png`: own equipment/containers and populated warehouse.
- `.impeccable/review/corpse-1600.png`: own inventory and corpse equipment/contents.
- `.impeccable/review/secure-1600.png`: scrolled secure case and no-target state.
- `.impeccable/review/cache-1280.png`: populated ordinary loot at the smaller viewport.
- `.impeccable/review/empty-1280.png`: empty body equipment, container entries, tools and no-target state.

The 1600×900 fixtures are stored at 2400×1350 pixels (1.5 DPI scale); the 1280×720 fixtures are stored at 1920×1080 pixels. All five were visually inspected. They show the earlier expanded special-slot row; the final 52×52 correction is confirmed in current USS with its former width-fitting callback removed. They remain evidence for the rest of the reviewed composition, not a new visual acceptance of the corrected tool sizing.

The implementation owner reports 70 passing isolated persistence assertions and backend compilation. These checks cover local secure/tool persistence rules and compile compatibility only. They do not establish physical input, formal-map Play, functional compass/rangefinder readouts, PostgreSQL online persistence, multiplayer or target-device performance acceptance.

Runtime visual sources: `RaidInventory.uss`, `RaidContainerInventory.uxml`, `RaidUI.uxml`, `QuickSlots.uss`, `ContainerInventoryView.cs` and `ContainerInventoryView.Raid.cs`. The design sidecar's HTML/CSS snippets are documentation translations of native primitives, not replacement runtime UI.
