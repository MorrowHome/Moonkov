"""Retarget the current firearm library and seed Shian's Quaternius melee bake.

Uses the existing anatomical/contact solver rather than copying its mathematics.
After this command, build the Shian prefabs, call QuaterniusMeleeSetup.BakeShian()
in the existing Unity Editor, then run this tool with --validate-only.  The melee
seed intentionally carries a pending revision and must never be shipped as-is.
"""

import argparse
import copy
import json
import math
from pathlib import Path

from retarget_hatsune_miku import (
    ROOT, Retargeter as AnatomicalRetargeter, Rig, axis_angle, cross, digest,
    finger_chain, length, qmul, self_test, sub, write_library,
)


FIREARMS = ROOT / "Assets/DollSinger/Animation/Blender/WeaponGestures.json"
MELEE = ROOT / "Assets/DollSinger/Animation/Melee/Resources/MoonkovMeleeGestures.json"
SOURCE_BIND = ROOT / "Tools/Blender/DollSingerBind.json"
TARGET_BIND = ROOT / "Tools/Blender/ShianBind.json"
OUTPUT = ROOT / "Assets/Shian/Animation"
PENDING = "PENDING-Quaternius-exact-rig-bake"


class Retargeter(AnatomicalRetargeter):
    """Respect Shian's asymmetric finger-base spacing in the closed viewfinder."""

    def __init__(self, source, target):
        super().__init__(source, target)
        self.stats["maximumSniperFingerAdjustmentDegrees"] = 0.0

    def align_sniper(self, world):
        # Straight perpendicular index/thumb chains alone leave ~5 mm unequal
        # diagonals on this model. Rotate both complete chains together around
        # their separate bases: this preserves their shape and right angle while
        # allowing the two L shapes to meet without stretching any bone.
        positions, _ = self.target.evaluate(world=world)
        diagonal = {side: length(sub(positions["人指先." + side], positions["親指先." + side]))
                    for side in ("L", "R")}
        target_length = sum(diagonal.values()) * .5
        for side in ("L", "R"):
            normal = cross(sub(positions["人指先." + side], positions["人指１." + side]),
                           sub(positions["親指先." + side], positions["親指１." + side]))
            names = finger_chain("人指", side)[:-1] + finger_chain("親指", side)[:-1]
            original = {name: world[name] for name in names}

            def adjust(degrees):
                rotation = axis_angle(normal, math.radians(degrees))
                for name in names:
                    world[name] = qmul(rotation, original[name])
                p, _ = self.target.evaluate(world=world)
                return length(sub(p["人指先." + side], p["親指先." + side]))

            low, high = -15.0, 15.0
            low_length, high_length = adjust(low), adjust(high)
            if not min(low_length, high_length) <= target_length <= max(low_length, high_length):
                raise ValueError("Shian sniper diagonal cannot close within a 15 degree finger adjustment")
            for _ in range(24):
                angle = (low + high) * .5
                actual = adjust(angle)
                if (actual < target_length) == (low_length < high_length):
                    low = angle
                else:
                    high = angle
            adjust(angle)
            self.stats["maximumSniperFingerAdjustmentDegrees"] = max(
                self.stats["maximumSniperFingerAdjustmentDegrees"], abs(angle))
        super().align_sniper(world)


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def metadata(value):
    """Every non-payload field, including pitch and nested burst identities."""
    if isinstance(value, dict):
        return {k: metadata(v) for k, v in value.items()
                if k not in ("pose", "fire", "reload", "values")}
    if isinstance(value, list):
        return [metadata(v) for v in value]
    return value


def check_samples(values, frames, bones, label):
    if frames < 1 or len(values) != frames * len(bones) * 4:
        raise ValueError("Sample length mismatch: " + label)
    for start in range(0, len(values), 4):
        q = values[start:start + 4]
        if not all(math.isfinite(v) for v in q) or abs(sum(v * v for v in q) - 1) > .00001:
            raise ValueError("Invalid quaternion: " + label + " at " + str(start))


