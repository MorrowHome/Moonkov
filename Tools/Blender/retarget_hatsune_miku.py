"""Bake DollSinger gesture semantics onto the imported Hatsune Miku skeleton.

Run with ordinary Python 3; no Blender/Unity process or third-party modules needed::

    python3 Tools/Blender/retarget_hatsune_miku.py --self-test
    python3 Tools/Blender/retarget_hatsune_miku.py

Both bind files must describe the final Unity hierarchy in the same character
coordinate system, with world-space metre positions and xyzw rotations.  The
target FBX must contain plain transforms, without live MMD grant constraints.
Finger tip markers must be children of the final phalanges.  This tool never
changes either source library, either skeleton, or any existing Blender file.

Unlike bind-delta copying, anatomical frames align each target bone segment to
the evaluated source segment.  Target hand dimensions determine contact offsets;
target arm lengths determine the two-bone solve.  Runtime still samples an exact
local-rotation library, with its existing common shoulder-pair aim correction.
"""

import argparse
import copy
import hashlib
import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
IDENTITY = (0.0, 0.0, 0.0, 1.0)
SIDES = ("L", "R")
DIGITS = ("人指", "中指", "薬指", "小指", "親指")
EPSILON = 1e-10


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def mul(v, factor):
    return tuple(x * factor for x in v)


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def length(v):
    return math.sqrt(dot(v, v))


def unit(v):
    magnitude = length(v)
    if magnitude < EPSILON:
        raise ValueError("Degenerate anatomical axis: " + repr(v))
    return mul(v, 1 / magnitude)


def qnorm(q):
    return unit(q)


def qinverse(q):
    return (-q[0], -q[1], -q[2], q[3])


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qrotate(q, v):
    t = mul(cross(q[:3], v), 2)
    return add(v, add(mul(t, q[3]), cross(q[:3], t)))


def axis_angle(axis, radians):
    return (*mul(unit(axis), math.sin(radians / 2)), math.cos(radians / 2))


def qslerp(a, b, t):
    cosine = dot(a, b)
    if cosine < 0:
        b = mul(b, -1)
        cosine = -cosine
    cosine = min(1, cosine)
    if cosine > .9995:
        return qnorm(add(mul(a, 1 - t), mul(b, t)))
    angle = math.acos(cosine)
    return add(mul(a, math.sin((1 - t) * angle) / math.sin(angle)),
               mul(b, math.sin(t * angle) / math.sin(angle)))


def qangle(q):
    return math.degrees(2 * math.acos(min(1, abs(qnorm(q)[3]))))


def between(a, b):
    a, b = unit(a), unit(b)
    cosine = dot(a, b)
    if cosine < -.999999:
        helper = (1, 0, 0) if abs(a[0]) < .8 else (0, 1, 0)
        return axis_angle(cross(a, helper), math.pi)
    return qnorm((*cross(a, b), 1 + cosine))


