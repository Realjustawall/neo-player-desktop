from __future__ import annotations

import hashlib
import sys
import tempfile
import time
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'python'))

from ai_runtime import AIRuntimeManager


class AIRuntimeManagerTests(unittest.TestCase):
    def test_verified_runtime_is_installed_on_demand(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            archive = root / 'runtime.zip'
            executable = 'NEOLyricsAI.exe' if sys.platform == 'win32' else 'NEOLyricsAI'
            with zipfile.ZipFile(archive, 'w') as package:
                package.writestr(executable, b'optional runtime')
            digest = hashlib.sha256(archive.read_bytes()).hexdigest()
            Path(str(archive) + '.sha256').write_text(f'{digest}  runtime.zip', encoding='ascii')
            manager = AIRuntimeManager(root / 'installed', archive.as_uri())
            manager.install()
            deadline = time.time() + 5
            while manager.status()['busy'] and time.time() < deadline:
                time.sleep(.02)
            status = manager.status()
            self.assertTrue(status['installed'])
            self.assertEqual('installed', status['phase'])

    def test_rejects_unsupported_model_before_starting_worker(self):
        with tempfile.TemporaryDirectory() as tmp:
            manager = AIRuntimeManager(Path(tmp) / 'runtime')
            manager.executable.parent.mkdir(parents=True)
            manager.executable.write_bytes(b'placeholder')
            with self.assertRaises(ValueError):
                manager.ensure_model('large-v3')


if __name__ == '__main__':
    unittest.main()
