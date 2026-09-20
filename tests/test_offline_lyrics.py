import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'python'))
from offline_lyrics import OfflineLyricsEngine, lrc_timestamp, segments_to_lrc


class OfflineLyricsTests(unittest.TestCase):
    def test_lrc_formatting(self):
        self.assertEqual('[00:01.25]', lrc_timestamp(1.25))
        self.assertEqual('[61:01.50]', lrc_timestamp(3661.5))
        self.assertEqual('[00:00.00]سلام\n[01:02.40]Hello', segments_to_lrc([
            {'start': 0, 'text': ' سلام '}, {'start': 62.4, 'text': 'Hello'}]))

    def test_transcription_uses_local_model_and_word_timestamps(self):
        calls = {}
        class FakeModel:
            def __init__(self, path, **kwargs): calls['init'] = (path, kwargs)
            def transcribe(self, path, **kwargs):
                calls['transcribe'] = (path, kwargs)
                word = SimpleNamespace(start=.1, end=.4, word='Hello')
                segment = SimpleNamespace(start=.1, end=.7, text=' Hello world ', words=[word])
                return iter([segment]), SimpleNamespace(language='en', language_probability=.99, duration=.7)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); audio = root / 'song.wav'; audio.write_bytes(b'RIFF'); model = root / 'model'; model.mkdir()
            result = OfflineLyricsEngine(FakeModel).transcribe(audio, model)
        self.assertEqual('en', result.language); self.assertIn('[00:00.10]Hello world', result.lrc)
        self.assertTrue(calls['init'][1]['local_files_only']); self.assertTrue(calls['transcribe'][1]['word_timestamps'])


if __name__ == '__main__': unittest.main()
