"""Read-only audit of coincident runtime triangles, including cross-chunk copies.

Run with Blender --background --factory-startup --python (uses bundled NumPy).
This checks identical vertex triplets at 1 mm tolerance, not arbitrary overlaps.
"""
import collections
import functools
import json
import pathlib
import struct
import time
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent
PROJECT = ROOT.parents[2]
WORK = PROJECT / 'LocalData/SourceFidelity20261028/World'
world = json.loads((WORK / 'WorldGeometry.json').read_text())
chunks = {row['id']: row for row in world['chunks']}
tolerance = .001
started = time.monotonic()

@functools.lru_cache(maxsize=24)
def geometry(index):
    row = chunks[index]
    raw = (PROJECT / row['file']).read_bytes()
    if raw[:4] != b'SMW1' or struct.unpack_from('<II', raw, 4) != (row['vertices'], row['indices']):
        raise ValueError(f'Invalid chunk header {index}')
    vertices = np.frombuffer(raw, dtype='<f4', count=row['vertices'] * 34, offset=12).reshape(-1, 34)
    faces = np.frombuffer(raw, dtype='<u4', offset=12 + row['vertices'] * 136).reshape(-1, 3)
    origin = np.array([row['position'][k] for k in 'xyz'], dtype=np.float64)
    quantized = np.rint((vertices[:, :3].astype(np.float64) + origin) / tolerance).astype(np.int64)
    return quantized, faces

dtype = np.dtype([('hash', '<u8'), ('chunk', '<u4'), ('face', '<u4'), ('winding', 'u1')])
parts = []
for row in world['chunks']:
    vertices, faces = geometry(row['id'])
    q = vertices[faces]
    order = np.lexsort((q[:, :, 2], q[:, :, 1], q[:, :, 0]), axis=1)
    canonical = q[np.arange(len(q))[:, None], order].reshape(-1, 9)
    hashes = np.full(len(faces), 14695981039346656037, dtype=np.uint64)
    for column in range(9):
        hashes ^= canonical[:, column].astype(np.uint64)
        hashes *= np.uint64(1099511628211)
    inversions = ((order[:, 0] > order[:, 1]).astype(np.uint8) +
                  (order[:, 0] > order[:, 2]) + (order[:, 1] > order[:, 2])) % 2
    records = np.empty(len(faces), dtype=dtype)
    records['hash'] = hashes
    records['chunk'] = row['id']
    records['face'] = np.arange(len(faces), dtype=np.uint32)
    records['winding'] = inversions
    parts.append(records)
records = np.concatenate(parts)
del parts
records.sort(order='hash')
starts = np.r_[0, np.flatnonzero(records['hash'][1:] != records['hash'][:-1]) + 1]
ends = np.r_[starts[1:], len(records)]
counts = collections.Counter()
examples = []
for begin, end in zip(starts[ends - starts > 1], ends[ends - starts > 1]):
    # Verify actual canonical coordinates, so hash collisions are never findings.
    exact = collections.defaultdict(list)
    for record in records[begin:end]:
        vertices, faces = geometry(int(record['chunk']))
        q = vertices[faces[int(record['face'])]]
        q = q[np.lexsort((q[:, 2], q[:, 1], q[:, 0]))]
        exact[q.tobytes()].append(record)
    for group in exact.values():
        if len(group) < 2:
            continue
        counts['coincidentGroups'] += 1
        counts['extraTriangles'] += len(group) - 1
        orientation = collections.Counter(int(r['winding']) for r in group)
        same_facing = any(n > 1 for n in orientation.values())
        counts['sameFacingGroups' if same_facing else 'oppositeFacingOnlyGroups'] += 1
        counts['crossChunkGroups'] += len({int(r['chunk']) for r in group}) > 1
        if len(examples) < 100:
            examples.append({'sameFacing': same_facing,
                             'triangles': [{'chunk': int(r['chunk']), 'face': int(r['face']),
                                            'material': chunks[int(r['chunk'])]['material']}
                                           for r in group]})
report = {'sourceSha256': world['sourceSha256'], 'triangles': len(records),
          'toleranceMeters': tolerance, 'seconds': time.monotonic() - started,
          'scope': 'Identical vertex triplets only; arbitrary coplanar overlaps are not covered.',
          'geometryChanged': False, 'counts': dict(counts), 'examples': examples}
(WORK / 'DuplicateTriangleAudit.json').write_text(json.dumps(report, indent=2))
print('DUPLICATE_TRIANGLE_AUDIT', json.dumps({k: v for k, v in report.items() if k != 'examples'}), flush=True)
