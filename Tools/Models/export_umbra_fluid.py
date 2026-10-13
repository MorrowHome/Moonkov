"""Blender Python: fill the exported Umbra solid with deterministic fluid seeds.

Run --factory-startup --background --disable-autoexec --python this_file --
Umbra08Liquid.bytes Umbra08Fluid.bytes. No source text or author script is run.
"""
import sys, struct, os, json
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

source, output = sys.argv[sys.argv.index('--') + 1:][:2]
with open(source, 'rb') as stream:
    assert stream.read(4) == b'UML2'
    n, indices, bone_count = struct.unpack('<iii', stream.read(12))
    rest = np.frombuffer(stream.read(bone_count * 12), dtype='<f4').reshape(-1, 3).copy()
    stream.read(bone_count * 4)
    vertices = np.frombuffer(stream.read(n * 12), dtype='<f4').reshape(-1, 3).copy()
    stream.read(n * 12 + n * 4)
    triangles = np.frombuffer(stream.read(indices * 4), dtype='<i4').reshape(-1, 3).copy()
tree = BVHTree.FromPolygons([Vector(v) for v in vertices], triangles.tolist(), all_triangles=True)
minimum, maximum = vertices.min(axis=0), vertices.max(axis=0)
spacing = .05
seeds = []
for y in np.arange(minimum[1] + spacing * .37, maximum[1], spacing):
    for z in np.arange(minimum[2] + spacing * .41, maximum[2], spacing):
        x = float(minimum[0] - .1)
        hits = []
        for _ in range(48):
            hit, normal, face, distance = tree.ray_cast(Vector((x, y, z)), Vector((1, 0, 0)), float(maximum[0] - x + .1))
            if hit is None:
                break
            hits.append(hit.x)
            x = hit.x + .00003
        for a, b in zip(hits[::2], hits[1::2]):
            # Centred intervals also seed the thin tapering extremities.
            count = max(1, int((b - a) / spacing))
            for j in range(count):
                seeds.append((a + (j + .5) * (b - a) / count, y, z))
seeds = np.array(seeds, dtype=np.float64)
count = len(seeds)
best = np.full(count, np.inf)
bone_a = np.zeros(count, dtype=np.int32)
bone_b = bone_a.copy()
factor = np.zeros(count)
for leg in range(8):
    for sample in range(10):
        node = 1 + leg * 11 + sample
        a, b = rest[node:node + 2]
        delta = b - a
        t = np.clip((seeds - a) @ delta / np.dot(delta, delta), 0, 1)
        distance = np.sum((seeds - a - t[:, None] * delta) ** 2, axis=1)
        near = distance < best
        best[near] = distance[near]; bone_a[near] = node; bone_b[near] = node + 1; factor[near] = t[near]
radius = np.linalg.norm(seeds[:, [0, 2]], axis=1)
t = np.clip((radius - .32) / .40, 0, 1)
limb = t * t * (3 - 2 * t)
limb *= 1 - np.clip((seeds[:, 1] - .05) / .18, 0, 1) * np.clip((.94 - radius) / .22, 0, 1)
bones = np.stack([np.zeros(count, dtype=np.int32), bone_a, bone_b, np.zeros(count, dtype=np.int32)], axis=1)
weights = np.stack([1 - limb, limb * (1 - factor), limb * factor, np.zeros(count)], axis=1)
# Redistribute the fixed particle budget: the sculpt's body/upper legs contain
# almost all of its volume, while its distal legs are too thin for a fluid field.
# Keep a thinner mantle, lightly reduce upper-leg mass, and explicitly seed
# continuous lower-leg tubes. This changes fluid allocation, not the old skin.
body_region = weights[:, 0] > .5
lower_region = ~body_region & (((bone_a - 1) % 11) >= 5)
upper_region = ~body_region & ~lower_region
random = np.random.default_rng(8).random(count)
keep = (body_region & (random < .45)) | (upper_region & (random < .85))
seeds[body_region] *= np.array([.84, .62, .84])
seeds, bones, weights = seeds[keep], bones[keep], weights[keep]
base_count = len(seeds)
extra_positions, extra_bones, extra_weights = [], [], []
for leg in range(8):
    first = 1 + leg * 11 + 5
    points = rest[first:first + 6]
    lengths = np.linalg.norm(np.diff(points, axis=0), axis=1)
    cumulative = np.concatenate(([0.], np.cumsum(lengths)))
    rings = int(np.ceil(cumulative[-1] / spacing)) + 1
    for ring in range(rings):
        distance = cumulative[-1] * ring / (rings - 1)
        segment = min(4, np.searchsorted(cumulative, distance, side='right') - 1)
        t = (distance - cumulative[segment]) / lengths[segment]
        centre = points[segment] * (1 - t) + points[segment + 1] * t
        axis = (points[segment + 1] - points[segment]) / lengths[segment]
        tangent = np.cross(axis, np.array([0., 1., 0.]))
        if np.linalg.norm(tangent) < .01:
            tangent = np.cross(axis, np.array([1., 0., 0.]))
        tangent /= np.linalg.norm(tangent)
        bitangent = np.cross(axis, tangent)
        u = ring / (rings - 1)
        radius = .108 * (1 - u) + .078 * u
        # Offset square lattice gives each cross-section a solid fluid volume,
        # including the toe, rather than a sparse ring of surface particles.
        for x in np.arange(-radius + spacing * .4, radius, spacing):
            for y in np.arange(-radius + spacing * .4, radius, spacing):
                if x*x + y*y > radius*radius:
                    continue
                extra_positions.append(centre + tangent * x + bitangent * y)
                extra_bones.append([0, first + segment, first + segment + 1, 0])
                extra_weights.append([0., 1 - t, t, 0.])
seeds = np.concatenate([seeds, np.array(extra_positions)])
bones = np.concatenate([bones, np.array(extra_bones)])
weights = np.concatenate([weights, np.array(extra_weights)])
count = len(seeds)
os.makedirs(os.path.dirname(output), exist_ok=True)
with open(output, 'wb') as stream:
    stream.write(b'UFP1'); stream.write(struct.pack('<i', count))
    stream.write(seeds.astype('<f4').tobytes()); stream.write(bones.astype('<i4').tobytes()); stream.write(weights.astype('<f4').tobytes())
print(json.dumps(dict(particles=count, spacing=spacing, retained_body_upper=base_count,
                      lower_leg_particles=len(extra_positions), bytes=os.path.getsize(output), output=output)))
