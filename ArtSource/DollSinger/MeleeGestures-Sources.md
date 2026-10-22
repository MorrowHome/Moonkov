# Melee authoring provenance

Created for Moonkov on 2026-10-22.

- New melee Actions, independent exact-rig baking script, energy-sword/dagger geometry and presentation shaders are original project work.
- The existing production DollSinger FBX, character mesh, skin weights and bind-axis authoring helpers remain existing project inputs. This work adds no new license to the character model.
- No downloaded animation/model/texture or paid asset was incorporated into this melee addition.

Sources reviewed, not imported:

- Adobe Mixamo FAQ: https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html — the official FAQ permits royalty-free personal/commercial game use. No Mixamo clip was downloaded or retargeted here.
- Kenney: https://kenney.nl/support and https://kenney.nl/assets/animated-characters-protagonists — official pages label game assets CC0, with no mandatory attribution. No package was imported.
- Quaternius Universal Animation Library: https://quaternius.com/packs/universalanimationlibrary.html — that specific pack page lists CC0. No archive or paid/source edition was imported.

The runtime JSON weapon meshes come directly from the original low-poly solids authored in MeleeGestures.blend. Their vertices, material colors and normals use the same explicit coordinate conversion as the project's rig baking. There are no external textures to attribute.
