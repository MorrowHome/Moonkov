"""Strict provenance for resumable source-radiance captures (no Blender imports)."""
import hashlib
import json
import math
from pathlib import Path


def file_sha256(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def lighting_revision(source_sha, blend_sha, profile):
    payload = {'version': 1, 'sourceSha256': source_sha,
               'inputBlendSha256': blend_sha, 'profile': profile}
    return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def capture_ready(project, row, source_sha, revision):
    if not revision:
        return False
    path = Path(project) / row['file']
    try:
        stamp = json.loads(path.with_suffix('.json').read_text(encoding='utf-8'))
        if (stamp.get('sourceSha256') != source_sha or
                stamp.get('lightingRevision') != revision or stamp.get('id') != row['id']):
            return False
        for key in 'xyz':
            value = stamp['position'][key]
            if not math.isfinite(value) or abs(value - row['position'][key]) >= .0001:
                return False
        # OpenEXR magic prevents a JSON stamp plus an empty/interrupted file from resuming.
        with path.open('rb') as stream:
            if path.stat().st_size <= 16 or stream.read(4) != b'\x76\x2f\x31\x01':
                return False
        return stamp.get('radianceSha256') == file_sha256(path)
    except (OSError, ValueError, TypeError, KeyError):
        return False
