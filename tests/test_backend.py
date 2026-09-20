import json
import struct
import sys
import tempfile
import unittest
import urllib.request
import wave
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'python'))
import sqlite_close  # noqa: F401
from backend import NeoStore, create_server


class BackendTests(unittest.TestCase):
    def make_wav(self, path: Path, seconds: float = .2):
        frames = max(1, int(44100 * seconds))
        with wave.open(str(path), 'wb') as file:
            file.setnchannels(2); file.setsampwidth(2); file.setframerate(44100)
            file.writeframes(struct.pack('<hh', 0, 0) * frames)

    def test_scan_duration_filter_and_library_features(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); music = root / 'Music'; music.mkdir(); self.make_wav(music / 'Demo.wav', .2)
            store = NeoStore(root / 'data'); store.add_folder(str(music))
            result = store.scan(); self.assertEqual(1, result['skippedShort']); self.assertEqual([], store.library())
            store.patch_settings({'minDurationMs': 0, 'accent': 'blue', 'language': 'fa'})
            result = store.scan(); self.assertEqual(1, result['found']); songs = store.library(); self.assertEqual(1, len(songs)); song = songs[0]
            self.assertEqual('Demo', song['title']); store.set_favorite(song['id'], True); self.assertTrue(store.library('', True)[0]['favorite'])
            store.add_history(song['id']); self.assertEqual(song['id'], store.history()[0]['id'])
            self.assertEqual(1, store.stats()['songs'])

    def test_playlist_folders_lyrics_profiles_backup_and_queue(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); music = root / 'Music'; music.mkdir(); self.make_wav(music / 'Demo.wav')
            store = NeoStore(root / 'data'); store.patch_settings({'minDurationMs': 0}); store.add_folder(str(music)); store.scan(); song = store.library()[0]
            folder = store.create_playlist_folder('Mixes'); playlist = store.create_playlist('Offline', folder['id']); store.add_playlist_song(playlist['id'], song['id'])
            loaded = store.playlist(playlist['id']); self.assertEqual(folder['id'], loaded['folderId']); self.assertEqual(song['id'], loaded['songs'][0]['id'])
            store.patch_playlist(playlist['id'], {'pinned': True, 'sortMode': 'title'}); self.assertTrue(store.playlists()[0]['pinned'])
            store.set_queue([song['id']]); self.assertEqual(song['id'], store.queue()[0]['id'])
            saved = store.save_lyrics(song['id'], {'plain': 'hello', 'lrc': '[00:00.00]hello'}); self.assertIn('hello', saved['lrc'])
            profile = store.save_track_profile(song['id'], {'accent': '#45D483', 'visualizer': 'waveform'}); self.assertEqual('waveform', profile['visualizer'])
            mix = store.smart_mix(None, 10); self.assertTrue(isinstance(mix, list))
            backup = store.backup(); self.assertEqual(2, backup['version']); self.assertIn('settings', backup)

    def test_http_health_media_and_new_endpoints(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); web = root / 'dist'; web.mkdir(); (web / 'index.html').write_text('<div id="root"></div>', encoding='utf-8')
            music = root / 'Music'; music.mkdir(); self.make_wav(music / 'Demo.wav')
            store = NeoStore(root / 'data'); store.patch_settings({'minDurationMs': 0}); store.add_folder(str(music)); store.scan(); song = store.library()[0]; server = create_server(store, web)
            try:
                with urllib.request.urlopen(server.url + '/api/health') as response:
                    payload = json.loads(response.read()); self.assertTrue(payload['ok']); self.assertEqual(2, payload['version'])
                with urllib.request.urlopen(server.url + '/api/settings') as response:
                    self.assertIn('minDurationMs', json.loads(response.read()))
                request = urllib.request.Request(server.url + f"/media/{song['id']}", headers={'Range':'bytes=0-31'})
                with urllib.request.urlopen(request) as response: self.assertEqual(206, response.status); self.assertEqual(32, len(response.read()))
            finally: server.stop()


if __name__ == '__main__': unittest.main()
