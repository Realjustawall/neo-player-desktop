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

AUDIO_EXTENSIONS = {'.mp3', '.flac', '.wav', '.m4a', '.aac', '.ogg', '.opus', '.wma'}


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
                CREATE TABLE IF NOT EXISTS playlists(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, created_at INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS playlist_songs(
                    playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
                    song_id INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE,
                    position INTEGER NOT NULL, PRIMARY KEY(playlist_id, song_id)
                );
                CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS queue(position INTEGER PRIMARY KEY, song_id INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE);
                CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY AUTOINCREMENT, song_id INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE, played_at INTEGER NOT NULL);
            ''')
            if db.execute('SELECT COUNT(*) FROM settings').fetchone()[0] == 0:
                defaults = {'theme': 'tangerine', 'volume': 0.82, 'shuffle': False, 'repeat': 'off', 'playbackRate': 1.0, 'crossfade': 0}
                db.executemany('INSERT INTO settings(key,value) VALUES(?,?)', [(k, json.dumps(v)) for k, v in defaults.items()])

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

    def scan(self) -> dict[str, int]:
        roots = self.folders()
        now = int(time.time())
        found = updated = 0
        with self._connect() as db:
            for root in roots:
                for base, _, files in os.walk(root):
                    for name in files:
                        path = Path(base) / name
                        if path.suffix.lower() not in AUDIO_EXTENSIONS:
                            continue
                        found += 1
                        try:
                            stat = path.stat()
                            existing = db.execute('SELECT id,modified_at FROM songs WHERE path=?', (str(path),)).fetchone()
                            if existing and int(existing['modified_at']) == int(stat.st_mtime):
                                db.execute('UPDATE songs SET last_seen=? WHERE id=?', (now, existing['id']))
                                continue
                            title, artist, album, genre, track, duration = self._metadata(path)
                            db.execute('''
                                INSERT INTO songs(path,title,artist,album,genre,track,duration,added_at,modified_at,last_seen)
                                VALUES(?,?,?,?,?,?,?,?,?,?)
                                ON CONFLICT(path) DO UPDATE SET title=excluded.title, artist=excluded.artist,
                                album=excluded.album, genre=excluded.genre, track=excluded.track, duration=excluded.duration,
                                modified_at=excluded.modified_at, last_seen=excluded.last_seen
                            ''', (str(path), title, artist, album, genre, track, duration, now, int(stat.st_mtime), now))
                            updated += 1
                        except Exception:
                            continue
            if roots:
                db.execute('DELETE FROM songs WHERE last_seen<>? AND (' + ' OR '.join('path LIKE ?' for _ in roots) + ')', [now] + [r.rstrip('\\/') + os.sep + '%' for r in roots])
        return {'found': found, 'updated': updated}

    def _metadata(self, path: Path) -> tuple[str, str, str, str, int, float]:
        title, artist, album, genre, track, duration = path.stem, '', '', '', 0, 0.0
        try:
            audio = MutagenFile(path, easy=True)
            if audio is not None:
                tags = audio.tags or {}
                title = _first(tags.get('title'), title)
                artist = _first(tags.get('artist'))
                album = _first(tags.get('album'))
                genre = _first(tags.get('genre'))
                track = _track_number(tags.get('tracknumber'))
                duration = float(getattr(getattr(audio, 'info', None), 'length', 0.0) or 0.0)
        except Exception:
            pass
        return title, artist, album, genre, track, duration

    @staticmethod
    def _song_dict(row: sqlite3.Row) -> dict[str, Any]:
        return {'id': row['id'], 'path': row['path'], 'title': row['title'], 'artist': row['artist'], 'album': row['album'],
                'genre': row['genre'], 'track': row['track'], 'duration': row['duration'], 'favorite': bool(row['favorite']),
                'addedAt': row['added_at'], 'cover': f"/cover/{row['id']}"}

    def library(self, query: str = '', favorite: bool | None = None, limit: int = 5000) -> list[dict[str, Any]]:
        sql, args, clauses = 'SELECT * FROM songs', [], []
        if query:
            like = f'%{query}%'; clauses.append('(title LIKE ? OR artist LIKE ? OR album LIKE ? OR genre LIKE ?)'); args.extend([like] * 4)
        if favorite is not None:
            clauses.append('favorite=?'); args.append(1 if favorite else 0)
        if clauses:
            sql += ' WHERE ' + ' AND '.join(clauses)
        sql += ' ORDER BY artist COLLATE NOCASE, album COLLATE NOCASE, track, title COLLATE NOCASE LIMIT ?'; args.append(limit)
        with self._connect() as db:
            return [self._song_dict(r) for r in db.execute(sql, args)]

    def song_path(self, song_id: int) -> Path | None:
        with self._connect() as db:
            row = db.execute('SELECT path FROM songs WHERE id=?', (song_id,)).fetchone()
            return Path(row['path']) if row else None

    def set_favorite(self, song_id: int, favorite: bool) -> None:
        with self._connect() as db:
            db.execute('UPDATE songs SET favorite=? WHERE id=?', (1 if favorite else 0, song_id))

    def playlists(self) -> list[dict[str, Any]]:
        with self._connect() as db:
            rows = db.execute('''SELECT p.id,p.name,p.created_at,COUNT(ps.song_id) count FROM playlists p LEFT JOIN playlist_songs ps ON ps.playlist_id=p.id GROUP BY p.id ORDER BY p.created_at DESC''')
            return [{'id': r['id'], 'name': r['name'], 'count': r['count'], 'createdAt': r['created_at']} for r in rows]

    def create_playlist(self, name: str) -> dict[str, Any]:
        name = name.strip() or 'New playlist'
        with self._connect() as db:
            pid = int(db.execute('INSERT INTO playlists(name,created_at) VALUES(?,?)', (name, int(time.time()))).lastrowid)
        return {'id': pid, 'name': name, 'count': 0}

    def playlist(self, playlist_id: int) -> dict[str, Any] | None:
        with self._connect() as db:
            p = db.execute('SELECT * FROM playlists WHERE id=?', (playlist_id,)).fetchone()
            if not p: return None
            songs = db.execute('SELECT s.* FROM playlist_songs ps JOIN songs s ON s.id=ps.song_id WHERE ps.playlist_id=? ORDER BY ps.position', (playlist_id,)).fetchall()
            return {'id': p['id'], 'name': p['name'], 'songs': [self._song_dict(s) for s in songs]}

    def add_playlist_song(self, playlist_id: int, song_id: int) -> None:
        with self._connect() as db:
            pos = db.execute('SELECT COALESCE(MAX(position),-1)+1 FROM playlist_songs WHERE playlist_id=?', (playlist_id,)).fetchone()[0]
            db.execute('INSERT OR IGNORE INTO playlist_songs(playlist_id,song_id,position) VALUES(?,?,?)', (playlist_id, song_id, pos))

    def remove_playlist_song(self, playlist_id: int, song_id: int) -> None:
        with self._connect() as db:
            db.execute('DELETE FROM playlist_songs WHERE playlist_id=? AND song_id=?', (playlist_id, song_id))
            rows = db.execute('SELECT song_id FROM playlist_songs WHERE playlist_id=? ORDER BY position', (playlist_id,)).fetchall()
            for i, row in enumerate(rows): db.execute('UPDATE playlist_songs SET position=? WHERE playlist_id=? AND song_id=?', (i, playlist_id, row['song_id']))

    def delete_playlist(self, playlist_id: int) -> None:
        with self._connect() as db: db.execute('DELETE FROM playlists WHERE id=?', (playlist_id,))

    def settings(self) -> dict[str, Any]:
        with self._connect() as db: return {r['key']: json.loads(r['value']) for r in db.execute('SELECT key,value FROM settings')}

    def patch_settings(self, patch: dict[str, Any]) -> dict[str, Any]:
        allowed = {'theme', 'volume', 'shuffle', 'repeat', 'playbackRate', 'crossfade'}
        with self._connect() as db:
            for key, value in patch.items():
                if key in allowed: db.execute('INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value', (key, json.dumps(value)))
        return self.settings()

    def queue(self) -> list[dict[str, Any]]:
        with self._connect() as db:
            return [self._song_dict(r) for r in db.execute('SELECT s.* FROM queue q JOIN songs s ON s.id=q.song_id ORDER BY q.position')]

    def set_queue(self, song_ids: list[int]) -> None:
        with self._connect() as db:
            db.execute('DELETE FROM queue'); db.executemany('INSERT INTO queue(position,song_id) VALUES(?,?)', [(i, int(sid)) for i, sid in enumerate(song_ids)])

    def add_history(self, song_id: int) -> None:
        with self._connect() as db:
            db.execute('INSERT INTO history(song_id,played_at) VALUES(?,?)', (song_id, int(time.time())))
            db.execute('DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY played_at DESC LIMIT 1000)')

    def cover(self, song_id: int) -> tuple[bytes, str] | None:
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
        server_version = 'NEOPlayer/0.1'
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
                if parsed.path == '/api/health': return self._json({'ok': True})
                if parsed.path == '/api/library': return self._json(store.library(qs.get('q', [''])[0], None))
                if parsed.path == '/api/favorites': return self._json(store.library('', True))
                if parsed.path == '/api/folders': return self._json(store.folders())
                if parsed.path == '/api/playlists': return self._json(store.playlists())
                if len(parts) == 3 and parts[:2] == ['api','playlists']:
                    item = store.playlist(int(parts[2])); return self._json(item or {'error':'not found'}, 200 if item else 404)
                if parsed.path == '/api/settings': return self._json(store.settings())
                if parsed.path == '/api/queue': return self._json(store.queue())
                if len(parts) == 2 and parts[0] == 'cover':
                    cover = store.cover(int(parts[1]))
                    if not cover: return self.send_error(404)
                    data, mime = cover; self.send_response(200); self.send_header('Content-Type', mime); self.send_header('Cache-Control','private, max-age=86400'); self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data); return
                if len(parts) == 2 and parts[0] == 'media':
                    path = store.song_path(int(parts[1])); return self.send_error(404) if not path or not path.exists() else self._serve_media(path)
                return self._serve_static(parsed.path)
            except Exception as ex: self._json({'error': str(ex)}, 500)
        def do_POST(self) -> None:
            parts = self._parts()
            try:
                body = self._read_json()
                if self.path == '/api/folders': return self._json({'path': store.add_folder(str(body.get('path','')))}, 201)
                if self.path == '/api/scan': return self._json(store.scan())
                if self.path == '/api/favorites': store.set_favorite(int(body['song_id']), bool(body['favorite'])); return self._json({'ok':True})
                if self.path == '/api/playlists': return self._json(store.create_playlist(str(body.get('name',''))), 201)
                if len(parts) == 4 and parts[:2] == ['api','playlists'] and parts[3] == 'songs': store.add_playlist_song(int(parts[2]), int(body['song_id'])); return self._json({'ok':True})
                if self.path == '/api/history': store.add_history(int(body['song_id'])); return self._json({'ok':True})
                return self._json({'error':'not found'}, 404)
            except Exception as ex: self._json({'error': str(ex)}, 400)
        def do_PATCH(self) -> None:
            try: return self._json(store.patch_settings(self._read_json())) if self.path == '/api/settings' else self._json({'error':'not found'}, 404)
            except Exception as ex: self._json({'error': str(ex)}, 400)
        def do_PUT(self) -> None:
            try:
                if self.path == '/api/queue': store.set_queue([int(x) for x in self._read_json().get('song_ids',[])]); return self._json({'ok':True})
                return self._json({'error':'not found'}, 404)
            except Exception as ex: self._json({'error': str(ex)}, 400)
        def do_DELETE(self) -> None:
            parts = self._parts()
            try:
                if len(parts) == 3 and parts[:2] == ['api','folders']: store.remove_folder(unquote(parts[2])); return self._json({'ok':True})
                if len(parts) == 3 and parts[:2] == ['api','playlists']: store.delete_playlist(int(parts[2])); return self._json({'ok':True})
                if len(parts) == 5 and parts[:2] == ['api','playlists'] and parts[3] == 'songs': store.remove_playlist_song(int(parts[2]), int(parts[4])); return self._json({'ok':True})
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
