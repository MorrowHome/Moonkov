#!/usr/bin/env python3
"""Rasterize the editable geometric source with the installed librsvg tool."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile


def main():
    source = Path(__file__).resolve().with_name("ModuleArtwork.svg")
    output = source.parents[2] / "Assets/Resources/Moonkov/HaloModules/ModuleArtwork.png"
    renderer = shutil.which("rsvg-convert")
    if renderer is None:
        raise SystemExit("需要已有的 rsvg-convert。不要改用有损图像格式。")
    # Render outside Assets and publish atomically; an open Editor never sees
    # an incomplete PNG. The existing .meta and its GUID stay untouched.
    with tempfile.TemporaryDirectory(prefix="moonkov-halo-art-") as temporary:
        rendered = Path(temporary) / "ModuleArtwork.png"
        subprocess.run([renderer, "--format=png", "--output", str(rendered), str(source)], check=True)
        data = rendered.read_bytes()
        if data[:8] != b"\x89PNG\r\n\x1a\n" or int.from_bytes(data[16:20], "big") != 1024 or int.from_bytes(data[20:24], "big") != 512:
            raise SystemExit("图集必须是 1024x512 PNG。")
        pending = output.with_suffix(".png.pending")
        try:
            pending.write_bytes(data)
            os.replace(pending, output)
        finally:
            if pending.exists():
                pending.unlink()
    print(output)


if __name__ == "__main__":
    main()
