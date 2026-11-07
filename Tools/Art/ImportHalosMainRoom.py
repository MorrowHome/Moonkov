"""Import the original Halos room without repainting or rearranging its artwork."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import uuid
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, required=True, help="Halos / Moonkov UIs")
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
destination = root / "Assets/Resources/Moonkov/UI/Halos/MainRoom"
archive = root / "ArtSource/HalosUI/MainRoom"
destination.mkdir(parents=True, exist_ok=True)
archive.mkdir(parents=True, exist_ok=True)
meta_template = (root / "Assets/Resources/Moonkov/UI/Halos/Noise.png.meta").read_text()
manifest = {"source": "Halos / Moonkov UIs", "commit": "2511cfad1a2ebb635839489ff3ee1242d8a4aa02", "assets": []}
layout = {"icons": []}
# Draw positions and mouse rectangles from mainRoomControl. Pages bind the empty art demo to real features.
placements = {
    "MRUI_left": [(330, -100, 1, 2, 720, 655, 295, 225), (330, -100, 1, 3, 460, 245, 225, 170), (330, -100, 1, 6, 1075, 430, 345, 225)],
    "MRUI_mid": [(320, 280, .4, 4, 495, 575, 295, 180), (300, 240, .4, 1, 380, 250, 210, 235)],
    "MRUI_right": [(330, -100, 1, 5, 1185, 440, 225, 210), (330, -100, 1, 7, 870, 680, 300, 170), (320, -100, 1, 8, 450, 135, 360, 255)],
}


def frames(sprite):
    folder = args.source / "sprites" / sprite
    data = json.loads(re.sub(r",\s*([}\]])", r"\1", (folder / (sprite + ".yy")).read_text(encoding="utf-8-sig")))
    return [folder / (frame["name"] + ".png") for frame in data["frames"]]


def save_meta(path, size):
    meta = path.with_suffix(path.suffix + ".meta")
    text = meta.read_text() if meta.exists() else re.sub(r"(?m)^guid: .+$", "guid: " + uuid.uuid4().hex, meta_template)
    text = re.sub(r"(?m)^(\s*maxTextureSize:) \d+$", rf"\g<1> {size}", text)
    meta.write_text(text, encoding="utf-8")


for sprite, count in [("MRUI_left", 3), ("MRUI_mid", 2), ("MRUI_right", 3)]:
    paths = frames(sprite)
    for index in range(count):
        pair = [Image.open(path).convert("RGBA") for path in paths[index * 2:index * 2 + 2]]
        bounds = [image.getchannel("A").getbbox() for image in pair]
        crop = (max(0, min(b[0] for b in bounds) - 8), max(0, min(b[1] for b in bounds) - 8),
                min(pair[0].width, max(b[2] for b in bounds) + 8), min(pair[0].height, max(b[3] for b in bounds) + 8))
        for state, image in enumerate(pair):
            source = paths[index * 2 + state]
            name = f"{sprite[5:].capitalize()}{index}{'Hover' if state else ''}.png"
            target = destination / name
            image.crop(crop).save(target)
            save_meta(target, 512)
            manifest["assets"].append({"output": name, "source": source.relative_to(args.source).as_posix(),
                                       "crop": crop, "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest()})
        x, y, scale, page, hx, hy, hw, hh = placements[sprite][index]
        room = ["MRUI_left", "MRUI_mid", "MRUI_right"].index(sprite)
        layout["icons"].append({"name": f"{sprite[5:].capitalize()}{index}", "room": room, "page": page,
                                "x": x + crop[0] * scale, "y": y + crop[1] * scale,
                                "width": (crop[2] - crop[0]) * scale, "height": (crop[3] - crop[1]) * scale,
                                "hoverOffset": [-30, 21, 30][room], "hitX": hx, "hitY": hy, "hitWidth": hw, "hitHeight": hh})

for sprite, stem in [("moon", "Moon"), ("mainRoomBG", "Room"), ("tabletParts", "Tablet"), ("screenButton", "Arrow")]:
    paths = frames(sprite)
    for index, source in enumerate(paths):
        name = f"{stem}{index if len(paths) > 1 else ''}.png"
        shutil.copyfile(source, destination / name)
        save_meta(destination / name, 2048)
        manifest["assets"].append({"output": name, "source": source.relative_to(args.source).as_posix(),
                                   "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest()})
source = frames("mainRoomLights")[0]
lights = Image.open(source)
for index in range(3):
    name = f"Lights{index}.png"
    crop = (index * 1920, 0, min((index + 1) * 1920, lights.width), lights.height)
    lights.crop(crop).save(destination / name)
    save_meta(destination / name, 2048)
    manifest["assets"].append({"output": name, "source": source.relative_to(args.source).as_posix(), "crop": crop,
                               "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest()})
(destination / "Layout.json").write_text(json.dumps(layout, indent=2) + "\n", encoding="utf-8")
for path in (args.source / "objects/mainRoomControl").glob("*.gml"):
    code = "\n".join(line.rstrip() for line in path.read_text(encoding="utf-8-sig").splitlines()).rstrip() + "\n"
    (archive / path.name).write_text(code, encoding="utf-8", newline="\n")
(archive / "source-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Imported {len(manifest['assets'])} existing art assets.")
