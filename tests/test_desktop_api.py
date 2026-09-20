import struct
import sys
import tempfile
import unittest
import wave
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'python'))
import sqlite_close  # noqa: F401
from backend import NeoStore
from desktop import DesktopApi


class DesktopApiTests(unittest.TestCase):
    def make_wav(self, path: Path, seconds: float = .15):
        with wave.open(str(path), 'wb') as f:
            f.setnchannels(2); f.setsampwidth(2); f.setframerate(44100)
            f.writeframes(struct.pack('<hh', 0, 0) * max(1, int(44100 * seconds)))

    def test_full_backup_roundtrip_and_analysis(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); music=root/'Music'; music.mkdir(); self.make_wav(music/'Demo.wav')
            store=NeoStore(root/'data'); store.patch_settings({'minDurationMs':0,'accent':'teal'}); store.add_folder(str(music)); store.scan(); song=store.library()[0]
            folder=store.create_playlist_folder('Root'); playlist=store.create_playlist('Mix',folder['id']); store.add_playlist_song(playlist['id'],song['id'])
            store.set_favorite(song['id'],True); store.set_queue([song['id']]); store.save_lyrics(song['id'],{'plain':'hello','lrc':'[00:00.00]hello'}); store.save_track_profile(song['id'],{'accent':'#45D483','wallpaperOpacity':.4})
            api=DesktopApi(store); data=api._full_backup_data(); self.assertEqual(4,data['version']); self.assertEqual('Demo.wav',Path(data['queuePaths'][0]).name); self.assertTrue(Path(data['queuePaths'][0]).is_file())
            store.delete_playlist(playlist['id']); store.set_queue([]); store.save_lyrics(song['id'],{'plain':'changed','lrc':''}); store.set_favorite(song['id'],False)
            self.assertTrue(api._restore_full_data(data)); restored=store.playlists(); self.assertEqual('Mix',restored[0]['name']); self.assertEqual(song['id'],store.playlist(restored[0]['id'])['songs'][0]['id']); self.assertEqual(song['id'],store.queue()[0]['id']); self.assertIn('hello',store.lyrics(song['id'])['lrc']); self.assertTrue(store.library('',True)[0]['favorite'])
            self.assertTrue(api.save_track_analysis(song['id'],{'bpm':128,'key':'C','mood':'energetic','energy':.8})); analyzed=store.library()[0]; self.assertEqual(128,analyzed['bpm']); self.assertEqual('C',analyzed['key']); self.assertEqual('energetic',analyzed['mood'])


if __name__ == '__main__': unittest.main()
