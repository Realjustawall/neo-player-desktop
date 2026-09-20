import os
import sqlite3
import time
from concurrent.futures import ThreadPoolExecutor

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


def _fast_scan(self, scan_all=False):
    roots = self.system_roots() if scan_all else self.folders()
    now = int(time.time())
    minimum = int(self.settings().get('minDurationMs', 10_000)) / 1000.0
    found = 0
    updated = 0
    skipped_short = 0
    pending = []
    seen_updates = []
    delete_ids = []

    with self._connect() as db:
        existing = {
            row['path']: {
                'id': row['id'],
                'modified_at': int(row['modified_at'] or 0),
                'duration': float(row['duration'] or 0),
            }
            for row in db.execute("SELECT id,path,modified_at,duration FROM songs WHERE source_type='file'")
        }

    for path in self._walk_audio(roots, scan_all=scan_all):
        found += 1
        try:
            stat = path.stat()
        except OSError:
            continue
        key = str(path)
        previous = existing.get(key)
        if previous and previous['modified_at'] == int(stat.st_mtime):
            if previous['duration'] >= minimum:
                seen_updates.append((now, previous['id']))
            else:
                delete_ids.append((previous['id'],))
                skipped_short += 1
            continue
        pending.append((path, stat, previous))

    def read_metadata(item):
        path, stat, previous = item
        try:
            return path, stat, previous, self._metadata(path)
        except Exception:
            return path, stat, previous, None

    workers = max(2, min(16, (os.cpu_count() or 4) * 2))
    with ThreadPoolExecutor(max_workers=workers, thread_name_prefix='neo-scan') as pool:
        parsed = list(pool.map(read_metadata, pending, chunksize=8))

    rows = []
    for path, stat, previous, meta in parsed:
        if not meta:
            continue
        if float(meta.get('duration') or 0) < minimum:
            skipped_short += 1
            if previous:
                delete_ids.append((previous['id'],))
            continue
        rows.append((
            str(path), meta['title'], meta['artist'], meta['album'], meta['genre'], meta['track'], meta['duration'],
            now, int(stat.st_mtime), now, meta['year'], meta['disc'], meta['bitrate'], meta['sample_rate'],
            meta['channels'], int(stat.st_size), meta['bpm'], meta['musical_key'], meta['mood'], meta['energy']
        ))
        updated += 1

    with self._connect() as db:
        if seen_updates:
            db.executemany('UPDATE songs SET last_seen=? WHERE id=?', seen_updates)
        if delete_ids:
            db.executemany('DELETE FROM songs WHERE id=?', delete_ids)
        if rows:
            db.executemany('''
                INSERT INTO songs(path,title,artist,album,genre,track,duration,added_at,modified_at,last_seen,
                    year,disc,bitrate,sample_rate,channels,file_size,bpm,musical_key,mood,energy)
                VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
                ON CONFLICT(path) DO UPDATE SET title=excluded.title,artist=excluded.artist,album=excluded.album,
                    genre=excluded.genre,track=excluded.track,duration=excluded.duration,modified_at=excluded.modified_at,
                    last_seen=excluded.last_seen,year=excluded.year,disc=excluded.disc,bitrate=excluded.bitrate,
                    sample_rate=excluded.sample_rate,channels=excluded.channels,file_size=excluded.file_size,
                    bpm=excluded.bpm,musical_key=excluded.musical_key,mood=excluded.mood,energy=excluded.energy
            ''', rows)
        if roots and not scan_all:
            clauses = ' OR '.join('path LIKE ?' for _ in roots)
            args = [now] + [r.rstrip('\\/') + os.sep + '%' for r in roots]
            db.execute(f'DELETE FROM songs WHERE last_seen<>? AND ({clauses})', args)

    return {'found': found, 'updated': updated, 'skippedShort': skipped_short, 'roots': len(roots)}


NeoStore.scan = _fast_scan
