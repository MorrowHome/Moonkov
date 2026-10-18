# Moonkov inventory art

`InventoryAtlas.png` is a new, UI-only raster atlas generated with the built-in image tool. It depicts six existing inventory categories: flight helmet, chest rig, backpack, lunar regolith, alloy and energy cell. It does not add inventory definitions, attributes or world models. The original DollSinger model was not supplied to or redrawn by the image model.

The 1536×1024 RGBA source retains transparency. Unity's Sprite Editor data provider slices six 512×512 rectangles into Helmet, Rig, Backpack, Dust, Alloy and Cell, with capability checks and stable generated IDs stored by Unity in the importer. No pixel cropping or manual importer metadata editing was used. Compression HQ, no mipmaps, Clamp, max size 2048; no runtime image processing.

`StashItemArt.ArchiveSprite` maps existing item art kinds to the six sprites and retains vector fallback for missing assets. Variants of rigs/backpacks share their category artwork; their actual names and definitions remain separate. Other consumables and specialist items retain their existing original vector art. Remove the mapping to revert to that art.

Prompt: “Production sprite atlas for existing Moonkov inventory, equal 3 columns by 2 rows with isolated centered items. Top row EVA flight helmet, folded chest rig, cargo backpack. Bottom row lunar regolith stones, machined silver alloy ingots, sealed industrial energy cell. Refined monochrome studio product rendering, consistent three-quarter view, off-white ceramic, brushed titanium and graphite fabric, subtle believable wear, transparent background and gutters. No text, numbers, labels, UI frames, logos, character, hands, guns, purple, cyan neon or colored emission.”
