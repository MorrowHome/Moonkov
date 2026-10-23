---
version: 1
slug: "assets-scripts-ui-game-containerinventoryview-cs"
primary_target: "Assets/Scripts/UI/Game/ContainerInventoryView.cs"
related_targets: ["Assets/Resources/Moonkov/UI/RaidContainerInventory.uxml","Assets/Resources/Moonkov/UI/RaidInventory.uss","Assets/Resources/Moonkov/RaidUI.uxml","Assets/Resources/Moonkov/UI/QuickSlots.uss","Assets/Scripts/UI/Game/ContainerInventoryView.Raid.cs","Assets/Resources/Moonkov/UI/Components/CorpsePortrait.uxml"]
---

# Inventory surfaces

Scope: native Unity UI Toolkit raid inventory, looting and warehouse. Mode: Operate. User direction is explicitly pinned to the supplied Tarkov reference; code-led, no generated design comps.

## Direction contract

THESIS: Keep equipment, personal containers and searched ownership visually separate, and make empty drop targets discoverable.
OWN-WORLD: Dark charcoal and muted olive, source CJK font, existing vector item art and a baked static production-character portrait.
STORY: Players inspect their loadout, place equipment on visible body slots, organize carried storage, then transfer from the right-hand target with authoritative commands.
FIRST VIEWPORT: Joined left portrait/equipment and middle personal storage align vertically; right scrollable loot or warehouse owns the remaining width. Six body targets overlay the static full-body production character. Personal storage is ordered chest rig, pockets, three special tool slots, backpack, secure case. Container equipment uses a 96×96 slot beside the content grid. All storage cells remain 48×48 logical units; three tool targets remain 52×52 squares. Raid quickbar docks below.
FORM: User-pinned Tarkov inventory canon, native UXML/USS/C#; no random concept or approved generated comp.
FINISH: The finish reviewer returned ship for the corpse-portrait extension and overall regression using the three current native inventory fixtures below. The built system is recorded in token-bearing DESIGN.md and schemaVersion 2 .impeccable/design.json. FieldOperatorPortrait.png carries baked production-avatar provenance; no external ImageGen asset or runtime portrait camera is involved.

Validation uses native editor panel captures and real inventory command callbacks. No claim of physical mouse, formal-map Play or multiplayer acceptance. Secure container/tool persistence is checked in isolated local profiles, with online backend compilation only.

## Built surface contract

- One compact header row contains equipment/health tabs and close. Repeated headline, pane instructions, persistent tutorial captions and large container cards stay hidden.
- Own equipment has twelve targets: six body slots, chest rig/backpack/secure case entries in personal storage, and three compact special slots. Empty container entries remain visible.
- Own storage, loot, stash and opened containers share fixed 48×48 cells, preserving true capacity, item dimensions and rotation. Container equipment targets are 96×96, including empty and corpse targets. Special tool slots remain 52×52. Both scroll axes are available for narrow panes; geometry events and snapshot rebuilds never rescale cells.
- Left and middle stay joined; the right target scrolls independently. The raid quickbar reserves a 62-unit dock. Warehouse uses more width for its right-hand stash and does not show the raid dock.
- Corpse loot shows the opponent's actual-model static full-body portrait with six body equipment overlays in a 420-unit stage; each overlay is 108×62. Compact chest-rig/backpack icon entries and their contents follow below, then loose items. Identity comes from the death record. Original slot-to-item-ID snapshot metadata preserves primary/secondary ownership and leaves looted slots empty. Protected case and special tools are excluded from corpse loot. Model index 2 currently maps to the existing DollSinger bake; unknown/legacy identities safely retain the gear-list fallback. No runtime camera is created.
- Interactions retain authoritative inventory commands, drag preview, R rotation, right-click/Esc cancellation and modifier-click transfer. Failed operations retain the original item and explain the reason; the UI does not simulate transaction success.

## Reviewed evidence and limits

2026-10-23 sizing correction: static checks only so far. The existing Editor was busy building shaders and its bridge did not respond; the captures below predate fixed storage cells and enlarged equipment slots.

The current reviewed files are native Editor panel fixtures using production UXML and presenters, not formal gameplay captures. All three were directly viewed:

- `.impeccable/review/corpse-1600.png`: opponent portrait, six body overlays, compact corpse containers and own inventory.
- `.impeccable/review/corpse-1280.png`: the same corpse composition at the smaller viewport.
- `.impeccable/review/warehouse-1600.png`: populated warehouse regression, including compact 52×52 tool targets.

The 1600×900 fixtures are stored at 2400×1350 pixels (1.5 DPI scale); the 1280×720 fixture is stored at 1920×1080 pixels. Prior `secure-1600.png`, `cache-1280.png` and `empty-1280.png` remain historical evidence of their respective states; they are not current tool-sizing evidence.

`Temp/corpse-portrait-checks.txt` reports 21 passing assertions for corpse identity, original equipment slots, protected exclusion, snapshot fragments, actual loot transfer and legacy reset. `Temp/corpse-portrait-final/*-checks.txt` reports passing native corpse-to-equipment graph exchange at both reviewed sizes and warehouse-to-pocket regression. These are directed panel/transaction checks, not physical input acceptance.

The implementation owner reports 70 passing isolated persistence assertions and backend compilation. These checks cover local secure/tool persistence rules and compile compatibility only. They do not establish physical input, formal-map Play, functional compass/rangefinder readouts, PostgreSQL online persistence, multiplayer or target-device performance acceptance.

Runtime visual sources: `RaidInventory.uss`, `RaidContainerInventory.uxml`, `RaidUI.uxml`, `QuickSlots.uss`, `Components/CorpsePortrait.uxml`, `ContainerInventoryView.cs` and `ContainerInventoryView.Raid.cs`; corpse identity/slot data flows through `RaidInventoryData.cs`. The design sidecar's HTML/CSS snippets are documentation translations of native primitives, not replacement runtime UI.
