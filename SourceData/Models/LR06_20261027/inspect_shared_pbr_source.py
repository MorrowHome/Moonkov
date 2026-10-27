"""Read LR06's shared material graphs and render one source-camera comparison.

Run in Blender background mode with the author file loaded. Never save the blend.
"""
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

base = Path(__file__).resolve().parent
source = base / 'Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend'
assert Path(bpy.data.filepath) == source
out = Path(bpy.path.abspath('//../../../Library/BuildValidation/LR06PBR'))
out.mkdir(parents=True, exist_ok=True)
names = ['R8_Dark_Protective_Steel', 'R8_Scanned_Mineral_Support',
         'R8_Matte_Deck_Plates', 'R8_Restrained_Safety_Ochre',
         'R8_OffWhite_Ceramic_Coat', 'R8_GrayWhite_Panel_Cladding',
         'R8_Rough_Exposed_Steel']


def graph(name):
    mat = bpy.data.materials[name]
    nodes = []
    for node in mat.node_tree.nodes:
        row = {'name': node.name, 'type': node.type}
        if node.type == 'TEX_IMAGE' and node.image:
            row['image'] = node.image.name
            row['colorspace'] = node.image.colorspace_settings.name
            row['size'] = list(node.image.size)
            row['interpolation'] = node.interpolation
            row['extension'] = node.extension
        if node.type == 'UVMAP': row['uv_map'] = node.uv_map
        if node.type == 'MATH':
            row['operation'] = node.operation
            row['inputs'] = [{'value': s.default_value, 'linked': s.is_linked} for s in node.inputs]
        if node.type == 'NORMAL_MAP':
            row['space'] = node.space
            row['strength'] = node.inputs['Strength'].default_value
            row['uv_map'] = node.uv_map
        nodes.append(row)
    return {'name': name, 'nodes': nodes,
            'links': [{'from': link.from_node.name, 'output': link.from_socket.name,
                       'to': link.to_node.name, 'input': link.to_socket.name}
                      for link in mat.node_tree.links]}


# Measure UV0 density on actual triangles of a real building, not bounding boxes.
stats = {name: {'triangles': 0, 'area_m2': 0.0, 'uv_area': 0.0, 'degenerate_uv_triangles': 0} for name in names}
mesh_names = []
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH' or not obj.name.startswith('R6_T06_'): continue
    mesh_names.append(obj.name)
    mesh = obj.data
    layer = mesh.uv_layers.active
    mesh.calc_loop_triangles()
    for tri in mesh.loop_triangles:
        slot = tri.material_index
        if slot >= len(obj.material_slots) or not obj.material_slots[slot].material: continue
        name = obj.material_slots[slot].material.name
        if name not in stats: continue
        record = stats[name]; record['triangles'] += 1
        points = [obj.matrix_world @ mesh.vertices[i].co for i in tri.vertices]
        area = ((points[1] - points[0]).cross(points[2] - points[0])).length / 2
        record['area_m2'] += area
        if layer:
            uv = [layer.data[i].uv for i in tri.loops]
            uv_area = abs((uv[1].x-uv[0].x)*(uv[2].y-uv[0].y)-(uv[1].y-uv[0].y)*(uv[2].x-uv[0].x)) / 2
            record['uv_area'] += uv_area
            if uv_area < 1e-12: record['degenerate_uv_triangles'] += 1
        else: record['degenerate_uv_triangles'] += 1
for record in stats.values():
    record['rms_texels_per_metre_at_1024'] = (1024 * math.sqrt(record['uv_area'] / record['area_m2'])
                                               if record['area_m2'] else None)

scene = bpy.context.scene
report = {'scope': 'read-only author source; game display and lights are separate',
          'blender_version': bpy.app.version_string, 'engine': scene.render.engine,
          'view_transform': scene.view_settings.view_transform, 'look': scene.view_settings.look,
          'exposure': scene.view_settings.exposure, 'gamma': scene.view_settings.gamma,
          'use_compositor': scene.use_nodes, 'source_lights': sum(o.type == 'LIGHT' for o in scene.objects),
          'building': 'R6_T06', 'building_meshes': mesh_names, 'uv_triangle_stats': stats,
          'materials': [graph(name) for name in names]}
