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
    def make_wav(self, path: Path):
        with wave.open(str(path), 'wb') as file:
            file.setnchannels(2); file.setsampwidth(2); file.setframerate(44100)
            file.writeframes(struct.pack('<hh', 0, 0) * 4410)

    def test_scan_favorite_playlist_queue_and_settings(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); music = root / 'Music'; music.mkdir(); self.make_wav(music / 'Demo.wav')
            store = NeoStore(root / 'data'); store.add_folder(str(music)); result = store.scan()
            self.assertEqual(1, result['found']); songs = store.library(); self.assertEqual(1, len(songs)); song = songs[0]
            self.assertEqual('Demo', song['title']); store.set_favorite(song['id'], True); self.assertTrue(store.library('', True)[0]['favorite'])
            playlist = store.create_playlist('Offline'); store.add_playlist_song(playlist['id'], song['id']); self.assertEqual(song['id'], store.playlist(playlist['id'])['songs'][0]['id'])
            store.set_queue([song['id']]); self.assertEqual(song['id'], store.queue()[0]['id'])
            settings = store.patch_settings({'theme':'sunset', 'volume':0.4}); self.assertEqual('sunset', settings['theme']); self.assertEqual(0.4, settings['volume'])

    def test_http_health_and_media_range(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); web = root / 'dist'; web.mkdir(); (web / 'index.html').write_text('<div id="root"></div>', encoding='utf-8')
            music = root / 'Music'; music.mkdir(); self.make_wav(music / 'Demo.wav')
            store = NeoStore(root / 'data'); store.add_folder(str(music)); store.scan(); song = store.library()[0]; server = create_server(store, web)
            try:
                with urllib.request.urlopen(server.url + '/api/health') as response: self.assertEqual({'ok': True}, json.loads(response.read()))
                request = urllib.request.Request(server.url + f"/media/{song['id']}", headers={'Range':'bytes=0-31'})
                with urllib.request.urlopen(request) as response: self.assertEqual(206, response.status); self.assertEqual(32, len(response.read()))
            finally: server.stop()


if __name__ == '__main__': unittest.main()
