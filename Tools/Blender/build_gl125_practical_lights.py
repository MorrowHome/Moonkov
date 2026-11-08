"""Export real GL125 fixture positions and lens corrections for Unity."""

import argparse
import collections
import hashlib
import json
import pathlib
import re

import numpy as np

from pack_gl125 import json_read, json_write


FIXTURE_MATERIAL = re.compile(
    r"diffuser|neutralTask|worklight|warm_emitter|entry_lens|luminaire|warm_service_light",
    re.IGNORECASE,
)


def practical_lights(evaluated, prepared, output):
    world_bytes = (prepared / "World.json").read_bytes()
    world = json.loads(world_bytes)
    export = json_read(evaluated / "Export.json")
    if export["sourceHash"] != world["sourceHash"]:
        raise ValueError("GL125 light and geometry revisions differ.")
    lights = [row for row in world["lights"] if row["type"] == "AREA" and row["energy"] > 0]
    positions = np.asarray([light["position"] for light in lights])
    materials = {
        index: material for index, material in enumerate(world["materials"])
        if FIXTURE_MATERIAL.search(material["name"])
    }
    fixtures = {}
    dark_lenses = collections.defaultdict(list)
    authored_lenses = {}

    def add(name, position, forward, color, size, probe=False):
        color = np.asarray(color[:3], dtype=float)
        if not np.isfinite(color).all() or color.max() <= 0:
            return
        position = np.asarray(position) + np.asarray(forward) * .065
        # A linked light and its mesh should produce only one runtime source.
        key = tuple(np.round(position / .22).astype(int))
        fixtures.setdefault(key, dict(
            name=name, position=position.tolist(), forward=list(forward), color=(color / color.max()).tolist(),
            range=float(np.clip(4.3 + 1.3 * size, 4.6, 9.5)),
            intensity=float(np.clip(.65 + 1.12 * size, .8, 4.3)),
            probe=probe,
        ))

    for light in lights:
        if light["name"].startswith(("UA38_CEILING_", "UA38_UNDER_GALLERY_")):
            add(light["name"], light["position"], light["forward"], light["color"], light["size"])

    for part in export["parts"]:
        with np.load(evaluated / part["file"], allow_pickle=False) as packed:
            geometry, material_ids = packed["mesh"], packed["material"]
        for row in part["objects"]:
            if row["proxy"] or row["count"] == 0 or row["name"] == "UA38_HALL_LIGHT_LENSES":
                continue
            start = row["offset"]
            stop = start + row["count"]
            ids = material_ids[start:stop]
            for index in set(np.unique(ids)).intersection(materials):
                points = geometry[start:stop][ids == index, :, :3].reshape(-1, 3)
                low, high = points.min(axis=0), points.max(axis=0)
                span = high - low
                size = float(np.linalg.norm(span))
                if size < .14 or size > 6.5:
                    continue
                center = (low + high) * .5
                distance = np.linalg.norm(positions - center, axis=1)
                nearest = int(distance.argmin())
                emission = materials[index]["emission"]
                if max(emission) > .1:
                    authored_lenses[int(index)] = emission
                if distance[nearest] <= .28:
                    light = lights[nearest]
                    add(light["name"], light["position"], light["forward"], light["color"], light["size"])
                    if max(emission) <= .01:
                        peak = float(np.clip(.85 + .13 * np.sqrt(light["energy"]), 1.05, 3.6))
                        color = np.asarray(light["color"], dtype=float)
                        dark_lenses[int(index)].append(color / color.max() * peak)
                elif max(emission) > .1 and span[2] < .23:
                    # Ceiling panels can have emissive geometry without a
                    # Blender Light object. Use the underside of that lens.
                    center[2] = low[2]
                    add(row["name"], center, (0., 0., -1.), emission, size, probe=True)
    if len(fixtures) < 100 or not dark_lenses:
        raise ValueError("The source export is missing practical lighting geometry.")
    for index, colors in dark_lenses.items():
        authored_lenses[index] = np.mean(colors, axis=0).tolist()
    corrections = [dict(material=index, emission=emission) for index, emission in sorted(authored_lenses.items())]
    json_write(output, dict(version=1, worldHash=hashlib.sha256(world_bytes).hexdigest(),
                            fixtures=sorted(fixtures.values(), key=lambda row: row["name"]), lenses=corrections))
    print(f"{len(fixtures)} fixture sources, {len(corrections)} lens material corrections.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evaluated", type=pathlib.Path)
    parser.add_argument("prepared", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    practical_lights(args.evaluated, args.prepared, args.output)


if __name__ == "__main__":
    main()
