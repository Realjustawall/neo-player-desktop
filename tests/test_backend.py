import json
import struct
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
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

    def test_excluded_folder_is_not_scanned(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); music = root / 'Music'; blocked = music / 'Private'; blocked.mkdir(parents=True)
            self.make_wav(music / 'Keep.wav', .2); self.make_wav(blocked / 'Skip.wav', .2)
            store = NeoStore(root / 'data'); store.add_folder(str(music))
            store.patch_settings({'minDurationMs': 0, 'excludedFolders': [str(blocked)]})
            result = store.scan(); self.assertEqual(1, result['found'])
            self.assertEqual(['Keep'], [song['title'] for song in store.library()])

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
            store.set_pin('album', 'Offline Album', True); self.assertEqual('Offline Album', store.pins()[0]['itemKey'])
            self.assertEqual('Offline Album', store.backup()['pins'][0]['item_key'])

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
                with urllib.request.urlopen(server.url + f"/cover/{song['id']}") as response:
                    self.assertEqual('image/svg+xml', response.headers.get_content_type()); self.assertIn(b'<svg', response.read())
                request = urllib.request.Request(server.url + '/api/streams', method='POST',
                    data=json.dumps({'url':'https://example.com/radio.mp3','title':'Radio'}).encode('utf-8'),
                    headers={'Content-Type':'application/json'})
                with urllib.request.urlopen(request) as response:
                    stream = json.loads(response.read()); self.assertEqual('stream', stream['sourceType'])
            finally: server.stop()

    def test_recent_search_queue_validation_playlist_reorder_and_cache(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); music = root / 'Music'; music.mkdir()
            self.make_wav(music / 'A.wav'); self.make_wav(music / 'B.wav')
            store = NeoStore(root / 'data'); store.patch_settings({'minDurationMs': 0, 'themePreset': 'aurora'})
            store.add_folder(str(music)); store.scan(); songs = store.library()
            self.assertEqual('aurora', store.settings()['themePreset'])
            self.assertEqual(['Neo Mix'], store.add_recent_search('  Neo   Mix  '))
            store.add_recent_search('Persian'); self.assertEqual(['Persian', 'Neo Mix'], store.recent_searches())
            store.clear_recent_searches(); self.assertEqual([], store.recent_searches())
            store.set_queue([songs[0]['id'], 999999, songs[1]['id']])
            self.assertEqual([songs[0]['id'], songs[1]['id']], [s['id'] for s in store.queue()])
            playlist = store.create_playlist('Order')
            for song in songs: store.add_playlist_song(playlist['id'], song['id'])
            store.reorder_playlist(playlist['id'], [songs[1]['id'], songs[0]['id']])
            self.assertEqual([songs[1]['id'], songs[0]['id']], [s['id'] for s in store.playlist(playlist['id'])['songs']])
            cache = root / 'data' / 'cache'; cache.mkdir(); (cache / 'art.tmp').write_bytes(b'1234')
            self.assertEqual({'files': 1, 'bytes': 4}, store.cache_status())
            self.assertEqual(1, store.clear_cache()['removed']); self.assertEqual(0, store.cache_status()['files'])

    def test_format_filters_stream_playlists_and_scoped_audio_profiles(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); music = root / 'Music'; music.mkdir()
            self.make_wav(music / 'Keep.wav'); (music / 'Ignore.mp3').write_bytes(b'not audio')
            store = NeoStore(root / 'data'); store.add_folder(str(music))
            store.patch_settings({'minDurationMs': 0, 'audioExtensions': ['wav', '.invalid']})
            self.assertEqual(['.wav'], store.settings()['audioExtensions'])
            store.scan(); self.assertEqual(['Keep'], [song['title'] for song in store.library()])
            stream = store.add_stream('https://example.com/live.mp3', 'Live')
            self.assertEqual('stream', stream['sourceType']); self.assertIsNone(store.song_path(stream['id']))
            folder = store.create_playlist_folder('Radio')
            folder_fx = {'enabled': True, 'eq': [1, 2, 3, 4, 5], 'bass': 30, 'virtualizer': 20, 'loudness': 2}
            store.patch_playlist_folder(folder['id'], {'audioProfile': folder_fx})
            playlist = store.create_playlist('Streams', folder['id']); store.add_playlist_song(playlist['id'], stream['id'])
            self.assertTrue(store.playlist(playlist['id'])['folderAudioProfile']['enabled'])
            playlist_fx = {'enabled': True, 'eq': [-1, 0, 1, 0, -1], 'bass': 12}
            store.patch_playlist(playlist['id'], {'audioProfile': playlist_fx})
            self.assertEqual(12, store.playlist(playlist['id'])['audioProfile']['bass'])
            exported = root / 'shared.m3u8'; store.export_m3u8(playlist['id'], str(exported))
            self.assertIn('https://example.com/live.mp3', exported.read_text(encoding='utf-8'))
            imported = store.import_m3u8(str(exported), 'Imported')
            self.assertEqual('stream', imported['songs'][0]['sourceType'])

    def test_track_audio_profile_is_clamped(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); music = root / 'Music'; music.mkdir(); self.make_wav(music / 'Demo.wav')
            store = NeoStore(root / 'data'); store.patch_settings({'minDurationMs': 0}); store.add_folder(str(music)); store.scan()
            song = store.library()[0]
            profile = store.save_track_profile(song['id'], {'audioEnabled': True, 'eq': [99, -99], 'bass': 999, 'virtualizer': 55, 'loudness': -99})
            self.assertTrue(profile['audioEnabled']); self.assertEqual([12, -12, 0, 0, 0], profile['eq'])
            self.assertEqual(100, profile['bass']); self.assertEqual(-12, profile['loudness'])

    def test_stream_proxy_and_strict_offline_guard(self):
        class AudioHandler(BaseHTTPRequestHandler):
            def log_message(self, *_): pass
            def do_GET(self):
                data = b'live-audio-bytes'; self.send_response(200); self.send_header('Content-Type','audio/mpeg')
                self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
        upstream = ThreadingHTTPServer(('127.0.0.1', 0), AudioHandler)
        thread = threading.Thread(target=upstream.serve_forever, daemon=True); thread.start()
        try:
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp); web = root / 'dist'; web.mkdir(); (web / 'index.html').write_text('ok')
                store = NeoStore(root / 'data'); stream = store.add_stream(f'http://127.0.0.1:{upstream.server_port}/live.mp3')
                server = create_server(store, web)
                try:
                    with urllib.request.urlopen(server.url + f"/media/{stream['id']}") as response:
                        self.assertEqual('audio/mpeg', response.headers.get_content_type()); self.assertEqual(b'live-audio-bytes', response.read())
                    store.patch_settings({'strictOfflineMode': True})
                    with self.assertRaises(urllib.error.HTTPError) as error:
                        urllib.request.urlopen(server.url + f"/media/{stream['id']}")
                    self.assertEqual(403, error.exception.code)
                finally: server.stop()
        finally:
            upstream.shutdown(); upstream.server_close(); thread.join(timeout=3)


if __name__ == '__main__': unittest.main()
