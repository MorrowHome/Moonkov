"""Supersample irradiance in bounded UV tiles, retaining the runtime UVs and size.

Run in PreparedWorldResolved.blend, e.g. -- --atlas=26. Results stay separate
from Bakes until visual acceptance. Each tile is a restartable checkpoint.
"""
import argparse, hashlib, json, pathlib, sys, time
import bpy
import numpy as np

SCRIPT = pathlib.Path(__file__).resolve()
PROJECT = SCRIPT.parents[3]
parser = argparse.ArgumentParser()
parser.add_argument('--atlas', type=int, required=True)
parser.add_argument('--scale', type=int, default=8)
parser.add_argument('--tiles', type=int, default=8)
parser.add_argument('--samples', type=int, default=128)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
if not (1 <= args.scale <= 16 and 1 <= args.tiles <= 16 and args.samples > 0):
    raise ValueError('Invalid scale, tile count or sample count')

# Reuse the established source-material bake and compositor implementation.
# Stop before its atlas loop; never execute or overwrite the legacy bake jobs.
helper_path = SCRIPT.with_name('bake_world.py')
helper_text = helper_path.read_text().split("for atlas in report['atlases']:")[0]
if 'def surface_statistics(' not in helper_text:
    raise RuntimeError('Bake helper layout changed; review this entry point')
helper_text = helper_text.replace("[('Image',image),('Normal',normal),('Albedo',albedo)]",
                                  "[('Image',image),('Normal',normal)]")
helper_text = helper_text.replace("'uv_layer':'SourceStaticBake'", "'uv_layer':'SupersampleBake'")
helper_text = helper_text.replace("scene.render.threads_mode='FIXED';scene.render.threads=4",
                                  "scene.render.threads_mode='AUTO'")
helper_text = helper_text.replace("compositor.render.threads_mode='FIXED';compositor.render.threads=4",
                                  "compositor.render.threads_mode='AUTO'")
globals()['__file__'] = str(helper_path)
exec(compile(helper_text, str(helper_path), 'exec'), globals())

atlas = next(a for a in report['atlases'] if a['id'] == args.atlas)
size = atlas['size']
if size % args.tiles:
    raise ValueError('Runtime size must divide evenly into tiles')
step = size // args.tiles
padding = 8
bake_size = (step + 2 * padding) * args.scale
if bake_size > 8192:
    raise ValueError('Tile exceeds the tested 8192 limit; increase tile count')
destination = OUT / f'TiledQuality_s{args.samples}_x{args.scale}'
tile_dir = destination / f'Atlas_{args.atlas:03d}'
tile_dir.mkdir(parents=True, exist_ok=True)
configuration = {'version': 1, 'atlas': args.atlas, 'size': size, 'scale': args.scale,
                 'tiles': args.tiles, 'samples': args.samples, 'padding': padding,
                 'sourceSha256': report['source_sha256'],
                 'preparedSha256': hashlib.sha256(pathlib.Path(bpy.data.filepath).read_bytes()).hexdigest(),
                 'helperSha256': hashlib.sha256(helper_path.read_bytes()).hexdigest()}
obj = scene.objects[atlas['object']]
mesh = obj.data
source_uv = mesh.uv_layers['SourceStaticBake']
uv = mesh.uv_layers.get('SupersampleBake') or mesh.uv_layers.new(name='SupersampleBake')
coordinates = np.empty(len(mesh.loops) * 2, dtype=np.float32)
source_uv.data.foreach_get('uv', coordinates)
coordinates = coordinates.reshape(-1, 2)
source_uv.active_render = True
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
began = time.monotonic()

def save_image(image, path):
    image.save_render(str(path), scene=scene)

def read_pixels(image):
    pixels = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    return pixels.reshape(image.size[1], image.size[0], 4)

