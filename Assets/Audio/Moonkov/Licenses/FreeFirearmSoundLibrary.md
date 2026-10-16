# The Free Firearm Sound Library

- Creators: Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney / Still North Media.
- Source and license evidence: https://opengameart.org/content/the-free-firearm-sound-library
- Source archive: https://opengameart.org/sites/default/files/Prepared%20SFX%20Library.7z
- License: CC0 1.0 Universal, https://creativecommons.org/publicdomain/zero/1.0/
- Retrieved: 2026-10-16.

The source page identifies this collection as CC0 and permits personal and professional use without royalty or credit. Credit is retained here for provenance. Only edited AR-15, Smith & Wesson 642 revolver and Mosin Nagant reports are included in the project; the downloaded source archive remains in ignored `LocalData/ShootingAV/Sources/`.

`Assets/Audio/Moonkov/ShootingSources.json` records source paths, source and output SHA-256 hashes, excerpt offsets and processing. Rebuild with `python3 Tools/Audio/build_shooting_audio.py` after extracting the source archive into `LocalData/ShootingAV/Sources/` (requires numpy and ffmpeg).

These assets replace rifle, revolver and sniper presentation sounds. The existing shotgun sounds and their licenses are unchanged.
