from __future__ import annotations

import json
import mimetypes
import os
import re
import sqlite3
import threading
import time
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, unquote, urlparse

from mutagen import File as MutagenFile

AUDIO_EXTENSIONS = {'.mp3', '.flac', '.wav', '.m4a', '.aac', '.ogg', '.opus', '.wma', '.aiff', '.aif', '.ape'}
SKIP_DIR_NAMES = {
    '$recycle.bin', 'system volume information', 'windows', 'program files', 'program files (x86)',
    'programdata', 'recovery', 'node_modules', '.git', '.cache', 'appdata'
}
PLACEHOLDER_COVER = b'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><defs><linearGradient id="g" x2="1" y2="1"><stop stop-color="#292929"/><stop offset="1" stop-color="#101010"/></linearGradient><linearGradient id="a" x2="1"><stop stop-color="#ff9a45"/><stop offset="1" stop-color="#ff6517"/></linearGradient></defs><rect width="512" height="512" rx="42" fill="url(#g)"/><path d="M132 338V174c0-24 30-34 45-15l158 198c15 19 45 8 45-16V174" fill="none" stroke="url(#a)" stroke-width="48" stroke-linecap="round" stroke-linejoin="round"/><circle cx="132" cy="374" r="34" fill="#ff7a1a"/><circle cx="380" cy="138" r="34" fill="#ff7a1a"/></svg>'''

DEFAULT_SETTINGS: dict[str, Any] = {
    'themeMode': 'dark', 'accent': 'orange', 'customColor': '#ff7a1a', 'language': 'system',
    'themePreset': 'studio', 'interfaceDensity': 'comfortable',
    'fontFamily': 'vazirmatn', 'fontScale': 1.0, 'customFontName': '', 'dynamicArtwork': True, 'reduceMotion': False,
    'rememberQueue': True, 'resumeLastSong': True, 'lastSongId': 0, 'lastPosition': 0.0, 'minDurationMs': 10_000,
    'volume': 0.82, 'shuffle': False, 'repeat': 'off', 'playbackRate': 1.0,
    'crossfadeMs': 0, 'gaplessEnabled': True, 'automixEnabled': False, 'advancedAutomixEnabled': True,
    'loudnessNormalization': False, 'normalizationTargetLufs': -14.0, 'normalizationMode': 'smart',
    'lyricsMode': 'auto', 'translationEnabled': True, 'romanizationEnabled': True,
    'lyricsFontSize': 20, 'lyricsAutoScroll': True, 'writeLyricsSidecar': False,
    'offlineLyricsModelPath': '', 'offlineLyricsLanguage': 'auto',
    'libraryViewMode': 'list', 'strictOfflineMode': False, 'excludedFolders': [],
    'offlineBackupEnabled': True, 'offlineBackupAutoRefresh': True, 'offlineBackupLimit': 100,
    'offlineBackupMood': 'all', 'offlineBackupGenre': 'all',
    'visualizer': 'bars', 'visualizerSensitivity': 1.0,
    'eqEnabled': False, 'eqBands': [0, 0, 0, 0, 0], 'bassBoost': 0,
}


def _first(value: Any, fallback: str = '') -> str:
    if value is None:
        return fallback
    if isinstance(value, (list, tuple)):
        return str(value[0]) if value else fallback
    return str(value)


def _track_number(value: Any) -> int:
    text = _first(value, '0').split('/')[0]
    match = re.search(r'\d+', text)
    return int(match.group()) if match else 0


def _year(value: Any) -> int:
    match = re.search(r'(19|20)\d{2}', _first(value))
    return int(match.group()) if match else 0


def _safe_json(value: str, fallback: Any = None) -> Any:
    try:
        return json.loads(value)
    except Exception:
        return fallback


