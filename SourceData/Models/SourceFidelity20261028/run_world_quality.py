"""Run remaining interior atlases, then source reflections, one GPU job at a time.

Requires prior acceptance of atlas 26. Candidates remain outside Unity Assets.
Run with ordinary Python and --blender /absolute/path/to/blender.exe.
"""
import argparse
import hashlib
import json
import pathlib
import subprocess
import time

ROOT = pathlib.Path(__file__).resolve().parent
PROJECT = ROOT.parents[2]
WORK = PROJECT / 'LocalData/SourceFidelity20261028/World'
parser = argparse.ArgumentParser()
parser.add_argument('--blender', type=pathlib.Path, required=True)
args = parser.parse_args()
if not args.blender.is_file():
    raise FileNotFoundError(args.blender)
prepared = WORK / 'PreparedWorldResolved.blend'
source_hash = json.loads((ROOT / 'SourceSummary.json').read_text())['source_sha256']
prepared_hash = hashlib.sha256(prepared.read_bytes()).hexdigest()
helper_hash = hashlib.sha256((ROOT / 'bake_world.py').read_bytes()).hexdigest()
quality = WORK / 'TiledQuality_s128_x8'
state_path = WORK / 'WindowsQualityQueue.json'
started = time.time()

def state(phase, **fields):
    row = {'phase': phase, 'started': started, 'updated': time.time(), **fields}
    temporary = state_path.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(row, indent=2))
    temporary.replace(state_path)
    print('QUALITY_QUEUE', json.dumps(row), flush=True)

def verify_atlas(index):
    row = json.loads((quality / f'Irradiance_{index:03d}.json').read_text())
    c = row['configuration']
    expected = {'atlas': index, 'size': 4096, 'samples': 128, 'scale': 8, 'tiles': 8,
                'sourceSha256': source_hash, 'preparedSha256': prepared_hash,
                'helperSha256': helper_hash}
    if any(c.get(k) != v for k, v in expected.items()):
        raise ValueError(f'Atlas {index} configuration differs from the accepted candidate')
    for name in (f'Irradiance_{index:03d}.exr', f'SurfaceStatistics_{index:03d}.png'):
        if not (quality / name).is_file() or (quality / name).stat().st_size == 0:
            raise ValueError('Missing output ' + name)

def blender_job(script, arguments, log):
    command = [str(args.blender), '--background', str(prepared), '--python-exit-code', '10',
               '--python', str(ROOT / script), '--', *arguments]
    with log.open('a', encoding='utf-8') as stream:
        subprocess.run(command, cwd=PROJECT, stdout=stream, stderr=subprocess.STDOUT, check=True,
                       creationflags=getattr(subprocess, 'ABOVE_NORMAL_PRIORITY_CLASS', 0))

try:
    verify_atlas(26)
    for index in (24, 25):
        if (quality / f'Irradiance_{index:03d}.json').is_file():
            verify_atlas(index)
            state('atlas_ready', atlas=index, resumed=True)
            continue
        state('baking', atlas=index)
        blender_job('bake_world_tiled.py', [f'--atlas={index}'],
                    WORK.parent / f'TiledQuality{index}.log')
        verify_atlas(index)
    state('capturing_reflections')
    plan = json.loads((WORK / 'WorldReflectionPlan.json').read_text())
    if (plan.get('sourceSha256') != source_hash or plan.get('positionVersion') != 3
            or plan.get('boundsVersion') != 2):
        raise ValueError('Reflection plan is not the reviewed source/position/bounds version')
    blender_job('capture_world_reflections.py', ['--resolved'], WORK.parent / 'WindowsReflections.log')
    from reflection_capture_contract import capture_ready
    plan = json.loads((WORK / 'WorldReflectionPlan.json').read_text())
    for probe in plan['probes']:
        if not capture_ready(PROJECT, probe, source_hash, plan.get('lightingRevision')):
            raise ValueError(f'Stale/incomplete reflection lighting {probe["id"]}')
        path = PROJECT / probe['file']
        row = json.loads(path.with_suffix('.json').read_text())
        if not path.is_file() or row.get('sourceSha256') != source_hash:
            raise ValueError(f'Invalid reflection {probe["id"]}')
        if any(abs(row['position'][k] - probe['position'][k]) >= .0001 for k in 'xyz'):
            raise ValueError(f'Stale reflection position {probe["id"]}')
    state('candidates_ready_for_review', atlases=[24, 25, 26], reflections=len(plan['probes']),
          imported=False, formalSceneInstalled=False)
except Exception as error:
    state('failed', error=str(error))
    raise