def frame(y, x):
    """Right-handed anatomical frame, matching weapon_gestures.frame()."""
    y = unit(y)
    x = unit(sub(x, mul(y, dot(x, y))))
    z = cross(x, y)
    m00, m01, m02 = x[0], y[0], z[0]
    m10, m11, m12 = x[1], y[1], z[1]
    m20, m21, m22 = x[2], y[2], z[2]
    trace = m00 + m11 + m22
    if trace > 0:
        s = math.sqrt(trace + 1) * 2
        q = ((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, s / 4)
    elif m00 > m11 and m00 > m22:
        s = math.sqrt(1 + m00 - m11 - m22) * 2
        q = (s / 4, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s)
    elif m11 > m22:
        s = math.sqrt(1 + m11 - m00 - m22) * 2
        q = ((m01 + m10) / s, s / 4, (m12 + m21) / s, (m02 - m20) / s)
    else:
        s = math.sqrt(1 + m22 - m00 - m11) * 2
        q = ((m02 + m20) / s, (m12 + m21) / s, s / 4, (m10 - m01) / s)
    return qnorm(q)


def named(prefix, side):
    return prefix + "." + side


def finger_chain(digit, side):
    joints = ("０", "１", "２") if digit == "親指" else ("１", "２", "３")
    return [named(digit + joint, side) for joint in joints] + [named(digit + "先", side)]


class Rig:
    def __init__(self, data):
        records = data["bones"]
        self.records = {b["name"]: b for b in records}
        if len(self.records) != len(records):
            raise ValueError("Duplicate bone names in bind file")
        self.p = {n: tuple(b["position"]) for n, b in self.records.items()}
        self.r = {n: qnorm(tuple(b["rotation"])) for n, b in self.records.items()}
        self.parent = {n: b["parent"] for n, b in self.records.items()}
        self.order = []
        visiting = set()

        def visit(n):
            if n in self.order:
                return
            if n in visiting:
                raise ValueError("Bind hierarchy cycle: " + n)
            visiting.add(n)
            if self.parent[n] in self.records:
                visit(self.parent[n])
            visiting.remove(n)
            self.order.append(n)

        for n in self.records:
            visit(n)
        self.local = {}
        self.offset = {}
        for n in self.order:
            p = self.parent[n]
            if p in self.records:
                self.local[n] = qnorm(qmul(qinverse(self.r[p]), self.r[n]))
                self.offset[n] = qrotate(qinverse(self.r[p]), sub(self.p[n], self.p[p]))
            else:
                self.local[n] = self.r[n]
                self.offset[n] = self.p[n]

    def child_of(self, n, root):
        while n in self.records:
            if n == root:
                return True
            n = self.parent[n]
        return False

    def evaluate(self, local=None, world=None):
        """FK in canonical metres; bind offsets already include import scale."""
        local, world = local or {}, world or {}
        positions, rotations = {}, {}
        for n in self.order:
            p = self.parent[n]
            if p in rotations:
                positions[n] = add(positions[p], qrotate(rotations[p], self.offset[n]))
                rotations[n] = world.get(n) or qmul(rotations[p], local.get(n, self.local[n]))
            else:
                positions[n] = self.p[n]
                rotations[n] = world.get(n) or local.get(n, self.r[n])
        return positions, rotations


def palm_frame(rig, side):
    h, m = named("手首", side), named("中指１", side)
    across = sub(rig.p[named("人指１", side)], rig.p[named("小指１", side)])
    return frame(sub(rig.p[m], rig.p[h]), across)


class Retargeter:
    def __init__(self, source, target):
        self.source, self.target = source, target
        required = ["上半身", "上半身2", "頭", "足首.L", "足首.R"]
        for side in SIDES:
            required += [named(b, side) for b in ("肩P", "肩", "肩C", "腕", "腕捩", "ひじ", "手捩", "手首")]
            for digit in DIGITS:
                required += finger_chain(digit, side)
        for label, rig in (("source", source), ("target", target)):
            missing = [n for n in required if n not in rig.records]
            if missing:
                raise ValueError(label + " bind missing bones: " + ", ".join(missing))
        source_height = source.p["頭"][1] - min(source.p[named("足首", s)][1] for s in SIDES)
        target_height = target.p["頭"][1] - min(target.p[named("足首", s)][1] for s in SIDES)
        self.scale = target_height / source_height
        if not .2 < self.scale < 5:
            raise ValueError("Bind files disagree on metre scale: " + str(self.scale))
        self.arm_tracks = [n for n in target.order if any(target.child_of(n, named("肩P", s)) for s in SIDES)]
        # Miku has an additional 上半身1 between the two authored chest bones.
        # Include it in melee's restore/playback set, even though the source
        # has no corresponding track and its authored local stays at bind.
        self.torso_tracks = [n for n in target.order if target.child_of(n, "上半身")
                             and target.child_of("上半身2", n)]
        self.semantic = {}
        for side in SIDES:
            h = named("手首", side)
            self.semantic[h] = qmul(palm_frame(source, side), qmul(qinverse(palm_frame(target, side)), target.r[h]))
            for digit in DIGITS:
                chain = finger_chain(digit, side)
                for n, end in zip(chain, chain[1:]):
                    axes = []
                    for rig in (source, target):
                        across = sub(rig.p[named("人指１", side)], rig.p[named("小指１", side)])
                        forward = sub(rig.p[named("中指１", side)], rig.p[h])
                        aux = cross(across, forward) if digit == "親指" else across
                        axes.append(frame(sub(rig.p[end], rig.p[n]), aux))
                    # The source delta transports this bind anatomical frame.
                    self.semantic[n] = qmul(axes[0], qmul(qinverse(axes[1]), target.r[n]))
        self.arm_frames = {}
        for side in SIDES:
            upper, lower, wrist = (named(b, side) for b in ("腕", "ひじ", "手首"))
            u, l = sub(target.p[lower], target.p[upper]), sub(target.p[wrist], target.p[lower])
            rest_u, rest_l = unit(u), unit(l)
            rest_side = unit(sub(target.p[named("人指１", side)], target.p[named("小指１", side)]))
            rest_n = unit(sub(rest_side, mul(rest_u, dot(rest_u, rest_side))))
            if dot(rest_n, cross(rest_u, (0, 0, 1))) < 0:
                rest_n = mul(rest_n, -1)
            self.arm_frames[side] = (length(u), length(l),
                qmul(qinverse(frame(rest_u, rest_n)), target.r[upper]),
                qmul(qinverse(frame(rest_l, rest_n)), target.r[lower]))
        self.stats = {"frames": 0, "maximumContactResidualMetres": 0.0,
                      "maximumWeightedContactResidualMetres": 0.0,
                      "maximumSniperContactResidualMetres": 0.0,
                      "maximumReachAdjustmentMetres": 0.0,
                      "maximumWristSolveErrorMetres": 0.0}

    def semantic_rotation(self, name, source_world):
        delta = qmul(source_world[name], qinverse(self.source.r[name]))
        return qnorm(qmul(delta, self.semantic[name]))

    def bind_delta(self, name, source_world):
        return qnorm(qmul(qmul(source_world[name], qinverse(self.source.r[name])), self.target.r[name]))

    def arm(self, side, wrist, guide, world, starts, previous=None, anatomical_wrist_limit=None, elbow_max_y=None, pose_penalty=None):
        """Target elbow-circle solve; share roll between elbow and wrist twist."""
        t = self.target
        upper, lower, h, twist, upper_twist = (named(b, side) for b in ("腕", "ひじ", "手首", "手捩", "腕捩"))
        a, b, upper_frame, lower_frame = self.arm_frames[side]
        start = starts[upper]
        v = sub(wrist, start)
        distance = length(v)
        if distance >= a + b or distance < abs(a - b) + 1e-6:
            raise ValueError("Unreachable arm target: " + side + " " + str(distance))
        axis = unit(v)
        along = (a * a - b * b + distance * distance) / (2 * distance)
        height = math.sqrt(max(0, a * a - along * along))
        pole = sub(guide, start)
        projected = sub(pole, mul(axis, dot(pole, axis)))
        if length(projected) < 1e-7:
            projected = cross(axis, (0, 0, 1))
        if length(projected) < 1e-7:
            projected = cross(axis, (0, 1, 0))
        pole = unit(projected)
        best = None
        for degrees in range(-75, 76, 5):
            bend = qrotate(axis_angle(axis, math.radians(degrees)), pole)
            elbow = add(start, add(mul(axis, along), mul(bend, height)))
            u, l = unit(sub(elbow, start)), unit(sub(wrist, elbow))
            normal = unit(cross(u, l))
            upper_rot = qmul(frame(u, normal), upper_frame)
            base_lower = qmul(frame(l, normal), lower_frame)
            expected = qmul(qmul(base_lower, qinverse(t.r[lower])), t.r[h])
            delta = qmul(world[h], qinverse(expected))
            vector = mul(l, dot(delta[:3], l))
            twist_q = (vector[0], vector[1], vector[2], delta[3])
            twist_q = qnorm(twist_q) if length(twist_q) > 1e-7 else IDENTITY
            if twist_q[3] < 0:
                twist_q = mul(twist_q, -1)
            raw = qangle(twist_q)
            applied = twist_q if raw <= 105 else qslerp(IDENTITY, twist_q, 105 / raw)
            wrist_angle = qangle(qmul(world[h], qinverse(qmul(applied, expected))))
            score = (wrist_angle / 55) ** 4 + (raw / 105) ** 4 + .4 * (degrees / 50) ** 2
            if anatomical_wrist_limit is not None:
                palm_forward = unit(qrotate(qmul(world[h], qinverse(t.r[h])), sub(t.p[named("中指１", side)], t.p[h])))
                bend_degrees = math.degrees(math.acos(max(-1, min(1, dot(l, palm_forward)))))
                score += (max(0, bend_degrees - anatomical_wrist_limit) / 10) ** 4
            if elbow_max_y is not None:
                score += (max(0, elbow[1] - elbow_max_y) / .01) ** 4
            if pose_penalty is not None:
                score += pose_penalty(elbow, wrist)
            if previous:
                # Moving contacts need temporal coherence between equivalent
                # elbow-circle solutions. Static aiming retains its original score.
                candidate_lower = qmul(qslerp(IDENTITY, applied, .5), base_lower)
                score += 1.5 * (qangle(qmul(upper_rot, qinverse(previous[upper]))) / 20) ** 2
                score += 1.5 * (qangle(qmul(candidate_lower, qinverse(previous[lower]))) / 20) ** 2
            if best is None or score < best[0]:
                best = (score, upper_rot, base_lower, applied)
        _, upper_rot, base_lower, roll = best
        world[upper] = upper_rot
        world[upper_twist] = qmul(qmul(upper_rot, qinverse(t.r[upper])), t.r[upper_twist])
        world[lower] = qmul(qslerp(IDENTITY, roll, .5), base_lower)
        world[twist] = qmul(qmul(qmul(roll, base_lower), qinverse(t.r[lower])), t.r[twist])
        for driver, parent, prefix in ((upper_twist, upper, "腕捩"), (twist, lower, "手捩")):
            inherited_driver = qmul(qmul(world[parent], qinverse(t.r[parent])), t.r[driver])
            additional = qmul(world[driver], qinverse(inherited_driver))
            for i, fraction in ((1, .25), (2, .5), (3, .75)):
                name = named(prefix + str(i), side)
                if name in t.records:
                    inherited = qmul(qmul(world[parent], qinverse(t.r[parent])), t.r[name])
                    world[name] = qmul(qslerp(IDENTITY, additional, fraction), inherited)

    def relative_tips(self, world):
        positions, _ = self.target.evaluate(world=world)
        return {named(d + "先", s): sub(positions[named(d + "先", s)], positions[named("手首", s)])
                for s in SIDES for d in DIGITS}

    def align_sniper(self, world):
        """Share a small whole-hand rotation to align the two L diagonals."""
        tips = self.relative_tips(world)
        left = sub(tips["人指先.L"], tips["親指先.L"])
        right = sub(tips["親指先.R"], tips["人指先.R"])
        halfway = qslerp(IDENTITY, between(left, right), .5)
        for side, rotation in (("L", halfway), ("R", qinverse(halfway))):
            for n in self.semantic:
                if n.endswith("." + side):
                    world[n] = qmul(rotation, world[n])

    def contact_wrist_targets(self, source_p, world, wrists, sniper):
        if sniper:
            self.align_sniper(world)
        tips = self.relative_tips(world)
        pairs = [("人指先.L", "親指先.R"), ("親指先.L", "人指先.R")] if sniper else [
            (named(a + "先", "L"), named(b + "先", "R")) for a in DIGITS for b in DIGITS]
        baseline = sub(wrists["L"], wrists["R"])
        candidates, weights, constraints = [], [], []
        for left, right in pairs:
            gap = sub(source_p[left], source_p[right])
            distance = length(gap)
            if sniper:
                weight = 1.0
            elif distance < .055:
                # The revolver's deliberately spaced hammer/blade contact is
                # about 35 mm apart in the source, rather than an exact touch.
                x = max(0, min(1, (.055 - distance) / .035))
                weight = x * x * (3 - 2 * x)
            else:
                continue
            # Local hand dimensions replace the uniformly scaled source offsets.
            desired_gap = mul(gap, self.scale)
            desired_separation = sub(desired_gap, sub(tips[left], tips[right]))
            candidates.append(desired_separation)
            weights.append(weight)
            constraints.append((left, right, desired_gap, weight))
        if candidates:
            total = sum(weights)
            candidate = mul(tuple(sum(v[j] * w for v, w in zip(candidates, weights)) for j in range(3)), 1 / total)
            contact_mix = 1 if sniper else max(weights)
            separation = add(mul(baseline, 1 - contact_mix), mul(candidate, contact_mix))
            center = mul(add(wrists["L"], wrists["R"]), .5)
            wrists = {"L": add(center, mul(separation, .5)), "R": sub(center, mul(separation, .5))}
        return wrists, constraints

    def keep_pair_reachable(self, wrists, starts):
        separation = sub(wrists["L"], wrists["R"])
        original = mul(add(wrists["L"], wrists["R"]), .5)
        center = original
        # Project the common center into the intersection of both reach spheres;
        # independent wrist clamps would destroy fingertip contacts.
        spheres = []
        for side, sign in (("L", 1), ("R", -1)):
            a, b = self.arm_frames[side][:2]
            sphere_center = sub(starts[named("腕", side)], mul(separation, sign * .5))
            spheres.append((sphere_center, (a + b) * .995))
        for _ in range(64):
            moved = False
            for origin, radius in spheres:
                delta = sub(center, origin)
                distance = length(delta)
                if distance > radius:
                    center = add(origin, mul(delta, radius / distance))
                    moved = True
            if not moved:
                break
        if any(length(sub(center, origin)) > radius + 1e-6 for origin, radius in spheres):
            raise ValueError("No reachable paired wrist arrangement; inspect target scale/hand contact")
        self.stats["maximumReachAdjustmentMetres"] = max(self.stats["maximumReachAdjustmentMetres"], length(sub(center, original)))
        return {"L": add(center, mul(separation, .5)), "R": sub(center, mul(separation, .5))}

    def sample(self, source_tracks, values, frame_index, melee=False, sniper=False):
        stride = len(source_tracks) * 4
        start = frame_index * stride
        local = {name: qnorm(tuple(values[start + i * 4:start + i * 4 + 4])) for i, name in enumerate(source_tracks)}
        source_p, source_r = self.source.evaluate(local=local)
        world = {}
        # Only the chest is shared with the Humanoid Animator; keep the target's
        # own clavicle offsets while transporting source torso wind-up.
        owned = self.torso_tracks if melee else []
        for n in owned + self.arm_tracks:
            if n in source_r and n not in self.semantic:
                world[n] = self.bind_delta(n, source_r)
        for n in self.semantic:
            world[n] = self.semantic_rotation(n, source_r)
        target_p, _ = self.target.evaluate(world=world)
        anchor_s, anchor_t = source_p["上半身2"], target_p["上半身2"]
        wrists = {s: add(anchor_t, mul(sub(source_p[named("手首", s)], anchor_s), self.scale)) for s in SIDES}
        wrists, constraints = self.contact_wrist_targets(source_p, world, wrists, sniper)
        wrists = self.keep_pair_reachable(wrists, target_p)
        for side in SIDES:
            guide = add(anchor_t, mul(sub(source_p[named("ひじ", side)], anchor_s), self.scale))
            self.arm(side, wrists[side], guide, world, target_p)
        final_p, final_r = self.target.evaluate(world=world)
        for side in SIDES:
            error = length(sub(final_p[named("手首", side)], wrists[side]))
            self.stats["maximumWristSolveErrorMetres"] = max(self.stats["maximumWristSolveErrorMetres"], error)
        for left, right, desired, weight in constraints:
            error = length(sub(sub(final_p[left], final_p[right]), desired))
            self.stats["maximumWeightedContactResidualMetres"] = max(
                self.stats["maximumWeightedContactResidualMetres"], error * weight)
            key = "maximumSniperContactResidualMetres" if sniper else "maximumContactResidualMetres"
            if weight >= .95:
                self.stats[key] = max(self.stats[key], error)
        tracks = owned + self.arm_tracks
        result = []
        for n in tracks:
            parent = self.target.parent[n]
            q = qnorm(qmul(qinverse(final_r[parent]), final_r[n])) if parent in final_r else final_r[n]
            result.extend(round(v, 7) for v in q)
        self.stats["frames"] += 1
        return result

    def samples(self, source_tracks, values, frames, melee=False, sniper=False):
        if len(values) != frames * len(source_tracks) * 4:
            raise ValueError("Source sample length mismatch")
        result = []
        for index in range(frames):
            result.extend(self.sample(source_tracks, values, index, melee, sniper))
        return result

    def firearm_motion(self, source_tracks, motion, inherited_frames=None):
        result = copy.deepcopy(motion)
        frames = inherited_frames or {k: motion[k + "Frames"] for k in ("pose", "fire", "reload")}
        sniper = motion.get("weapon") == 4
        for mode in ("pose", "fire", "reload"):
            result[mode] = self.samples(source_tracks, motion[mode], frames[mode], sniper=sniper)
        if "aimPoses" in motion:
            result["aimPoses"] = [self.firearm_motion(source_tracks, pose, frames) for pose in motion["aimPoses"]]
        if "burstFires" in motion:
            result["burstFires"] = [dict(variant, values=self.samples(source_tracks, variant["values"], frames["fire"], sniper=sniper))
                                    for variant in motion["burstFires"]]
        return result

    def firearms(self, data):
        result = {k: copy.deepcopy(v) for k, v in data.items() if k != "motions"}
        result["bones"] = self.arm_tracks
        result["chestBindRotation"] = qobject(self.target.r["上半身2"])
        result["motions"] = []
        for motion in data["motions"]:
            result["motions"].append(self.firearm_motion(data["bones"], motion))
            print("RETARGET_FIREARM", motion["weapon"], motion["name"], flush=True)
        return result

    def melee(self, data):
        result = {k: copy.deepcopy(v) for k, v in data.items() if k != "clips"}
        result["bones"] = self.torso_tracks + self.arm_tracks
        result["chestBindRotation"] = qobject(self.target.r["上半身2"])
        t, h = self.target, "手首.R"
        f = unit(sub(t.p["中指１.R"], t.p[h]))
        across = sub(t.p["人指１.R"], t.p["小指１.R"])
        x = unit(sub(across, mul(f, dot(across, f))))
        palm = mul(unit(cross(x, f)), -1)
        center = mul(tuple(sum(t.p[d + "１.R"][j] for d in DIGITS[:4]) for j in range(3)), .25)
        center = add(center, add(mul(f, .016 * self.scale), mul(palm, .018 * self.scale)))
        result["gripPosition"] = vobject(qrotate(qinverse(t.r[h]), sub(center, t.p[h])))
        result["gripRotation"] = qobject(qmul(qinverse(t.r[h]), frame(x, f)))
        result["clips"] = []
        for clip in data["clips"]:
            converted = {k: copy.deepcopy(v) for k, v in clip.items() if k != "poses"}
            converted["poses"] = [dict(pose, values=self.samples(data["bones"], pose["values"], pose["frames"], melee=True))
                                  for pose in clip["poses"]]
            result["clips"].append(converted)
            print("RETARGET_MELEE", clip["weapon"], clip["action"], flush=True)
        return result


def qobject(q):
    return dict(zip(("x", "y", "z", "w"), (round(v, 9) for v in qnorm(q))))


def vobject(v):
    return dict(zip(("x", "y", "z"), (round(x, 9) for x in v)))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_library(path, data, source_path, source_bind, target_bind, retargeter, key):
    data["revision"] = hashlib.sha256(json.dumps(data[key], separators=(",", ":"), ensure_ascii=False).encode()).hexdigest()[:12]
    data["retarget"] = {"algorithm": "anatomical-frames-paired-contact-two-bone-v1",
                        "sourceLibrarySha256": digest(source_path),
                        "sourceBindSha256": digest(source_bind), "targetBindSha256": digest(target_bind),
                        "bodyScale": retargeter.scale, "statistics": copy.deepcopy(retargeter.stats)}
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print("WROTE", path.relative_to(ROOT) if path.is_relative_to(ROOT) else path,
          json.dumps(data["retarget"]["statistics"], sort_keys=True), flush=True)


def self_test(source):
    for v in ((1, 0, 0), (.2, -.5, .7)):
        q = axis_angle((.3, .7, .2), .8)
        assert length(sub(qrotate(qinverse(q), qrotate(q, v)), v)) < 1e-10
    for a, b in (((1, 0, 0), (-1, 0, 0)), ((.2, -.3, 1), (.5, .7, -.3))):
        assert length(sub(qrotate(between(a, b), unit(a)), unit(b))) < 1e-9
    p, r = source.evaluate()
    assert max(length(sub(p[n], source.p[n])) for n in source.order) < 1e-7
    assert max(qangle(qmul(r[n], qinverse(source.r[n]))) for n in source.order) < 1e-4
    identity_retarget = Retargeter(source, source)
    q = axis_angle((.1, .7, -.3), 1.1)
    for n in identity_retarget.semantic:
        posed = qmul(q, source.r[n])
        actual = identity_retarget.semantic_rotation(n, {n: posed})
        assert qangle(qmul(actual, qinverse(posed))) < 1e-4
    # An A/T-pose change must still aim the target segment along the source.
    source_frame = frame((1, -.6, 0), (0, 0, 1))
    target_frame = frame((1, 0, 0), (0, 0, 1))
    aligned = qmul(source_frame, qinverse(target_frame))
    assert length(sub(qrotate(aligned, (1, 0, 0)), unit((1, -.6, 0)))) < 1e-9
    print("SELF_TEST_PASS: quaternion algebra, bind FK, identity anatomical transfer, A/T direction alignment")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-bind", type=Path, default=ROOT / "Tools/Blender/DollSingerBind.json")
    parser.add_argument("--target-bind", type=Path, default=ROOT / "Tools/Blender/HatsuneMikuBind.json")
    parser.add_argument("--firearms", type=Path, default=ROOT / "Assets/DollSinger/Animation/Blender/WeaponGestures.json")
    parser.add_argument("--melee", type=Path, default=ROOT / "Assets/DollSinger/Animation/Melee/Resources/MoonkovMeleeGestures.json")
    parser.add_argument("--output", type=Path, default=ROOT / "Assets/HatsuneMiku/Animation")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--only", choices=("firearms", "melee"))
    args = parser.parse_args()
    source = Rig(json.loads(args.source_bind.read_text(encoding="utf-8")))
    if args.self_test:
        self_test(source)
        return
    target = Rig(json.loads(args.target_bind.read_text(encoding="utf-8")))
    protected = {p: digest(p) for p in (args.source_bind, args.target_bind, args.firearms, args.melee)}
    for key, source_path, output_name in (("motions", args.firearms, "WeaponGestures.json"),
                                          ("clips", args.melee, "MeleeGestures.json")):
        if args.only and ((args.only == "firearms") != (key == "motions")):
            continue
        output_path = args.output / output_name
        if output_path.resolve() in {p.resolve() for p in protected}:
            raise ValueError("Output must not overwrite a source library or bind file")
        retargeter = Retargeter(source, target)
        data = json.loads(source_path.read_text(encoding="utf-8"))
        result = retargeter.firearms(data) if key == "motions" else retargeter.melee(data)
        write_library(output_path, result, source_path, args.source_bind, args.target_bind, retargeter, key)
        if retargeter.stats["maximumSniperContactResidualMetres"] > .001:
            print("VALIDATION_REQUIRED: sniper contact residual exceeds 1 mm", flush=True)
        if retargeter.stats["maximumWristSolveErrorMetres"] > .003:
            print("VALIDATION_REQUIRED: wrist solve error exceeds 3 mm", flush=True)
    for path, before in protected.items():
        if digest(path) != before:
            raise RuntimeError("Source changed during bake: " + str(path))


if __name__ == "__main__":
    main()
