from __future__ import annotations

import argparse
import base64
import json
import mimetypes
import os
import sys
import tempfile
import time
import urllib.request
from pathlib import Path
from typing import Any

import webview
import sqlite_close  # noqa: F401 - deterministic SQLite close on Windows
from backend import NeoStore, create_server
from offline_lyrics import OfflineLyricsEngine

APP_NAME = 'NEO Player'


def resource_path(name: str) -> Path:
    base = Path(getattr(sys, '_MEIPASS', Path(__file__).resolve().parent.parent))
    return base / name


def data_dir() -> Path:
    override = os.environ.get('NEO_DATA_DIR')
    if override:
        return Path(override)
    local = os.environ.get('LOCALAPPDATA') or str(Path.home() / 'AppData' / 'Local')
    return Path(local) / 'NEO Player'


def _dialog_value(result: Any) -> str | None:
    if not result:
        return None
    return str(result[0] if isinstance(result, (list, tuple)) else result)


def _open_dialog_enum():
    return getattr(webview.FileDialog, 'OPEN', getattr(webview.FileDialog, 'LOAD', None))


class DesktopApi:
    def __init__(self, store: NeoStore):
        self.store = store

    def _window(self):
        return webview.windows[0] if webview.windows else None

    def pick_folder(self) -> str | None:
        try:
            window = self._window()
            return _dialog_value(window.create_file_dialog(webview.FileDialog.FOLDER)) if window else None
        except Exception:
            return None

    def pick_file(self, file_types: tuple[str, ...] = ()) -> str | None:
        try:
            window = self._window()
            return _dialog_value(window.create_file_dialog(_open_dialog_enum(), file_types=file_types)) if window else None
        except Exception:
            return None

    def save_file(self, filename: str = 'NEO-Backup.json', file_types: tuple[str, ...] = ()) -> str | None:
        try:
            window = self._window()
            return _dialog_value(window.create_file_dialog(webview.FileDialog.SAVE, save_filename=filename, file_types=file_types)) if window else None
        except Exception:
            return None

    def minimize(self) -> None:
        if self._window(): self._window().minimize()

    def toggle_fullscreen(self) -> None:
        if self._window(): self._window().toggle_fullscreen()

    def installed_fonts(self) -> list[str]:
        """Return locally installed font family names without any network lookup."""
        names: set[str] = set()
        if os.name == 'nt':
            try:
                import winreg
                keys = [
                    (winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts'),
                    (winreg.HKEY_CURRENT_USER, r'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts'),
                ]
                for hive, path in keys:
                    try:
                        with winreg.OpenKey(hive, path) as key:
                            for index in range(winreg.QueryInfoKey(key)[1]):
                                label = winreg.EnumValue(key, index)[0]
                                family = label.rsplit(' (', 1)[0].strip()
                                if family: names.add(family)
                    except OSError:
                        continue
            except Exception:
                pass
        return sorted(names, key=str.casefold)

    def choose_offline_lyrics_model(self) -> str | None:
        path = self.pick_folder()
        if not path:
            return None
        model = Path(path)
        markers = {'model.bin', 'config.json', 'tokenizer.json'}
        if not any((model / marker).exists() for marker in markers):
            raise ValueError('The selected folder is not a compatible local Whisper model')
        self.store.patch_settings({'offlineLyricsModelPath': str(model.resolve())})
        return str(model.resolve())

    def transcribe_song_offline(self, song_id: int, language: str = 'auto') -> dict[str, Any]:
        path = self.store.song_path(int(song_id))
        if not path or not path.is_file():
            raise ValueError('Song file was not found')
        settings = self.store.settings()
        model_path = str(settings.get('offlineLyricsModelPath', '')).strip()
        if not model_path:
            raise ValueError('Choose a local Whisper model first')
        result = OfflineLyricsEngine().transcribe(path, model_path, language).as_dict()
        saved = self.store.save_lyrics(int(song_id), {'plain': result['plain'], 'lrc': result['lrc']})
        return result | {'saved': saved}

    def reveal_song(self, song_id: int) -> bool:
        path = self.store.song_path(int(song_id))
        if not path or not path.exists(): return False
        try:
            if os.name == 'nt':
                os.startfile(str(path.parent))
                return True
        except Exception:
            return False
        return False

    def import_m3u8(self) -> dict[str, Any] | None:
        path = self.pick_file(('M3U playlists (*.m3u8;*.m3u)', 'All files (*.*)'))
        if not path: return None
        return self.store.import_m3u8(path)

    def export_m3u8(self, playlist_id: int) -> str | None:
        item = self.store.playlist(int(playlist_id))
        if not item: return None
        safe = ''.join(c for c in item['name'] if c not in '<>:"/\\|?*').strip() or 'playlist'
        path = self.save_file(f'{safe}.m3u8', ('M3U8 playlist (*.m3u8)', 'All files (*.*)'))
        if not path: return None
        if not path.lower().endswith('.m3u8'): path += '.m3u8'
        return self.store.export_m3u8(int(playlist_id), path)

    def _full_backup_data(self) -> dict[str, Any]:
        data = self.store.backup()
        data['version'] = 3
        with self.store._connect() as db:
            data['playlistSongsByPath'] = [dict(r) for r in db.execute('''SELECT ps.playlist_id,ps.position,ps.hidden,s.path song_path FROM playlist_songs ps JOIN songs s ON s.id=ps.song_id ORDER BY ps.playlist_id,ps.position''')]
            data['lyricsByPath'] = [dict(r) for r in db.execute('''SELECT s.path song_path,l.plain,l.lrc,l.translation,l.romanization,l.updated_at FROM lyrics l JOIN songs s ON s.id=l.song_id''')]
            data['trackProfilesByPath'] = [dict(r) for r in db.execute('''SELECT s.path song_path,tp.* FROM track_profiles tp JOIN songs s ON s.id=tp.song_id''')]
            data['queuePaths'] = [r['path'] for r in db.execute('SELECT s.path FROM queue q JOIN songs s ON s.id=q.song_id ORDER BY q.position')]
        return data

    def export_full_backup(self) -> str | None:
        path = self.save_file(f"NEO-Backup-{time.strftime('%Y-%m-%d')}.json", ('JSON backup (*.json)', 'All files (*.*)'))
        if not path: return None
        if not path.lower().endswith('.json'): path += '.json'
        Path(path).write_text(json.dumps(self._full_backup_data(), ensure_ascii=False, indent=2), encoding='utf-8')
        return path

    def _restore_full_data(self, data: dict[str, Any]) -> bool:
        self.store.patch_settings(data.get('settings', {}))
        for folder in data.get('folders', []):
            try: self.store.add_folder(str(folder))
            except Exception: pass
        try: self.store.scan(False)
        except Exception: pass
        with self.store._connect() as db:
            path_to_id = {r['path']: int(r['id']) for r in db.execute('SELECT id,path FROM songs')}
            db.execute('DELETE FROM queue')
            db.execute('DELETE FROM playlist_songs')
            db.execute('DELETE FROM playlists')
            db.execute('DELETE FROM playlist_folders')
            db.execute('DELETE FROM lyrics')
            db.execute('DELETE FROM track_profiles')
            db.execute('DELETE FROM pins')
            for row in data.get('pins', []):
                try: db.execute('INSERT OR IGNORE INTO pins(kind,item_key,position,created_at) VALUES(?,?,?,?)', (row['kind'],row['item_key'],int(row.get('position',0)),int(row.get('created_at',time.time()))))
                except Exception: pass
            for row in data.get('playlistFolders', []):
                try:
                    db.execute('INSERT INTO playlist_folders(id,parent_id,name,position,pinned,hidden,created_at) VALUES(?,?,?,?,?,?,?)', (
                        int(row['id']), row.get('parent_id'), row.get('name','Folder'), int(row.get('position',0)), int(row.get('pinned',0)), int(row.get('hidden',0)), int(row.get('created_at',time.time()))))
                except Exception: pass
            for row in data.get('playlists', []):
                try:
                    db.execute('''INSERT INTO playlists(id,name,created_at,folder_id,pinned,hidden,sort_mode,sort_desc,view_mode) VALUES(?,?,?,?,?,?,?,?,?)''', (
                        int(row['id']), row.get('name','Playlist'), int(row.get('created_at',time.time())), row.get('folder_id'), int(row.get('pinned',0)), int(row.get('hidden',0)), row.get('sort_mode','custom'), int(row.get('sort_desc',0)), row.get('view_mode','list')))
                except Exception: pass
            path_rows = data.get('playlistSongsByPath', [])
            if path_rows:
                for row in path_rows:
                    sid = path_to_id.get(str(row.get('song_path','')))
                    if sid:
                        try: db.execute('INSERT OR REPLACE INTO playlist_songs(playlist_id,song_id,position,hidden) VALUES(?,?,?,?)', (int(row['playlist_id']),sid,int(row.get('position',0)),int(row.get('hidden',0))))
                        except Exception: pass
            else:
                for row in data.get('playlistSongs', []):
                    if db.execute('SELECT 1 FROM songs WHERE id=?',(row.get('song_id'),)).fetchone():
                        try: db.execute('INSERT OR REPLACE INTO playlist_songs(playlist_id,song_id,position,hidden) VALUES(?,?,?,?)',(int(row['playlist_id']),int(row['song_id']),int(row.get('position',0)),int(row.get('hidden',0))))
                        except Exception: pass
            lyrics_rows = data.get('lyricsByPath', [])
            if lyrics_rows:
                for row in lyrics_rows:
                    sid=path_to_id.get(str(row.get('song_path','')))
                    if sid: db.execute('INSERT OR REPLACE INTO lyrics(song_id,plain,lrc,translation,romanization,updated_at) VALUES(?,?,?,?,?,?)',(sid,row.get('plain',''),row.get('lrc',''),row.get('translation',''),row.get('romanization',''),int(row.get('updated_at',0))))
            profiles = data.get('trackProfilesByPath', [])
            for row in profiles:
                sid=path_to_id.get(str(row.get('song_path','')))
                if not sid: continue
                db.execute('''INSERT OR REPLACE INTO track_profiles(song_id,accent,background,secondary,artwork_path,wallpaper_path,wallpaper_opacity,wallpaper_blur,canvas_path,canvas_start,canvas_end,canvas_speed,visualizer,eq_json,bass,virtualizer,loudness) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)''', (
                    sid,row.get('accent',''),row.get('background',''),row.get('secondary',''),row.get('artwork_path',''),row.get('wallpaper_path',''),float(row.get('wallpaper_opacity',.28)),int(row.get('wallpaper_blur',18)),row.get('canvas_path',''),float(row.get('canvas_start',0)),float(row.get('canvas_end',0)),float(row.get('canvas_speed',1)),row.get('visualizer',''),row.get('eq_json',''),float(row.get('bass',0)),float(row.get('virtualizer',0)),float(row.get('loudness',0))))
            for index,path in enumerate(data.get('queuePaths', [])):
                sid=path_to_id.get(str(path))
                if sid: db.execute('INSERT OR REPLACE INTO queue(position,song_id) VALUES(?,?)',(index,sid))
            for row in data.get('songUser', []):
                db.execute('UPDATE songs SET favorite=?,hidden=?,play_count=?,skip_count=?,last_played=? WHERE path=?', (int(row.get('favorite',0)),int(row.get('hidden',0)),int(row.get('play_count',0)),int(row.get('skip_count',0)),int(row.get('last_played',0)),str(row.get('path',''))))
        return True

    def restore_full_backup(self) -> bool:
        path = self.pick_file(('JSON backup (*.json)', 'All files (*.*)'))
        if not path: return False
        data = json.loads(Path(path).read_text(encoding='utf-8-sig'))
        return self._restore_full_data(data)

    def pick_track_asset(self, song_id: int, kind: str) -> str | None:
        kind = str(kind).lower()
        mapping = {'artwork':'artworkPath','wallpaper':'wallpaperPath','canvas':'canvasPath'}
        if kind not in mapping: return None
        file_types = ('Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)', 'All files (*.*)') if kind != 'canvas' else ('Video (*.mp4;*.webm;*.m4v;*.mov)', 'All files (*.*)')
        path = self.pick_file(file_types)
        if not path: return None
        self.store.save_track_profile(int(song_id), {mapping[kind]: path})
        return path

    def clear_track_assets(self, song_id: int) -> bool:
        self.store.save_track_profile(int(song_id), {'artworkPath':'','wallpaperPath':'','canvasPath':'','accent':'','background':'','secondary':''})
        return True

    def track_asset_data(self, song_id: int, kind: str) -> str | None:
        profile = self.store.track_profile(int(song_id)); key = {'artwork':'artworkPath','wallpaper':'wallpaperPath','canvas':'canvasPath'}.get(str(kind).lower())
        if not key: return None
        raw = profile.get(key,''); path = Path(raw) if raw else None
        if not path or not path.is_file(): return None
        size = path.stat().st_size
        if size > 48 * 1024 * 1024: return None
        mime = mimetypes.guess_type(path.name)[0] or ('video/mp4' if kind == 'canvas' else 'image/jpeg')
        return f"data:{mime};base64,{base64.b64encode(path.read_bytes()).decode('ascii')}"

    def save_track_analysis(self, song_id: int, analysis: dict[str, Any]) -> bool:
        with self.store._connect() as db:
            db.execute('UPDATE songs SET bpm=?,musical_key=?,mood=?,energy=? WHERE id=?', (
                max(0.0,float(analysis.get('bpm',0) or 0)), str(analysis.get('key',''))[:24], str(analysis.get('mood',''))[:32], max(0.0,min(1.0,float(analysis.get('energy',0) or 0))), int(song_id)))
        return True


def run_self_test() -> int:
    with tempfile.TemporaryDirectory(prefix='neo-player-test-') as tmp:
        store = NeoStore(tmp); desktop = DesktopApi(store)
        defaults = store.settings(); assert defaults['accent'] == 'orange'; assert defaults['themeMode'] == 'dark'; assert defaults['minDurationMs'] == 10_000
        store.patch_settings({'accent':'blue','volume':.5,'language':'fa'}); updated=store.settings(); assert updated['accent']=='blue' and updated['language']=='fa'
        playlist=store.create_playlist('Offline Mix'); assert playlist['name']=='Offline Mix' and store.playlist(playlist['id'])['songs']==[]
        folder=store.create_playlist_folder('Folder'); assert folder['name']=='Folder'; assert isinstance(store.system_roots(),list)
        backup=desktop._full_backup_data(); assert backup['version']==3 and 'queuePaths' in backup
    print('NEO_SELF_TEST_OK'); return 0


def run_ui_smoke() -> int:
    with tempfile.TemporaryDirectory(prefix='neo-player-ui-') as tmp:
        store=NeoStore(tmp); server=create_server(store,resource_path('dist')); rendered={'ok':False}
        try:
            with urllib.request.urlopen(server.url+'/api/health',timeout=5) as response: assert 'true' in response.read().decode('utf-8').lower()
            with urllib.request.urlopen(server.url+'/',timeout=5) as response: assert '<div id="root"></div>' in response.read().decode('utf-8')
            window=webview.create_window(APP_NAME,server.url,width=1180,height=760,min_size=(900,600),js_api=DesktopApi(store))
            def verify_after_start():
                deadline=time.time()+7
                while time.time()<deadline:
                    try:
                        value=window.evaluate_js("Boolean(document.querySelector('.app-shell')) && Boolean(document.querySelector('.neo-plus-fab')) && document.body.innerText.includes('NEO PLAYER')")
                        if value: rendered['ok']=True; break
                    except Exception: pass
                    time.sleep(.25)
                window.destroy()
            webview.start(verify_after_start,gui='edgechromium'); assert rendered['ok'],'React/NEO+ DOM did not render in packaged WebView2'
        finally: server.stop()
    print('NEO_UI_SMOKE_OK'); return 0


def run_app() -> int:
    store=NeoStore(data_dir()); server=create_server(store,resource_path('dist'))
    try:
        webview.create_window(APP_NAME,server.url,width=1360,height=860,min_size=(960,640),js_api=DesktopApi(store),background_color='#0b0b0b')
        webview.start(gui='edgechromium'); return 0
    finally: server.stop()


def main() -> int:
    parser=argparse.ArgumentParser(add_help=False); parser.add_argument('--self-test',action='store_true'); parser.add_argument('--ui-smoke',action='store_true'); args,_=parser.parse_known_args()
    if args.self_test:return run_self_test()
    if args.ui_smoke:return run_ui_smoke()
    return run_app()


if __name__=='__main__': raise SystemExit(main())
