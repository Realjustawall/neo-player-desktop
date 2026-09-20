import json
import sqlite3

_original_connect = sqlite3.connect


class ClosingConnection(sqlite3.Connection):
    def __exit__(self, exc_type, exc, tb):
        try:
            return super().__exit__(exc_type, exc, tb)
        finally:
            self.close()


def _connect(*args, **kwargs):
    kwargs.setdefault('factory', ClosingConnection)
    return _original_connect(*args, **kwargs)


sqlite3.connect = _connect

from backend import NeoStore
from native_scanner import NativeLibraryScanner


SONG_COLUMNS = {
    'year': 'INTEGER NOT NULL DEFAULT 0',
    'disc': 'INTEGER NOT NULL DEFAULT 0',
    'bitrate': 'INTEGER NOT NULL DEFAULT 0',
    'sample_rate': 'INTEGER NOT NULL DEFAULT 0',
    'channels': 'INTEGER NOT NULL DEFAULT 0',
    'file_size': 'INTEGER NOT NULL DEFAULT 0',
    'hidden': 'INTEGER NOT NULL DEFAULT 0',
    'play_count': 'INTEGER NOT NULL DEFAULT 0',
    'skip_count': 'INTEGER NOT NULL DEFAULT 0',
    'last_played': 'INTEGER NOT NULL DEFAULT 0',
    'bpm': 'REAL NOT NULL DEFAULT 0',
    'musical_key': "TEXT NOT NULL DEFAULT ''",
    'mood': "TEXT NOT NULL DEFAULT ''",
    'energy': 'REAL NOT NULL DEFAULT 0',
    'source_type': "TEXT NOT NULL DEFAULT 'file'",
    'source_url': "TEXT NOT NULL DEFAULT ''",
}


def _repair_schema(store):
    with store._connect() as db:
        tables = {row['name'] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        if 'songs' not in tables:
            store._init_db()
            return
        columns = {row['name'] for row in db.execute('PRAGMA table_info(songs)')}
        for name, ddl in SONG_COLUMNS.items():
            if name not in columns:
                db.execute(f'ALTER TABLE songs ADD COLUMN {name} {ddl}')
        playlist_columns = {row['name'] for row in db.execute('PRAGMA table_info(playlists)')} if 'playlists' in tables else set()
        if 'playlists' in tables and 'audio_profile_json' not in playlist_columns:
            db.execute("ALTER TABLE playlists ADD COLUMN audio_profile_json TEXT NOT NULL DEFAULT ''")
        folder_columns = {row['name'] for row in db.execute('PRAGMA table_info(playlist_folders)')} if 'playlist_folders' in tables else set()
        if 'playlist_folders' in tables and 'audio_profile_json' not in folder_columns:
            db.execute("ALTER TABLE playlist_folders ADD COLUMN audio_profile_json TEXT NOT NULL DEFAULT ''")
        profile_columns = {row['name'] for row in db.execute('PRAGMA table_info(track_profiles)')} if 'track_profiles' in tables else set()
        if 'track_profiles' in tables and 'audio_profile_enabled' not in profile_columns:
            db.execute('ALTER TABLE track_profiles ADD COLUMN audio_profile_enabled INTEGER NOT NULL DEFAULT 0')


def _scanner(store):
    _repair_schema(store)
    value = getattr(store, '_neo_store_scanner', None)
    if value is None:
        value = NativeLibraryScanner(store)
        setattr(store, '_neo_store_scanner', value)
    return value


def _native_store_scan(self, scan_all=False):
    _repair_schema(self)
    return _scanner(self).scan(bool(scan_all))


NeoStore.scan = _native_store_scan

try:
    import webview

    _create_window = webview.create_window

    def _attach_native_core(api):
        store = getattr(api, 'store', None)
        if store is None or getattr(api, '_neo_native_core_ready', False):
            return api
        _repair_schema(store)
        scanner = _scanner(store)
        setattr(api, '_neo_native_core_ready', True)
        setattr(api, '_neo_scanner', scanner)

        def native_capabilities():
            return {
                'nativeCore': True,
                'directFilesystem': True,
                'parallelScanner': True,
                'smartAutoScan': True,
                'scanStatus': True,
                'schemaRepair': True,
                'platform': 'windows',
            }

        def scan_library(scan_all=False):
            _repair_schema(store)
            return scanner.scan(bool(scan_all))

        def scan_status():
            return scanner.status()

        def auto_scan_music():
            _repair_schema(store)
            return scanner.auto_scan()

        def native_folders():
            return store.folders()

        def native_system_roots():
            return store.system_roots()

        def native_add_folder(path):
            return {'path': store.add_folder(str(path))}

        def native_remove_folder(path):
            store.remove_folder(str(path))
            return {'ok': True}

        def native_library(query='', include_hidden=False):
            _repair_schema(store)
            return store.library(str(query or ''), None, bool(include_hidden))

        def native_favorites():
            _repair_schema(store)
            return store.library('', True)

        def native_history(limit=100):
            _repair_schema(store)
            return store.history(max(1, min(int(limit), 500)))

        def native_stats():
            _repair_schema(store)
            return store.stats()

        def native_settings():
            return store.settings()

        def native_patch_settings(patch):
            return store.patch_settings(dict(patch or {}))

        def pick_and_scan_folder():
            path = api.pick_folder()
            if not path:
                return {'cancelled': True, 'native': True}
            _repair_schema(store)
            store.add_folder(path)
            result = scanner.scan(False)
            result['path'] = path
            return result

        for name, fn in {
            'native_capabilities': native_capabilities,
            'scan_library': scan_library,
            'scan_status': scan_status,
            'auto_scan_music': auto_scan_music,
            'native_folders': native_folders,
            'native_system_roots': native_system_roots,
            'native_add_folder': native_add_folder,
            'native_remove_folder': native_remove_folder,
            'native_library': native_library,
            'native_favorites': native_favorites,
            'native_history': native_history,
            'native_stats': native_stats,
            'native_settings': native_settings,
            'native_patch_settings': native_patch_settings,
            'pick_and_scan_folder': pick_and_scan_folder,
        }.items():
            setattr(api, name, fn)

        try:
            with store._connect() as db:
                marker = db.execute("SELECT value FROM settings WHERE key='native_language_initialized'").fetchone()
                if marker is None:
                    current = db.execute("SELECT value FROM settings WHERE key='language'").fetchone()
                    value = json.loads(current[0]) if current else 'system'
                    if value == 'system':
                        db.execute("INSERT INTO settings(key,value) VALUES('language',?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", (json.dumps('fa'),))
                    db.execute("INSERT OR REPLACE INTO settings(key,value) VALUES('native_language_initialized','true')")
        except Exception:
            pass
        return api

    def _native_window(*args, **kwargs):
        if 'js_api' in kwargs:
            kwargs['js_api'] = _attach_native_core(kwargs.get('js_api'))
        return _create_window(*args, **kwargs)

    webview.create_window = _native_window
except Exception:
    pass