class NeoStore:
    def __init__(self, data_dir: str | os.PathLike[str]):
        self.data_dir = Path(data_dir)
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.db_path = self.data_dir / 'neo.sqlite3'
        self._init_db()

    def _connect(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self.db_path, timeout=30)
        conn.row_factory = sqlite3.Row
        conn.execute('PRAGMA foreign_keys=ON')
        conn.execute('PRAGMA journal_mode=WAL')
        return conn

    def _ensure_column(self, db: sqlite3.Connection, table: str, name: str, ddl: str) -> None:
        columns = {row['name'] for row in db.execute(f'PRAGMA table_info({table})')}
        if name not in columns:
            db.execute(f'ALTER TABLE {table} ADD COLUMN {name} {ddl}')

    def _init_db(self) -> None:
        with self._connect() as db:
            db.executescript('''
                CREATE TABLE IF NOT EXISTS folders(path TEXT PRIMARY KEY, added_at INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS songs(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT NOT NULL UNIQUE, title TEXT NOT NULL,
                    artist TEXT NOT NULL DEFAULT '', album TEXT NOT NULL DEFAULT '', genre TEXT NOT NULL DEFAULT '',
                    track INTEGER NOT NULL DEFAULT 0, duration REAL NOT NULL DEFAULT 0,
                    favorite INTEGER NOT NULL DEFAULT 0, added_at INTEGER NOT NULL,
                    modified_at INTEGER NOT NULL DEFAULT 0, last_seen INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_songs_title ON songs(title);
                CREATE INDEX IF NOT EXISTS idx_songs_artist ON songs(artist);
                CREATE INDEX IF NOT EXISTS idx_songs_album ON songs(album);
                CREATE TABLE IF NOT EXISTS playlist_folders(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, parent_id INTEGER REFERENCES playlist_folders(id) ON DELETE CASCADE,
                    name TEXT NOT NULL, position INTEGER NOT NULL DEFAULT 0, pinned INTEGER NOT NULL DEFAULT 0,
                    hidden INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS playlists(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, created_at INTEGER NOT NULL,
                    folder_id INTEGER REFERENCES playlist_folders(id) ON DELETE SET NULL,
                    pinned INTEGER NOT NULL DEFAULT 0, hidden INTEGER NOT NULL DEFAULT 0,
                    sort_mode TEXT NOT NULL DEFAULT 'custom', sort_desc INTEGER NOT NULL DEFAULT 0,
                    view_mode TEXT NOT NULL DEFAULT 'list'
                );
                CREATE TABLE IF NOT EXISTS playlist_songs(
                    playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
                    song_id INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE,
                    position INTEGER NOT NULL, hidden INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(playlist_id, song_id)
                );
                CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS queue(position INTEGER PRIMARY KEY, song_id INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE);
                CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY AUTOINCREMENT, song_id INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE, played_at INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS lyrics(
                    song_id INTEGER PRIMARY KEY REFERENCES songs(id) ON DELETE CASCADE,
                    plain TEXT NOT NULL DEFAULT '', lrc TEXT NOT NULL DEFAULT '', translation TEXT NOT NULL DEFAULT '',
                    romanization TEXT NOT NULL DEFAULT '', updated_at INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS track_profiles(
                    song_id INTEGER PRIMARY KEY REFERENCES songs(id) ON DELETE CASCADE,
                    accent TEXT NOT NULL DEFAULT '', background TEXT NOT NULL DEFAULT '', secondary TEXT NOT NULL DEFAULT '',
                    artwork_path TEXT NOT NULL DEFAULT '', wallpaper_path TEXT NOT NULL DEFAULT '', wallpaper_opacity REAL NOT NULL DEFAULT .28,
                    wallpaper_blur INTEGER NOT NULL DEFAULT 18, canvas_path TEXT NOT NULL DEFAULT '', canvas_start REAL NOT NULL DEFAULT 0,
                    canvas_end REAL NOT NULL DEFAULT 0, canvas_speed REAL NOT NULL DEFAULT 1, visualizer TEXT NOT NULL DEFAULT '',
                    eq_json TEXT NOT NULL DEFAULT '', bass REAL NOT NULL DEFAULT 0, virtualizer REAL NOT NULL DEFAULT 0,
                    loudness REAL NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS recommendation_feedback(
                    song_id INTEGER PRIMARY KEY REFERENCES songs(id) ON DELETE CASCADE,
                    liked REAL NOT NULL DEFAULT 0, skipped REAL NOT NULL DEFAULT 0, updated_at INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS recent_searches(
                    query TEXT PRIMARY KEY COLLATE NOCASE, searched_at INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS pins(
                    kind TEXT NOT NULL, item_key TEXT NOT NULL, position INTEGER NOT NULL DEFAULT 0,
                    created_at INTEGER NOT NULL, PRIMARY KEY(kind,item_key)
                );
            ''')
            for name, ddl in [
                ('year', 'INTEGER NOT NULL DEFAULT 0'), ('disc', 'INTEGER NOT NULL DEFAULT 0'),
                ('bitrate', 'INTEGER NOT NULL DEFAULT 0'), ('sample_rate', 'INTEGER NOT NULL DEFAULT 0'),
                ('channels', 'INTEGER NOT NULL DEFAULT 0'), ('file_size', 'INTEGER NOT NULL DEFAULT 0'),
                ('hidden', 'INTEGER NOT NULL DEFAULT 0'), ('play_count', 'INTEGER NOT NULL DEFAULT 0'),
                ('skip_count', 'INTEGER NOT NULL DEFAULT 0'), ('last_played', 'INTEGER NOT NULL DEFAULT 0'),
                ('bpm', 'REAL NOT NULL DEFAULT 0'), ('musical_key', "TEXT NOT NULL DEFAULT ''"),
                ('mood', "TEXT NOT NULL DEFAULT ''"), ('energy', 'REAL NOT NULL DEFAULT 0'),
            ]:
                self._ensure_column(db, 'songs', name, ddl)
            for key, value in DEFAULT_SETTINGS.items():
                db.execute('INSERT OR IGNORE INTO settings(key,value) VALUES(?,?)', (key, json.dumps(value, ensure_ascii=False)))

    def settings(self) -> dict[str, Any]:
        values = dict(DEFAULT_SETTINGS)
        with self._connect() as db:
            for row in db.execute('SELECT key,value FROM settings'):
                values[row['key']] = _safe_json(row['value'], values.get(row['key']))
        return values

    def patch_settings(self, patch: dict[str, Any]) -> dict[str, Any]:
        allowed = set(DEFAULT_SETTINGS)
        sanitized = dict(patch)
        if 'minDurationMs' in sanitized:
            sanitized['minDurationMs'] = max(0, min(int(sanitized['minDurationMs']), 60 * 60 * 1000))
        if 'volume' in sanitized:
            sanitized['volume'] = max(0.0, min(float(sanitized['volume']), 1.0))
        if 'playbackRate' in sanitized:
            sanitized['playbackRate'] = max(.25, min(float(sanitized['playbackRate']), 3.0))
        if 'crossfadeMs' in sanitized:
            sanitized['crossfadeMs'] = max(0, min(int(sanitized['crossfadeMs']), 12000))
        if 'fontScale' in sanitized:
            sanitized['fontScale'] = max(.8, min(float(sanitized['fontScale']), 1.4))
        if 'customFontName' in sanitized: sanitized['customFontName'] = str(sanitized['customFontName'])[:160]
        if 'lastSongId' in sanitized: sanitized['lastSongId'] = max(0, int(sanitized['lastSongId']))
        if 'lastPosition' in sanitized: sanitized['lastPosition'] = max(0.0, float(sanitized['lastPosition']))
        if 'offlineLyricsModelPath' in sanitized: sanitized['offlineLyricsModelPath'] = str(sanitized['offlineLyricsModelPath'])[:1000]
        if 'offlineLyricsLanguage' in sanitized:
            value = str(sanitized['offlineLyricsLanguage']).strip().lower()
            sanitized['offlineLyricsLanguage'] = value if re.fullmatch(r'auto|[a-z]{2,3}', value) else 'auto'
        if 'excludedFolders' in sanitized:
            values = sanitized['excludedFolders'] if isinstance(sanitized['excludedFolders'], list) else []
            sanitized['excludedFolders'] = list(dict.fromkeys(str(Path(v).expanduser().resolve()) for v in values if isinstance(v,str) and v.strip()))[:100]
        with self._connect() as db:
            for key, value in sanitized.items():
                if key in allowed:
                    db.execute('INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value', (key, json.dumps(value, ensure_ascii=False)))
        return self.settings()

    def add_folder(self, path: str) -> str:
        resolved = str(Path(path).expanduser().resolve())
        if not Path(resolved).is_dir():
            raise ValueError('Folder does not exist')
        with self._connect() as db:
            db.execute('INSERT OR IGNORE INTO folders(path,added_at) VALUES(?,?)', (resolved, int(time.time())))
        return resolved

    def folders(self) -> list[str]:
        with self._connect() as db:
            return [r['path'] for r in db.execute('SELECT path FROM folders ORDER BY added_at')]

    def remove_folder(self, path: str) -> None:
        with self._connect() as db:
            db.execute('DELETE FROM folders WHERE path=?', (path,))

    def system_roots(self) -> list[str]:
        if os.name != 'nt':
            return [str(Path.home())]
        try:
            import ctypes
            mask = ctypes.windll.kernel32.GetLogicalDrives()
            roots: list[str] = []
            for index in range(26):
                if mask & (1 << index):
                    root = f'{chr(65 + index)}:\\'
                    if ctypes.windll.kernel32.GetDriveTypeW(root) == 3:  # DRIVE_FIXED
                        roots.append(root)
            return roots
        except Exception:
            return [str(Path.home())]

    def _walk_audio(self, roots: list[str], scan_all: bool = False):
        excluded = [os.path.normcase(os.path.abspath(str(p))) for p in self.settings().get('excludedFolders', []) if p]
        def is_excluded(path: str | os.PathLike[str]) -> bool:
            candidate = os.path.normcase(os.path.abspath(str(path)))
            return any(candidate == item or candidate.startswith(item + os.sep) for item in excluded)
        for root in roots:
            if not Path(root).exists():
                continue
            for base, dirs, files in os.walk(root, onerror=lambda _: None):
                if is_excluded(base): dirs[:] = []; continue
                dirs[:] = [d for d in dirs if not is_excluded(Path(base) / d)]
                if scan_all:
                    dirs[:] = [d for d in dirs if d.lower() not in SKIP_DIR_NAMES and not d.startswith('.')]
                for name in files:
                    path = Path(base) / name
                    if path.suffix.lower() in AUDIO_EXTENSIONS:
                        yield path

    def scan(self, scan_all: bool = False) -> dict[str, int]:
        roots = self.system_roots() if scan_all else self.folders()
        now = int(time.time())
        found = updated = skipped_short = 0
        minimum = int(self.settings().get('minDurationMs', 10_000)) / 1000.0
        with self._connect() as db:
            for path in self._walk_audio(roots, scan_all=scan_all):
                found += 1
                try:
                    stat = path.stat()
                    existing = db.execute('SELECT id,modified_at,duration FROM songs WHERE path=?', (str(path),)).fetchone()
                    if existing and int(existing['modified_at']) == int(stat.st_mtime):
                        if float(existing['duration']) >= minimum:
                            db.execute('UPDATE songs SET last_seen=? WHERE id=?', (now, existing['id']))
                        else:
                            db.execute('DELETE FROM songs WHERE id=?', (existing['id'],))
                            skipped_short += 1
                        continue
                    meta = self._metadata(path)
                    if meta['duration'] < minimum:
                        skipped_short += 1
                        if existing:
                            db.execute('DELETE FROM songs WHERE id=?', (existing['id'],))
                        continue
                    db.execute('''
                        INSERT INTO songs(path,title,artist,album,genre,track,duration,added_at,modified_at,last_seen,
                            year,disc,bitrate,sample_rate,channels,file_size,bpm,musical_key,mood,energy)
                        VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
                        ON CONFLICT(path) DO UPDATE SET title=excluded.title,artist=excluded.artist,album=excluded.album,
                            genre=excluded.genre,track=excluded.track,duration=excluded.duration,modified_at=excluded.modified_at,
                            last_seen=excluded.last_seen,year=excluded.year,disc=excluded.disc,bitrate=excluded.bitrate,
                            sample_rate=excluded.sample_rate,channels=excluded.channels,file_size=excluded.file_size,
                            bpm=excluded.bpm,musical_key=excluded.musical_key,mood=excluded.mood,energy=excluded.energy
                    ''', (
                        str(path), meta['title'], meta['artist'], meta['album'], meta['genre'], meta['track'], meta['duration'],
                        now, int(stat.st_mtime), now, meta['year'], meta['disc'], meta['bitrate'], meta['sample_rate'],
                        meta['channels'], int(stat.st_size), meta['bpm'], meta['musical_key'], meta['mood'], meta['energy']
                    ))
                    updated += 1
                except Exception:
                    continue
            if roots and not scan_all:
                clauses = ' OR '.join('path LIKE ?' for _ in roots)
                args = [now] + [r.rstrip('\\/') + os.sep + '%' for r in roots]
                db.execute(f'DELETE FROM songs WHERE last_seen<>? AND ({clauses})', args)
        return {'found': found, 'updated': updated, 'skippedShort': skipped_short, 'roots': len(roots)}

    def _metadata(self, path: Path) -> dict[str, Any]:
        result: dict[str, Any] = {
            'title': path.stem, 'artist': '', 'album': '', 'genre': '', 'track': 0, 'duration': 0.0,
            'year': 0, 'disc': 0, 'bitrate': 0, 'sample_rate': 0, 'channels': 0,
            'bpm': 0.0, 'musical_key': '', 'mood': '', 'energy': 0.0,
        }
        try:
            audio = MutagenFile(path, easy=True)
            if audio is not None:
                tags = audio.tags or {}
                result.update({
                    'title': _first(tags.get('title'), path.stem), 'artist': _first(tags.get('artist')),
                    'album': _first(tags.get('album')), 'genre': _first(tags.get('genre')),
                    'track': _track_number(tags.get('tracknumber')), 'year': _year(tags.get('date')),
                    'disc': _track_number(tags.get('discnumber')), 'bpm': float(_first(tags.get('bpm'), '0') or 0),
                    'musical_key': _first(tags.get('initialkey')), 'mood': _first(tags.get('mood')),
                })
                info = getattr(audio, 'info', None)
                result['duration'] = float(getattr(info, 'length', 0.0) or 0.0)
                result['bitrate'] = int(getattr(info, 'bitrate', 0) or 0)
                result['sample_rate'] = int(getattr(info, 'sample_rate', 0) or 0)
                result['channels'] = int(getattr(info, 'channels', 0) or 0)
        except Exception:
            pass
        return result

    @staticmethod
    def _song_dict(row: sqlite3.Row) -> dict[str, Any]:
        return {
            'id': row['id'], 'path': row['path'], 'title': row['title'], 'artist': row['artist'], 'album': row['album'],
            'genre': row['genre'], 'track': row['track'], 'duration': row['duration'], 'favorite': bool(row['favorite']),
            'addedAt': row['added_at'], 'year': row['year'], 'disc': row['disc'], 'bitrate': row['bitrate'],
            'sampleRate': row['sample_rate'], 'channels': row['channels'], 'fileSize': row['file_size'],
            'hidden': bool(row['hidden']), 'playCount': row['play_count'], 'skipCount': row['skip_count'],
            'lastPlayed': row['last_played'], 'bpm': row['bpm'], 'key': row['musical_key'], 'mood': row['mood'],
            'energy': row['energy'], 'cover': f"/cover/{row['id']}"
        }

    def library(self, query: str = '', favorite: bool | None = None, include_hidden: bool = False, limit: int = 10000) -> list[dict[str, Any]]:
        sql, args, clauses = 'SELECT * FROM songs', [], []
        if query:
            like = f'%{query}%'
            clauses.append('(title LIKE ? OR artist LIKE ? OR album LIKE ? OR genre LIKE ? OR mood LIKE ? OR musical_key LIKE ?)')
            args.extend([like] * 6)
        if favorite is not None:
            clauses.append('favorite=?'); args.append(1 if favorite else 0)
        if not include_hidden:
            clauses.append('hidden=0')
        if clauses:
            sql += ' WHERE ' + ' AND '.join(clauses)
        sql += ' ORDER BY artist COLLATE NOCASE,album COLLATE NOCASE,disc,track,title COLLATE NOCASE LIMIT ?'
        args.append(limit)
        with self._connect() as db:
            return [self._song_dict(r) for r in db.execute(sql, args)]

    def song_path(self, song_id: int) -> Path | None:
        with self._connect() as db:
            row = db.execute('SELECT path FROM songs WHERE id=?', (song_id,)).fetchone()
            return Path(row['path']) if row else None

    def set_favorite(self, song_id: int, favorite: bool) -> None:
        with self._connect() as db:
            db.execute('UPDATE songs SET favorite=? WHERE id=?', (1 if favorite else 0, song_id))

    def set_hidden(self, song_id: int, hidden: bool) -> None:
        with self._connect() as db:
            db.execute('UPDATE songs SET hidden=? WHERE id=?', (1 if hidden else 0, song_id))

    def pins(self) -> list[dict[str, Any]]:
        with self._connect() as db:
            return [dict(r) for r in db.execute('SELECT kind,item_key AS itemKey,position,created_at AS createdAt FROM pins ORDER BY position,created_at')]

    def set_pin(self, kind: str, item_key: str, pinned: bool) -> None:
        if kind not in {'album','artist','genre'} or not item_key.strip(): raise ValueError('Invalid collection pin')
        with self._connect() as db:
            if pinned:
                position=db.execute('SELECT COALESCE(MAX(position),-1)+1 FROM pins').fetchone()[0]
                db.execute('INSERT INTO pins(kind,item_key,position,created_at) VALUES(?,?,?,?) ON CONFLICT(kind,item_key) DO NOTHING',(kind,item_key.strip(),position,int(time.time())))
            else: db.execute('DELETE FROM pins WHERE kind=? AND item_key=?',(kind,item_key.strip()))

    def add_history(self, song_id: int, skipped: bool = False) -> None:
        now = int(time.time())
        with self._connect() as db:
            db.execute('INSERT INTO history(song_id,played_at) VALUES(?,?)', (song_id, now))
            db.execute('UPDATE songs SET play_count=play_count+?,skip_count=skip_count+?,last_played=? WHERE id=?', (0 if skipped else 1, 1 if skipped else 0, now, song_id))
            db.execute('DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY played_at DESC LIMIT 2000)')

    def history(self, limit: int = 100) -> list[dict[str, Any]]:
        with self._connect() as db:
            rows = db.execute('''SELECT s.* FROM history h JOIN songs s ON s.id=h.song_id WHERE s.hidden=0
                GROUP BY s.id ORDER BY MAX(h.played_at) DESC LIMIT ?''', (limit,)).fetchall()
            return [self._song_dict(r) for r in rows]

    def stats(self) -> dict[str, Any]:
        with self._connect() as db:
            row = db.execute('SELECT COUNT(*) songs,COALESCE(SUM(duration),0) duration,COALESCE(SUM(file_size),0) bytes FROM songs WHERE hidden=0').fetchone()
            return {'songs': row['songs'], 'duration': row['duration'], 'bytes': row['bytes'],
                    'favorites': db.execute('SELECT COUNT(*) FROM songs WHERE favorite=1 AND hidden=0').fetchone()[0],
                    'playlists': db.execute('SELECT COUNT(*) FROM playlists WHERE hidden=0').fetchone()[0]}

    def recent_searches(self, limit: int = 8) -> list[str]:
        with self._connect() as db:
            return [r['query'] for r in db.execute(
                'SELECT query FROM recent_searches ORDER BY searched_at DESC LIMIT ?',
                (max(1, min(int(limit), 30)),),
            )]

    def add_recent_search(self, query: str) -> list[str]:
        clean = ' '.join(str(query).strip().split())[:160]
        if clean:
            with self._connect() as db:
                db.execute('INSERT INTO recent_searches(query,searched_at) VALUES(?,?) '
                           'ON CONFLICT(query) DO UPDATE SET searched_at=excluded.searched_at',
                           (clean, time.time_ns()))
                db.execute('DELETE FROM recent_searches WHERE query NOT IN '
                           '(SELECT query FROM recent_searches ORDER BY searched_at DESC LIMIT 30)')
        return self.recent_searches()

    def clear_recent_searches(self) -> None:
        with self._connect() as db:
            db.execute('DELETE FROM recent_searches')

    def playlist_folders(self) -> list[dict[str, Any]]:
        with self._connect() as db:
            rows = db.execute('SELECT * FROM playlist_folders ORDER BY pinned DESC,position,name COLLATE NOCASE').fetchall()
            return [dict(r) | {'pinned': bool(r['pinned']), 'hidden': bool(r['hidden'])} for r in rows]

    def create_playlist_folder(self, name: str, parent_id: int | None = None) -> dict[str, Any]:
        with self._connect() as db:
            pos = db.execute('SELECT COALESCE(MAX(position),-1)+1 FROM playlist_folders WHERE parent_id IS ?', (parent_id,)).fetchone()[0]
            fid = int(db.execute('INSERT INTO playlist_folders(parent_id,name,position,created_at) VALUES(?,?,?,?)', (parent_id, name.strip() or 'New folder', pos, int(time.time()))).lastrowid)
        return {'id': fid, 'name': name.strip() or 'New folder', 'parent_id': parent_id, 'position': pos, 'pinned': False, 'hidden': False}

    def patch_playlist_folder(self, folder_id: int, patch: dict[str, Any]) -> None:
        allowed = {'name', 'parent_id', 'position', 'pinned', 'hidden'}
        sets, args = [], []
        for key, value in patch.items():
            if key in allowed:
                sets.append(f'{key}=?'); args.append(int(value) if key in {'position','pinned','hidden'} and value is not None else value)
        if sets:
            with self._connect() as db:
                db.execute(f"UPDATE playlist_folders SET {','.join(sets)} WHERE id=?", args + [folder_id])

    def delete_playlist_folder(self, folder_id: int) -> None:
        with self._connect() as db:
            db.execute('UPDATE playlists SET folder_id=NULL WHERE folder_id=?', (folder_id,))
            db.execute('DELETE FROM playlist_folders WHERE id=?', (folder_id,))

    def playlists(self, include_hidden: bool = False) -> list[dict[str, Any]]:
        with self._connect() as db:
            where = '' if include_hidden else 'WHERE p.hidden=0'
            rows = db.execute(f'''SELECT p.id,p.name,p.created_at,p.folder_id,p.pinned,p.hidden,p.sort_mode,p.sort_desc,p.view_mode,
                COUNT(ps.song_id) count FROM playlists p LEFT JOIN playlist_songs ps ON ps.playlist_id=p.id {where}
                GROUP BY p.id ORDER BY p.pinned DESC,p.created_at DESC''')
            return [{'id': r['id'], 'name': r['name'], 'count': r['count'], 'createdAt': r['created_at'],
                     'folderId': r['folder_id'], 'pinned': bool(r['pinned']), 'hidden': bool(r['hidden']),
                     'sortMode': r['sort_mode'], 'sortDesc': bool(r['sort_desc']), 'viewMode': r['view_mode']} for r in rows]

    def create_playlist(self, name: str, folder_id: int | None = None) -> dict[str, Any]:
        name = name.strip() or 'New playlist'
        with self._connect() as db:
            pid = int(db.execute('INSERT INTO playlists(name,created_at,folder_id) VALUES(?,?,?)', (name, int(time.time()), folder_id)).lastrowid)
        return {'id': pid, 'name': name, 'count': 0, 'folderId': folder_id}

    def patch_playlist(self, playlist_id: int, patch: dict[str, Any]) -> None:
        mapping = {'folderId': 'folder_id', 'sortMode': 'sort_mode', 'sortDesc': 'sort_desc', 'viewMode': 'view_mode',
                   'name': 'name', 'pinned': 'pinned', 'hidden': 'hidden'}
        sets, args = [], []
        for key, value in patch.items():
            if key in mapping:
                column = mapping[key]
                if key in {'pinned', 'hidden', 'sortDesc'}: value = 1 if value else 0
                sets.append(f'{column}=?'); args.append(value)
        if sets:
            with self._connect() as db:
                db.execute(f"UPDATE playlists SET {','.join(sets)} WHERE id=?", args + [playlist_id])

    def playlist(self, playlist_id: int) -> dict[str, Any] | None:
        with self._connect() as db:
            p = db.execute('SELECT * FROM playlists WHERE id=?', (playlist_id,)).fetchone()
            if not p: return None
            order = 'ps.position'
            if p['sort_mode'] == 'title': order = 's.title COLLATE NOCASE'
            elif p['sort_mode'] == 'artist': order = 's.artist COLLATE NOCASE,s.title COLLATE NOCASE'
            elif p['sort_mode'] == 'album': order = 's.album COLLATE NOCASE,s.track'
            elif p['sort_mode'] == 'added': order = 's.added_at'
            elif p['sort_mode'] == 'duration': order = 's.duration'
            if p['sort_desc']: order += ' DESC'
            songs = db.execute(f'''SELECT s.* FROM playlist_songs ps JOIN songs s ON s.id=ps.song_id
                WHERE ps.playlist_id=? AND ps.hidden=0 AND s.hidden=0 ORDER BY {order}''', (playlist_id,)).fetchall()
            return {'id': p['id'], 'name': p['name'], 'folderId': p['folder_id'], 'pinned': bool(p['pinned']),
                    'hidden': bool(p['hidden']), 'sortMode': p['sort_mode'], 'sortDesc': bool(p['sort_desc']),
                    'viewMode': p['view_mode'], 'songs': [self._song_dict(s) for s in songs]}

    def add_playlist_song(self, playlist_id: int, song_id: int) -> None:
        with self._connect() as db:
            pos = db.execute('SELECT COALESCE(MAX(position),-1)+1 FROM playlist_songs WHERE playlist_id=?', (playlist_id,)).fetchone()[0]
            db.execute('INSERT OR IGNORE INTO playlist_songs(playlist_id,song_id,position) VALUES(?,?,?)', (playlist_id, song_id, pos))

    def remove_playlist_song(self, playlist_id: int, song_id: int) -> None:
        with self._connect() as db:
            db.execute('DELETE FROM playlist_songs WHERE playlist_id=? AND song_id=?', (playlist_id, song_id))
            rows = db.execute('SELECT song_id FROM playlist_songs WHERE playlist_id=? ORDER BY position', (playlist_id,)).fetchall()
            for i, row in enumerate(rows):
                db.execute('UPDATE playlist_songs SET position=? WHERE playlist_id=? AND song_id=?', (i, playlist_id, row['song_id']))

    def reorder_playlist(self, playlist_id: int, song_ids: list[int]) -> None:
        with self._connect() as db:
            known = {int(r['song_id']) for r in db.execute(
                'SELECT song_id FROM playlist_songs WHERE playlist_id=?', (playlist_id,))}
            ordered: list[int] = []
            for value in song_ids:
                song_id = int(value)
                if song_id in known and song_id not in ordered:
                    ordered.append(song_id)
            ordered.extend(song_id for song_id in known if song_id not in ordered)
            for position, song_id in enumerate(ordered):
                db.execute('UPDATE playlist_songs SET position=? WHERE playlist_id=? AND song_id=?',
                           (position, playlist_id, song_id))

    def delete_playlist(self, playlist_id: int) -> None:
        with self._connect() as db:
            db.execute('DELETE FROM playlists WHERE id=?', (playlist_id,))

    def import_m3u8(self, file_path: str, name: str | None = None) -> dict[str, Any]:
        path = Path(file_path)
        if not path.is_file(): raise ValueError('Playlist file not found')
        entries = [line.strip() for line in path.read_text(encoding='utf-8-sig', errors='replace').splitlines() if line.strip() and not line.startswith('#')]
        playlist = self.create_playlist(name or path.stem)
        with self._connect() as db:
            for entry in entries:
                candidate = Path(entry)
                if not candidate.is_absolute(): candidate = (path.parent / candidate).resolve()
                row = db.execute('SELECT id FROM songs WHERE path=?', (str(candidate),)).fetchone()
                if row: self.add_playlist_song(playlist['id'], int(row['id']))
        return self.playlist(playlist['id']) or playlist

    def export_m3u8(self, playlist_id: int, file_path: str) -> str:
        item = self.playlist(playlist_id)
        if not item: raise ValueError('Playlist not found')
        path = Path(file_path)
        path.parent.mkdir(parents=True, exist_ok=True)
        lines = ['#EXTM3U']
        for song in item['songs']:
            lines.append(f"#EXTINF:{int(song['duration'])},{song['artist']} - {song['title']}")
            lines.append(song['path'])
        path.write_text('\n'.join(lines) + '\n', encoding='utf-8')
        return str(path)

    def queue(self) -> list[dict[str, Any]]:
        with self._connect() as db:
            return [self._song_dict(r) for r in db.execute('SELECT s.* FROM queue q JOIN songs s ON s.id=q.song_id WHERE s.hidden=0 ORDER BY q.position')]

    def set_queue(self, song_ids: list[int]) -> None:
        with self._connect() as db:
            db.execute('DELETE FROM queue')
            valid = {int(r['id']) for r in db.execute('SELECT id FROM songs WHERE hidden=0')}
            filtered = [int(sid) for sid in song_ids if int(sid) in valid]
            db.executemany('INSERT INTO queue(position,song_id) VALUES(?,?)', [(i, sid) for i, sid in enumerate(filtered)])

    def cache_status(self) -> dict[str, int]:
        cache_dir = self.data_dir / 'cache'
        files = [p for p in cache_dir.rglob('*') if p.is_file()] if cache_dir.exists() else []
        return {'files': len(files), 'bytes': sum(p.stat().st_size for p in files)}

    def clear_cache(self) -> dict[str, int]:
        cache_dir = self.data_dir / 'cache'
        removed = 0
        if cache_dir.exists():
            for path in sorted(cache_dir.rglob('*'), key=lambda p: len(p.parts), reverse=True):
                try:
                    if path.is_file():
                        path.unlink(); removed += 1
                    elif path.is_dir():
                        path.rmdir()
                except OSError:
                    pass
        return {'removed': removed, **self.cache_status()}

    def lyrics(self, song_id: int) -> dict[str, Any]:
        with self._connect() as db:
            row = db.execute('SELECT * FROM lyrics WHERE song_id=?', (song_id,)).fetchone()
        data = dict(row) if row else {'song_id': song_id, 'plain': '', 'lrc': '', 'translation': '', 'romanization': '', 'updated_at': 0}
        if not data.get('lrc') and not data.get('plain'):
            path = self.song_path(song_id)
            if path:
                lrc = path.with_suffix('.lrc')
                txt = path.with_suffix('.txt')
                try:
                    if lrc.is_file(): data['lrc'] = lrc.read_text(encoding='utf-8-sig', errors='replace')
                    if txt.is_file(): data['plain'] = txt.read_text(encoding='utf-8-sig', errors='replace')
                except Exception: pass
        return {'songId': song_id, 'plain': data.get('plain',''), 'lrc': data.get('lrc',''),
                'translation': data.get('translation',''), 'romanization': data.get('romanization',''), 'updatedAt': data.get('updated_at',0)}

    def save_lyrics(self, song_id: int, payload: dict[str, Any]) -> dict[str, Any]:
        now = int(time.time())
        values = [str(payload.get(k, '')) for k in ('plain','lrc','translation','romanization')]
        with self._connect() as db:
            db.execute('''INSERT INTO lyrics(song_id,plain,lrc,translation,romanization,updated_at) VALUES(?,?,?,?,?,?)
                ON CONFLICT(song_id) DO UPDATE SET plain=excluded.plain,lrc=excluded.lrc,translation=excluded.translation,
                romanization=excluded.romanization,updated_at=excluded.updated_at''', (song_id, *values, now))
        if self.settings().get('writeLyricsSidecar'):
            path = self.song_path(song_id)
            try:
                if path and values[1]: path.with_suffix('.lrc').write_text(values[1], encoding='utf-8')
                elif path and values[0]: path.with_suffix('.txt').write_text(values[0], encoding='utf-8')
            except Exception: pass
        return self.lyrics(song_id)

    def track_profile(self, song_id: int) -> dict[str, Any]:
        with self._connect() as db:
            row = db.execute('SELECT * FROM track_profiles WHERE song_id=?', (song_id,)).fetchone()
        if not row:
            return {'songId': song_id, 'accent': '', 'background': '', 'secondary': '', 'artworkPath': '', 'wallpaperPath': '',
                    'wallpaperOpacity': .28, 'wallpaperBlur': 18, 'canvasPath': '', 'canvasStart': 0, 'canvasEnd': 0,
                    'canvasSpeed': 1, 'visualizer': '', 'eq': [], 'bass': 0, 'virtualizer': 0, 'loudness': 0}
        return {'songId': song_id, 'accent': row['accent'], 'background': row['background'], 'secondary': row['secondary'],
                'artworkPath': row['artwork_path'], 'wallpaperPath': row['wallpaper_path'], 'wallpaperOpacity': row['wallpaper_opacity'],
                'wallpaperBlur': row['wallpaper_blur'], 'canvasPath': row['canvas_path'], 'canvasStart': row['canvas_start'],
                'canvasEnd': row['canvas_end'], 'canvasSpeed': row['canvas_speed'], 'visualizer': row['visualizer'],
                'eq': _safe_json(row['eq_json'], []), 'bass': row['bass'], 'virtualizer': row['virtualizer'], 'loudness': row['loudness']}

    def save_track_profile(self, song_id: int, p: dict[str, Any]) -> dict[str, Any]:
        current = self.track_profile(song_id) | p
        with self._connect() as db:
            db.execute('''INSERT INTO track_profiles(song_id,accent,background,secondary,artwork_path,wallpaper_path,wallpaper_opacity,
                wallpaper_blur,canvas_path,canvas_start,canvas_end,canvas_speed,visualizer,eq_json,bass,virtualizer,loudness)
                VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(song_id) DO UPDATE SET accent=excluded.accent,
                background=excluded.background,secondary=excluded.secondary,artwork_path=excluded.artwork_path,
                wallpaper_path=excluded.wallpaper_path,wallpaper_opacity=excluded.wallpaper_opacity,wallpaper_blur=excluded.wallpaper_blur,
                canvas_path=excluded.canvas_path,canvas_start=excluded.canvas_start,canvas_end=excluded.canvas_end,canvas_speed=excluded.canvas_speed,
                visualizer=excluded.visualizer,eq_json=excluded.eq_json,bass=excluded.bass,virtualizer=excluded.virtualizer,loudness=excluded.loudness''', (
                song_id, current.get('accent',''), current.get('background',''), current.get('secondary',''), current.get('artworkPath',''),
                current.get('wallpaperPath',''), float(current.get('wallpaperOpacity',.28)), int(current.get('wallpaperBlur',18)),
                current.get('canvasPath',''), float(current.get('canvasStart',0)), float(current.get('canvasEnd',0)), float(current.get('canvasSpeed',1)),
                current.get('visualizer',''), json.dumps(current.get('eq',[])), float(current.get('bass',0)),
                float(current.get('virtualizer',0)), float(current.get('loudness',0))))
        return self.track_profile(song_id)

    def smart_mix(self, seed_id: int | None = None, limit: int = 50, genre: str = '', mood: str = '') -> list[dict[str, Any]]:
        songs = self.library(limit=10000)
        seed = next((s for s in songs if s['id'] == seed_id), None)
        def score(song: dict[str, Any]) -> tuple[float, int]:
            value = song['playCount'] * .25 + (4 if song['favorite'] else 0) - song['skipCount'] * .4
            if seed and song['id'] != seed['id']:
                if song['artist'] and song['artist'] == seed['artist']: value += 4
                if song['genre'] and song['genre'] == seed['genre']: value += 3
                if song['album'] and song['album'] == seed['album']: value += 1.5
                if song['bpm'] and seed['bpm']: value += max(0, 2 - abs(song['bpm'] - seed['bpm']) / 10)
                if song['key'] and song['key'] == seed['key']: value += 1.5
            if genre and genre != 'all' and song['genre'].lower() != genre.lower(): value -= 100
            if mood and mood != 'all' and song['mood'].lower() != mood.lower(): value -= 100
            return value, -song['id']
        ranked = [s for s in songs if not seed or s['id'] != seed['id']]
        ranked.sort(key=score, reverse=True)
        return ranked[:max(1, min(limit, 500))]

    def backup(self) -> dict[str, Any]:
        with self._connect() as db:
            playlists = [dict(r) for r in db.execute('SELECT * FROM playlists')]
            playlist_songs = [dict(r) for r in db.execute('SELECT * FROM playlist_songs')]
            playlist_folders = [dict(r) for r in db.execute('SELECT * FROM playlist_folders')]
            lyrics = [dict(r) for r in db.execute('SELECT * FROM lyrics')]
            profiles = [dict(r) for r in db.execute('SELECT * FROM track_profiles')]
            song_user = [dict(r) for r in db.execute('SELECT path,favorite,hidden,play_count,skip_count,last_played FROM songs')]
            pins = [dict(r) for r in db.execute('SELECT * FROM pins ORDER BY position')]
        return {'version': 2, 'createdAt': int(time.time()), 'settings': self.settings(), 'folders': self.folders(),
                'playlists': playlists, 'playlistSongs': playlist_songs, 'playlistFolders': playlist_folders,
                'lyrics': lyrics, 'trackProfiles': profiles, 'songUser': song_user, 'pins': pins}

    def restore(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.patch_settings(payload.get('settings', {}))
        for folder in payload.get('folders', []):
            try: self.add_folder(folder)
            except Exception: pass
        with self._connect() as db:
            db.execute('DELETE FROM pins')
            for row in payload.get('pins', []):
                if row.get('kind') in {'album','artist','genre'} and row.get('item_key'):
                    db.execute('INSERT OR IGNORE INTO pins(kind,item_key,position,created_at) VALUES(?,?,?,?)',(row['kind'],row['item_key'],int(row.get('position',0)),int(row.get('created_at',time.time()))))
            for row in payload.get('songUser', []):
                db.execute('UPDATE songs SET favorite=?,hidden=?,play_count=?,skip_count=?,last_played=? WHERE path=?',
                    (row.get('favorite',0), row.get('hidden',0), row.get('play_count',0), row.get('skip_count',0), row.get('last_played',0), row.get('path','')))
        return {'ok': True}

    def cover(self, song_id: int) -> tuple[bytes, str] | None:
        profile = self.track_profile(song_id)
        custom = Path(profile.get('artworkPath','')) if profile.get('artworkPath') else None
        if custom and custom.is_file():
            return custom.read_bytes(), mimetypes.guess_type(custom.name)[0] or 'image/jpeg'
        path = self.song_path(song_id)
        if not path or not path.exists(): return None
        try:
            audio = MutagenFile(path)
            if audio is None: return None
            if hasattr(audio, 'pictures') and audio.pictures:
                pic = audio.pictures[0]; return bytes(pic.data), getattr(pic, 'mime', 'image/jpeg') or 'image/jpeg'
            tags = getattr(audio, 'tags', None)
            if tags:
                for key in tags.keys():
                    value = tags[key]
                    if key.startswith('APIC') and hasattr(value, 'data'): return bytes(value.data), getattr(value, 'mime', 'image/jpeg') or 'image/jpeg'
                covr = tags.get('covr') if hasattr(tags, 'get') else None
                if covr: return bytes(covr[0]), 'image/jpeg'
        except Exception: return None
        return None


@dataclass
class RunningServer:
    httpd: ThreadingHTTPServer
    thread: threading.Thread
    url: str
    def stop(self) -> None:
        self.httpd.shutdown(); self.httpd.server_close(); self.thread.join(timeout=5)


def create_server(store: NeoStore, web_root: str | os.PathLike[str], host: str = '127.0.0.1', port: int = 0) -> RunningServer:
    root = Path(web_root)
    class Handler(BaseHTTPRequestHandler):
        server_version = 'NEOPlayer/0.2'
        def log_message(self, fmt: str, *args: Any) -> None: return
        def _json(self, data: Any, status: int = 200) -> None:
            payload = json.dumps(data, ensure_ascii=False).encode('utf-8'); self.send_response(status)
            self.send_header('Content-Type', 'application/json; charset=utf-8'); self.send_header('Content-Length', str(len(payload))); self.end_headers(); self.wfile.write(payload)
        def _read_json(self) -> dict[str, Any]:
            length = int(self.headers.get('Content-Length', '0') or 0); return {} if length <= 0 else json.loads(self.rfile.read(length).decode('utf-8'))
        def _parts(self) -> list[str]: return [unquote(p) for p in urlparse(self.path).path.strip('/').split('/') if p]
        def do_GET(self) -> None:
            parsed, parts, qs = urlparse(self.path), self._parts(), parse_qs(urlparse(self.path).query)
            try:
                if parsed.path == '/api/health': return self._json({'ok': True, 'version': 2})
                if parsed.path == '/api/library': return self._json(store.library(qs.get('q',[''])[0], None, qs.get('hidden',['0'])[0] == '1'))
                if parsed.path == '/api/favorites': return self._json(store.library('', True))
                if parsed.path == '/api/history': return self._json(store.history(int(qs.get('limit',['100'])[0])))
                if parsed.path == '/api/stats': return self._json(store.stats())
                if parsed.path == '/api/recent-searches': return self._json(store.recent_searches(int(qs.get('limit',['8'])[0])))
                if parsed.path == '/api/cache': return self._json(store.cache_status())
                if parsed.path == '/api/folders': return self._json(store.folders())
                if parsed.path == '/api/system-roots': return self._json(store.system_roots())
                if parsed.path == '/api/pins': return self._json(store.pins())
                if parsed.path == '/api/playlists': return self._json(store.playlists(qs.get('hidden',['0'])[0] == '1'))
                if parsed.path == '/api/playlist-folders': return self._json(store.playlist_folders())
                if len(parts) == 3 and parts[:2] == ['api','playlists']:
                    item = store.playlist(int(parts[2])); return self._json(item or {'error':'not found'}, 200 if item else 404)
                if parsed.path == '/api/settings': return self._json(store.settings())
                if parsed.path == '/api/queue': return self._json(store.queue())
                if parsed.path == '/api/backup': return self._json(store.backup())
                if parsed.path == '/api/smart-mix': return self._json(store.smart_mix(int(qs['seed'][0]) if qs.get('seed') else None, int(qs.get('limit',['50'])[0]), qs.get('genre',[''])[0], qs.get('mood',[''])[0]))
                if len(parts) == 3 and parts[:2] == ['api','lyrics']: return self._json(store.lyrics(int(parts[2])))
                if len(parts) == 3 and parts[:2] == ['api','profiles']: return self._json(store.track_profile(int(parts[2])))
                if len(parts) == 2 and parts[0] == 'cover':
                    cover = store.cover(int(parts[1]))
                    data, mime = cover if cover else (PLACEHOLDER_COVER, 'image/svg+xml')
                    self.send_response(200); self.send_header('Content-Type', mime); self.send_header('Cache-Control','private, max-age=86400'); self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data); return
                if len(parts) == 2 and parts[0] == 'media':
                    path = store.song_path(int(parts[1])); return self.send_error(404) if not path or not path.exists() else self._serve_media(path)
                return self._serve_static(parsed.path)
            except Exception as ex: self._json({'error': str(ex)}, 500)
        def do_POST(self) -> None:
            parts = self._parts()
            try:
                body = self._read_json()
                if self.path == '/api/folders': return self._json({'path': store.add_folder(str(body.get('path','')))}, 201)
                if self.path == '/api/scan': return self._json(store.scan(False))
                if self.path == '/api/scan-system': return self._json(store.scan(True))
                if self.path == '/api/favorites': store.set_favorite(int(body['song_id']), bool(body['favorite'])); return self._json({'ok':True})
                if self.path == '/api/hide': store.set_hidden(int(body['song_id']), bool(body['hidden'])); return self._json({'ok':True})
                if self.path == '/api/pins': store.set_pin(str(body.get('kind','')),str(body.get('item_key','')),bool(body.get('pinned'))); return self._json({'ok':True})
                if self.path == '/api/playlists': return self._json(store.create_playlist(str(body.get('name','')), body.get('folder_id')), 201)
                if self.path == '/api/playlist-folders': return self._json(store.create_playlist_folder(str(body.get('name','')), body.get('parent_id')), 201)
                if len(parts) == 4 and parts[:2] == ['api','playlists'] and parts[3] == 'songs': store.add_playlist_song(int(parts[2]), int(body['song_id'])); return self._json({'ok':True})
                if self.path == '/api/history': store.add_history(int(body['song_id']), bool(body.get('skipped',False))); return self._json({'ok':True})
                if self.path == '/api/recent-searches': return self._json(store.add_recent_search(str(body.get('query',''))), 201)
                if self.path == '/api/cache/clear': return self._json(store.clear_cache())
                if self.path == '/api/import-m3u8': return self._json(store.import_m3u8(str(body['path']), body.get('name')), 201)
                if self.path == '/api/export-m3u8': return self._json({'path': store.export_m3u8(int(body['playlist_id']), str(body['path']))})
                if self.path == '/api/restore': return self._json(store.restore(body))
                if len(parts) == 3 and parts[:2] == ['api','lyrics']: return self._json(store.save_lyrics(int(parts[2]), body))
                if len(parts) == 3 and parts[:2] == ['api','profiles']: return self._json(store.save_track_profile(int(parts[2]), body))
                return self._json({'error':'not found'}, 404)
            except Exception as ex: self._json({'error': str(ex)}, 400)
        def do_PATCH(self) -> None:
            parts = self._parts()
            try:
                body = self._read_json()
                if self.path == '/api/settings': return self._json(store.patch_settings(body))
                if len(parts) == 3 and parts[:2] == ['api','playlists']: store.patch_playlist(int(parts[2]), body); return self._json({'ok':True})
                if len(parts) == 3 and parts[:2] == ['api','playlist-folders']: store.patch_playlist_folder(int(parts[2]), body); return self._json({'ok':True})
                return self._json({'error':'not found'}, 404)
            except Exception as ex: self._json({'error': str(ex)}, 400)
        def do_PUT(self) -> None:
            try:
                if self.path == '/api/queue': store.set_queue([int(x) for x in self._read_json().get('song_ids',[])]); return self._json({'ok':True})
                parts = self._parts()
                if len(parts) == 4 and parts[:2] == ['api','playlists'] and parts[3] == 'reorder':
                    body = self._read_json(); store.reorder_playlist(int(parts[2]), body.get('song_ids', [])); return self._json({'ok':True})
                return self._json({'error':'not found'}, 404)
            except Exception as ex: self._json({'error': str(ex)}, 400)
        def do_DELETE(self) -> None:
            parts = self._parts()
            try:
                if len(parts) == 3 and parts[:2] == ['api','folders']: store.remove_folder(unquote(parts[2])); return self._json({'ok':True})
                if len(parts) == 3 and parts[:2] == ['api','playlists']: store.delete_playlist(int(parts[2])); return self._json({'ok':True})
                if len(parts) == 3 and parts[:2] == ['api','playlist-folders']: store.delete_playlist_folder(int(parts[2])); return self._json({'ok':True})
                if len(parts) == 5 and parts[:2] == ['api','playlists'] and parts[3] == 'songs': store.remove_playlist_song(int(parts[2]), int(parts[4])); return self._json({'ok':True})
                if self.path == '/api/recent-searches': store.clear_recent_searches(); return self._json({'ok':True})
                return self._json({'error':'not found'}, 404)
            except Exception as ex: self._json({'error': str(ex)}, 400)
        def _serve_media(self, path: Path) -> None:
            size = path.stat().st_size; ctype = mimetypes.guess_type(path.name)[0] or 'application/octet-stream'; start, end, status = 0, size-1, 200
            header = self.headers.get('Range')
            if header and header.startswith('bytes='):
                status = 206; left, _, right = header[6:].split(',')[0].partition('-'); start = int(left) if left else 0; end = min(int(right), size-1) if right else size-1
            length = max(0, end-start+1); self.send_response(status); self.send_header('Content-Type', ctype); self.send_header('Accept-Ranges','bytes'); self.send_header('Content-Length', str(length))
            if status == 206: self.send_header('Content-Range', f'bytes {start}-{end}/{size}')
            self.end_headers()
            with path.open('rb') as file:
                file.seek(start); remaining = length
                while remaining:
                    chunk = file.read(min(262144, remaining))
                    if not chunk: break
                    self.wfile.write(chunk); remaining -= len(chunk)
        def _serve_static(self, url_path: str) -> None:
            if not root.exists(): return self._json({'error':'frontend not built'}, 503)
            rel = unquote(url_path.lstrip('/')) or 'index.html'; candidate = (root / rel).resolve(); resolved_root = root.resolve()
            if resolved_root not in candidate.parents and candidate != resolved_root: return self.send_error(403)
            if not candidate.is_file(): candidate = root / 'index.html'
            data = candidate.read_bytes(); ctype = mimetypes.guess_type(candidate.name)[0] or 'application/octet-stream'; self.send_response(200); self.send_header('Content-Type',ctype); self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
    httpd = ThreadingHTTPServer((host, port), Handler); actual_port = int(httpd.server_address[1]); thread = threading.Thread(target=httpd.serve_forever, name='neo-http', daemon=True); thread.start(); return RunningServer(httpd, thread, f'http://{host}:{actual_port}')
