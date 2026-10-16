# In-raid foley and creature sound credits

Edited for Moonkov, 2026-10-16. Files: `Clips/Raid/`. Exact source and output SHA256s, recipes and collection links: `RaidAudioSources.json`. Rebuild: `Tools/Audio/build_raid_audio.py` (numpy + ffmpeg).

- **Monster Sound Effects 2**, Ogrebane, [OpenGameArt](https://opengameart.org/content/monster-sound-effects-2), [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/). Organic vocal performances; edited with varispeed, filtered parallel throat layer, damped short delays and fades for warning/strike/hurt/death/idle variants.
- **Footsteps on different surfaces**, mastered by congusbongus, [OpenGameArt](https://opengameart.org/content/footsteps-on-different-surfaces). Individual source licenses below are preserved from the archive's per-folder notices. Edited: resampling, filtering, contact body, fades, peak adjustment. These notices must accompany redistributed assets, including game credits/license documentation.
  - Gravel: **Ali_6868**, [Gravel Footsteps](https://freesound.org/people/Ali_6868/packs/21608/), CC0 1.0.
  - Metal: **Eelke**, [fboots on aluminum ladder 01](https://freesound.org/people/Eelke/sounds/462598/), **[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)**. Used for Metal1–8.
  - Boots: **swuing**, [footstep-concrete.wav](https://www.freesound.org/people/swuing/sounds/38873/), **CC BY 3.0**. Used for Hard1–8.
  - Bone shell: **blukotek**, [bone shell](https://freesound.org/people/blukotek/sounds/249319/), CC0 1.0. Used with gravel for AlienContact1–4.
- Aim/Light/DryFire/ReloadReady/Medical/Pickup/OxygenRefill use edits of the existing project's licensed cloth/equipment/container foley. Their original provenance remains in `Sources.json` and existing Sonniss license notices.
- SuitBreath/Heartbeat/SuitWarning/ExtractStart/ExtractCancel/ExtractTick: deterministic original signal design in the rebuild script, no third-party recording.

No gunfire, hand-selected menu sounds, deployment voice or ship music is replaced by this revision.