for tile in range(args.tiles ** 2):
    stem = tile_dir / f'Tile_{tile:03d}'
    stamp = stem.with_suffix('.json')
    files = {key: stem.with_name(stem.name + '_' + key + '.exr') for key in ('mean', 'raw', 'normal')}
    if stamp.exists() and json.loads(stamp.read_text()).get('configuration') == configuration and all(p.exists() for p in files.values()):
        print('TILED_RESUME', args.atlas, tile, flush=True)
        continue
    x = (tile % args.tiles) * step
    y = (tile // args.tiles) * step
    uv.data.foreach_set('uv', ((coordinates * size - [x - padding, y - padding]) /
                              (step + 2 * padding)).astype(np.float32).ravel())
    mesh.update()
    print('TILED_BEGIN', args.atlas, tile, 'of', args.tiles ** 2, 'size', bake_size, flush=True)
    started = time.monotonic()
    raw = bake(obj, 'TiledRaw', bake_size, 'DIFFUSE', args.samples)
    normal = bake(obj, 'TiledNormal', bake_size, 'EMIT', 1, 'normal')
    high_path = tile_dir / 'CurrentMean.exr'
    denoise(raw, normal, normal, high_path)
    mean = bpy.data.images.load(str(high_path), check_existing=False)
    mean.colorspace_settings.name = 'Non-Color'
    for key, image in [('raw', raw), ('normal', normal), ('mean', mean)]:
        image.scale(step + 2 * padding, step + 2 * padding)
        pixels = read_pixels(image)[padding:padding + step, padding:padding + step].copy()
        image.scale(step, step)
        image.pixels.foreach_set(pixels.ravel())
        save_image(image, files[key])
        bpy.data.images.remove(image)
    temporary = stamp.with_suffix('.json.tmp')
    temporary.write_text(json.dumps({'configuration': configuration, 'seconds': time.monotonic() - started}, indent=2))
    temporary.replace(stamp)
    print('TILED_COMPLETE', args.atlas, tile, 'elapsed', round(time.monotonic() - began, 1), flush=True)

assembled = {}
for key in ('mean', 'raw', 'normal'):
    pixels = np.zeros((size, size, 4), dtype=np.float32)
    for tile in range(args.tiles ** 2):
        image = bpy.data.images.load(str(tile_dir / f'Tile_{tile:03d}_{key}.exr'), check_existing=False)
        x, y = (tile % args.tiles) * step, (tile // args.tiles) * step
        pixels[y:y + step, x:x + step] = read_pixels(image)
        bpy.data.images.remove(image)
    image = bpy.data.images.new('Assembled_' + key, size, size, alpha=True, float_buffer=True)
    image.colorspace_settings.name = 'Non-Color'
    image.pixels.foreach_set(pixels.ravel())
    assembled[key] = image
    suffix = '' if key == 'mean' else '_' + key
    save_image(image, destination / f'Irradiance_{args.atlas:03d}{suffix}.exr')
    del pixels
# Restore the original UV scale for the object-ID guide used by statistics.
uv.data.foreach_set('uv', coordinates.ravel())
mesh.update()
objects = bake(obj, 'TiledObjectGuide', size, 'EMIT', 1, 'objects')
statistics = destination / f'SurfaceStatistics_{args.atlas:03d}.png'
import shutil
shutil.copyfile(OUT / 'Bakes' / f'SourceAO_{args.atlas:03d}.png', destination / f'SourceAO_{args.atlas:03d}.png')
surface_statistics(assembled['raw'], assembled['normal'], objects,
                   destination / f'Irradiance_{args.atlas:03d}.exr', statistics, args.samples * args.scale ** 2)
result = {'configuration': configuration, 'seconds': time.monotonic() - began,
          'sourceSaved': False, 'runtimeSizeUnchanged': True, 'requiresVisualAcceptance': True}
temporary = destination / f'Irradiance_{args.atlas:03d}.json.tmp'
temporary.write_text(json.dumps(result, indent=2))
temporary.replace(destination / f'Irradiance_{args.atlas:03d}.json')
print('TILED_ATLAS_COMPLETE', args.atlas, round(time.monotonic() - began, 1), flush=True)
