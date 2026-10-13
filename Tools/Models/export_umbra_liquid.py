"""Blender 5.1: export the authored continuous skin, not its hidden construction parts.

Run with --factory-startup --background --disable-autoexec SOURCE.blend --python
this_file -- OUTPUT.bytes. Source axes are already Unity's literal +Y up/+Z forward;
there is deliberately no FBX axis conversion. Original .blend is never saved/modified.
The little-endian UML2 payload is read by LiquidSpiderPresentation.
"""
import bpy
import numpy as np
import os
import struct
import sys
import json

output = sys.argv[sys.argv.index('--') + 1]
skin = bpy.data.objects['V4_LiquidSkin_ConstrictedMantle']
root_inverse = bpy.data.objects['SPREAD__ROOT'].matrix_world.inverted()
rest = [np.zeros(3)]
for leg in range(1, 9):
    curve = bpy.data.objects[f'V4_SOURCE_L{leg:02d}_Bezier']
    points = curve.data.splines[0].bezier_points
    assert len(points) == 13, 'The exporter requires hip/knee/toe at curve indices 1/6/11.'
    matrix = root_inverse @ curve.matrix_world
    rest.extend(np.array(matrix @ p.co, dtype=np.float64) for p in points[1:12])
rest = np.array(rest)

# A runtime derivative; retain the editable high-poly source beside the project.
copy = skin.copy()
copy.data = skin.data.copy()
bpy.context.scene.collection.objects.link(copy)
copy.hide_set(False)
bpy.context.view_layer.objects.active = copy
copy.select_set(True)
modifier = copy.modifiers.new('Runtime budget / continuous surface', 'DECIMATE')
modifier.ratio = 0.075
bpy.ops.object.modifier_apply(modifier=modifier.name)
mesh = copy.data
mesh.calc_loop_triangles()
matrix = root_inverse @ copy.matrix_world
normal_matrix = matrix.to_3x3().inverted().transposed()
vertices = np.array([matrix @ v.co for v in mesh.vertices], dtype=np.float64)
normals = np.array([(normal_matrix @ v.normal).normalized() for v in mesh.vertices])
triangles = np.array([t.vertices[:] for t in mesh.loop_triangles], dtype='<i4')

# Closest point on each authored centreline. Blend the mantle into the nearest
# two spine samples; continuous weights keep the fused coxal folds continuous.
count = len(vertices)
best_distance = np.full(count, np.inf)
bone_a = np.zeros(count, dtype=np.int32)
bone_b = bone_a.copy()
factor = np.zeros(count)
for leg in range(8):
    start = 1 + leg * 11
    for sample in range(10):
        a, b = rest[start + sample:start + sample + 2]
        delta = b - a
        t = np.clip((vertices - a) @ delta / np.dot(delta, delta), 0, 1)
        distance = np.sum((vertices - a - t[:, None] * delta) ** 2, axis=1)
        nearer = distance < best_distance
        best_distance[nearer] = distance[nearer]
        bone_a[nearer] = start + sample
        bone_b[nearer] = start + sample + 1
        factor[nearer] = t[nearer]

radius = np.linalg.norm(vertices[:, [0, 2]], axis=1)
transition = np.clip((radius - .32) / .40, 0, 1)
limb_weight = transition * transition * (3 - 2 * transition)
# The raised hollow mantle belongs to the body, even above a nearby leg spine.
height_mask = np.clip((vertices[:, 1] - .05) / .18, 0, 1)
limb_weight *= 1 - height_mask * np.clip((.94 - radius) / .22, 0, 1)
bone_indices = np.stack([np.zeros(count, dtype=np.int32), bone_a, bone_b, np.zeros(count, dtype=np.int32)], axis=1)
weights = np.stack([1 - limb_weight, limb_weight * (1 - factor), limb_weight * factor, np.zeros(count)], axis=1)
# Conservative cross-section probes for bounded runtime tissue compression.
# The spine remains pinned; uniform sample scaling shrinks the blended tube
# around its centreline without moving the gait's hip/knee/contact endpoints.
radii = np.full(len(rest), .025)
body = vertices[limb_weight < .15]
radii[0] = np.max(np.linalg.norm(body, axis=1))
for node in range(1, len(rest)):
    region = (limb_weight > .45) & (((bone_a == node) & (factor < .9)) | ((bone_b == node) & (factor > .1)))
    if np.any(region):
        radii[node] = min(.32, np.max(np.sqrt(best_distance[region])) + .012)
# Vertex red is a flow envelope. Pin the final contact collar; no shader toe slide.
toe_distance = np.min(np.stack([np.linalg.norm(vertices - rest[11 + i * 11], axis=1) for i in range(8)]), axis=0)
flow = np.clip((toe_distance - .08) / .20, 0, 1)

os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)
with open(output, 'wb') as stream:
    stream.write(b'UML2')
    stream.write(struct.pack('<iii', count, len(triangles) * 3, len(rest)))
    for array in (rest, radii, vertices, normals, flow):
        stream.write(np.asarray(array, dtype='<f4').tobytes())
    stream.write(triangles.tobytes())
    stream.write(bone_indices.astype('<i4').tobytes())
    stream.write(weights.astype('<f4').tobytes())
print(json.dumps(dict(output=output, vertices=count, triangles=len(triangles), bones=len(rest),
                      bounds=[vertices.min(axis=0).tolist(), vertices.max(axis=0).tolist()],
                      bytes=os.path.getsize(output))))