def validate_firearms(data, source, retargeter):
    if data["bones"] != retargeter.arm_tracks:
        raise ValueError("Firearm tracks do not match the target hierarchy")
    if metadata(data["motions"]) != metadata(source["motions"]):
        raise ValueError("Firearm timing, pitch, burst, identity or frame metadata changed")
    if len(data["motions"]) != 13 or data["frameRate"] != source["frameRate"]:
        raise ValueError("Incomplete source firearm library")
    total = 0

    def check(motion, inherited=None):
        nonlocal total
        frames = inherited or {k: motion[k + "Frames"] for k in ("pose", "fire", "reload")}
        for mode in frames:
            check_samples(motion[mode], frames[mode], data["bones"], mode)
            total += frames[mode]
        for variant in motion.get("burstFires", []):
            check_samples(variant["values"], frames["fire"], data["bones"], "burst")
            total += frames["fire"]
        for pose in motion.get("aimPoses", []):
            check(pose, frames)

    for motion in data["motions"]:
        check(motion)
    if data["retarget"]["sourceLibrarySha256"] != digest(FIREARMS):
        raise ValueError("Firearm source has changed; regenerate Shian's library")
    if data["retarget"]["targetBindSha256"] != digest(TARGET_BIND):
        raise ValueError("Target bind has changed; regenerate Shian's library")
    stats = data["retarget"]["statistics"]
    if stats["maximumSniperContactResidualMetres"] > .001 or stats["maximumWristSolveErrorMetres"] > .003:
        raise ValueError("Retarget solve exceeded the sniper/wrist tolerance")
    return total


def seed_melee(data, retargeter):
    # Reuse target grip calculation and track selection without transporting the
    # DollSinger Quaternius samples across anatomical frames a second time.
    seed = copy.deepcopy(data)
    for clip in seed["clips"]:
        clip["poses"] = []
    result = retargeter.melee(seed)
    bind = [round(v, 7) for name in result["bones"] for v in retargeter.target.local[name]]
    for clip in result["clips"]:
        clip["poses"] = [{"pitch": pitch, "frames": 1, "values": bind[:]} for pitch in (-70, 0, 70)]
    result["revision"] = PENDING
    result.pop("retarget", None)
    return result


def validate_melee(data, source, retargeter):
    if data["revision"] != source["revision"] or not data["revision"].startswith("Quaternius-"):
        raise ValueError("Run QuaterniusMeleeSetup.BakeShian() in Unity; melee is not an exact-rig bake")
    if data["bones"] != retargeter.torso_tracks + retargeter.arm_tracks:
        raise ValueError("Melee tracks do not match the target hierarchy")
    if [(c["weapon"], c["action"]) for c in data["clips"]] != [(c["weapon"], c["action"]) for c in source["clips"]]:
        raise ValueError("Melee actions differ from the current source library")
    if len(data["clips"]) != 24 or data["frameRate"] != source["frameRate"]:
        raise ValueError("Incomplete melee library")
    total = 0
    for clip, original in zip(data["clips"], source["clips"]):
        if [p["pitch"] for p in clip["poses"]] != [-70, 0, 70]:
            raise ValueError("Melee pitch coverage changed")
        for key in ("seconds", "windup", "active", "recovery"):
            if not math.isfinite(clip[key]) or clip[key] < 0:
                raise ValueError("Invalid melee timing")
        # Attack contact timing is measured on this skeleton; durations and
        # frame counts must retain the selected Quaternius source take.
        if abs(clip["seconds"] - original["seconds"]) > .00001:
            raise ValueError("Melee source take duration changed")
        for pose, source_pose in zip(clip["poses"], original["poses"]):
            if pose["frames"] != source_pose["frames"]:
                raise ValueError("Melee frame coverage changed")
            check_samples(pose["values"], pose["frames"], data["bones"], clip["weapon"] + "/" + clip["action"])
            total += pose["frames"]
    return total


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    source = Rig(load(SOURCE_BIND))
    if args.self_test:
        self_test(source)
        return
    protected = {p: digest(p) for p in (FIREARMS, MELEE, SOURCE_BIND, TARGET_BIND)}
    retargeter = Retargeter(source, Rig(load(TARGET_BIND)))
    guns, melee = load(FIREARMS), load(MELEE)
    if not args.validate_only:
        result = retargeter.firearms(guns)
        write_library(OUTPUT / "WeaponGestures.json", result, FIREARMS, SOURCE_BIND, TARGET_BIND, retargeter, "motions")
        validate_firearms(result, guns, retargeter)
        seed = seed_melee(melee, retargeter)
        (OUTPUT / "MeleeGestures.json").write_text(json.dumps(seed, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
        print("MELEE_SEED_READY: build Shian prefabs then call QuaterniusMeleeSetup.BakeShian()")
    else:
        print(json.dumps({"passed": True,
                          "firearmFrames": validate_firearms(load(OUTPUT / "WeaponGestures.json"), guns, retargeter),
                          "meleeFrames": validate_melee(load(OUTPUT / "MeleeGestures.json"), melee, retargeter),
                          "firearmBones": len(retargeter.arm_tracks),
                          "meleeBones": len(retargeter.arm_tracks + retargeter.torso_tracks)}, sort_keys=True))
    for path, before in protected.items():
        if digest(path) != before:
            raise RuntimeError("Source changed during bake: " + str(path))


if __name__ == "__main__":
    main()