(out / 'BlenderSourcePBR.json').write_text(json.dumps(report, ensure_ascii=False, indent=2))
(base / 'FullPreviewStaging/BlenderSourcePBR.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
print('PBR_SOURCE_DIAGNOSTICS', json.dumps({k: v for k, v in report.items() if k not in ('materials', 'building_meshes')}, ensure_ascii=False), flush=True)
if '--render' in sys.argv or '--ray' in sys.argv:
    camera_data = bpy.data.cameras.new('LR06_PBR_COMPARISON_CAMERA_TEMP')
    camera = bpy.data.objects.new('LR06_PBR_COMPARISON_CAMERA_TEMP', camera_data)
    scene.collection.objects.link(camera)
    camera.location = Vector((1840, 2255, 255))
    target = Vector((1873, 2298, 240))
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data.sensor_fit = 'VERTICAL'
    camera_data.lens_unit = 'FOV'
    camera_data.angle = math.radians(55)
    camera_data.clip_start = .1
    camera_data.clip_end = 5000
    scene.camera = camera
    bpy.context.view_layer.update()
    if '--ray' in sys.argv:
        depsgraph = bpy.context.evaluated_depsgraph_get()
        ray_results = []
        for x, y in ((.5, .5), (.45, .48), (.55, .42)):
            scale = math.tan(math.radians(55) / 2)
            ray = camera.matrix_world.to_quaternion() @ Vector(((2*x-1)*(960/600)*scale, (2*y-1)*scale, -1)).normalized()
            hit, location, normal, face, obj, _ = scene.ray_cast(depsgraph, camera.location, ray, distance=5000)
            material = None
            hit_uv = None
            if hit and obj.type == 'MESH':
                evaluated = obj.evaluated_get(depsgraph)
                slot = evaluated.data.polygons[face].material_index
                if slot < len(evaluated.material_slots) and evaluated.material_slots[slot].material:
                    material = evaluated.material_slots[slot].material.name
                mesh = evaluated.data
                uv_layer = mesh.uv_layers.get('UVMap')
                if uv_layer:
                    mesh.calc_loop_triangles()
                    local_point = evaluated.matrix_world.inverted() @ location
                    for tri in mesh.loop_triangles:
                        if tri.polygon_index != face: continue
                        a, b, c = (mesh.vertices[i].co for i in tri.vertices)
                        v0, v1, v2 = b-a, c-a, local_point-a
                        d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
                        d20, d21 = v2.dot(v0), v2.dot(v1)
                        denominator = d00*d11-d01*d01
                        if abs(denominator) < 1e-12: continue
                        w1 = (d11*d20-d01*d21)/denominator
                        w2 = (d00*d21-d01*d20)/denominator
                        w0 = 1-w1-w2
                        if min(w0, w1, w2) >= -1e-4:
                            uvs = [uv_layer.data[i].uv for i in tri.loops]
                            hit_uv = list(uvs[0]*w0 + uvs[1]*w1 + uvs[2]*w2)
                            break
            ray_result = {'xy': [x, y], 'hit': hit, 'object': obj.name if hit else None,
                          'material': material, 'location': list(location), 'normal': list(normal), 'uv': hit_uv}
            ray_results.append(ray_result)
            print('SOURCE_RAY', json.dumps(ray_result), flush=True)
        (out / 'BlenderR6Rays.json').write_text(json.dumps(ray_results, ensure_ascii=False, indent=2) + '\n')
    if '--render' not in sys.argv:
        sys.exit(0)
    scene.render.resolution_x = 960
    scene.render.resolution_y = 600
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(out / 'BlenderR6T06.png')
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 4
    print('PBR_SOURCE_RENDER_START', scene.render.filepath, 'fov_y', camera_data.angle_y, flush=True)
    bpy.ops.render.render(write_still=True)
    print('PBR_SOURCE_RENDER_DONE', scene.render.filepath, flush=True)
