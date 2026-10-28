"""Small provenance regressions; no render or production cache writes."""
import json
import pathlib
import tempfile
import unittest
from reflection_capture_contract import capture_ready, lighting_revision, file_sha256


class ReflectionContractTests(unittest.TestCase):
    def test_lighting_change_invalidates_same_source_and_position(self):
        old = lighting_revision('source', 'old-prepared-blend', {'samples': 32})
        new = lighting_revision('source', 'new-prepared-blend', {'samples': 32})
        self.assertNotEqual(old, new)
        with tempfile.TemporaryDirectory() as directory:
            row = {'id': 4, 'file': 'probe.exr', 'position': dict(x=1., y=2., z=3.)}
            path = pathlib.Path(directory) / row['file']
            path.write_bytes(b'\x76\x2f\x31\x01' + b'payload' * 4)
            stamp = dict(row, sourceSha256='source', lightingRevision=old, radianceSha256=file_sha256(path))
            path.with_suffix('.json').write_text(json.dumps(stamp))
            self.assertTrue(capture_ready(directory, row, 'source', old))
            self.assertFalse(capture_ready(directory, row, 'source', new))
            self.assertFalse(capture_ready(directory, row, 'different-source', old))
            self.assertFalse(capture_ready(directory, row, 'source', ''))
            path.write_bytes(b'\x76\x2f\x31\x01' + b'changed' * 4)
            self.assertFalse(capture_ready(directory, row, 'source', old))
            path.write_bytes(b'\x76\x2f\x31\x01' + b'payload' * 4)
            stamp['position'] = dict(x=float('nan'), y=2., z=3.)
            path.with_suffix('.json').write_text(json.dumps(stamp))
            self.assertFalse(capture_ready(directory, row, 'source', old))
            path.with_suffix('.json').write_text('{')
            self.assertFalse(capture_ready(directory, row, 'source', old))
            path.with_suffix('.json').write_text(json.dumps(dict(row, sourceSha256='source', lightingRevision=old)))
            path.write_bytes(b'')
            self.assertFalse(capture_ready(directory, row, 'source', old))


if __name__ == '__main__':
    unittest.main()
