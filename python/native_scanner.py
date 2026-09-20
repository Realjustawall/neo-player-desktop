from __future__ import annotations

import os
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any


class NativeLibraryScanner:
    def __init__(self, store: Any):
        self.store = store
        self._run_lock = threading.Lock()
        self._state_lock = threading.Lock()
        self._state = {
            'running': False,
            'phase': 'idle',
            'found': 0,
            'processed': 0,
            'updated': 0,
            'unchanged': 0,
            'skippedShort': 0,
            'errors': 0,
            'roots': 0,
            'elapsedMs': 0,
        }

    def status(self) -> dict[str, Any]:
        with self._state_lock:
            return dict(self._state)

    def _set(self, **values: Any) -> None:
        with self._state_lock:
            self._state.update(values)

    def _norm(self, value: str | os.PathLike[str]) -> str:
        return os.path.normcase(os.path.abspath(os.fspath(value)))

    def _inside(self, path: str, roots: list[str]) -> bool:
        target = self._norm(path)
        for root in roots:
            base = self._norm(root)
            try:
                if os.path.commonpath([target, base]) == base:
                    return True
            except ValueError:
                pass
        return False

    def _paths(self, roots: list[str], scan_all: bool, enabled: set[str], excluded: list[str]):
        skipped = {
            '$recycle.bin', 'system volume information', 'windows', 'program files', 'program files (x86)',
            'programdata', 'recovery', 'node_modules', '.git', '.cache', 'appdata', '$windows.~bt', '$windows.~ws'
        }
        excluded_norm = [self._norm(x) for x in excluded if x]
        stack = [Path(root) for root in roots if root and Path(root).exists()]
        while stack:
            base = stack.pop()
            base_norm = self._norm(base)
            if any(base_norm == item or base_norm.startswith(item + os.sep) for item in excluded_norm):
                continue
            try:
                with os.scandir(base) as entries:
                    for entry in entries:
                        try:
                            if entry.is_dir(follow_symlinks=False):
                                name = entry.name.lower()
                                if scan_all and (name in skipped or name.startswith('.')):
                                    continue
                                target_norm = self._norm(entry.path)
                                if any(target_norm == item or target_norm.startswith(item + os.sep) for item in excluded_norm):
                                    continue
                                stack.append(Path(entry.path))
                            elif entry.is_file(follow_symlinks=False) and Path(entry.name).suffix.lower() in enabled:
                                yield Path(entry.path)
                        except OSError:
                            continue
            except OSError:
                continue

    def _probe(self, item: tuple[Path, os.stat_result], minimum: float):
        path, stat = item
        try:
            meta = self.store._metadata(path)
            duration = float(meta.get('duration') or 0.0)
            if duration > 0 and duration < minimum:
                return ('short', str(path), None, None)
            row = (
                str(path),
                str(meta.get('title') or path.stem),
                str(meta.get('artist') or ''),
                str(meta.get('album') or ''),
                str(meta.get('genre') or ''),
                int(meta.get('track') or 0),
                duration,
                int(stat.st_mtime),
                int(meta.get('year') or 0),
                int(meta.get('disc') or 0),
                int(meta.get('bitrate') or 0),
                int(meta.get('sample_rate') or 0),
                int(meta.get('channels') or 0),
                int(stat.st_size),
                float(meta.get('bpm') or 0),
                str(meta.get('musical_key') or ''),
                str(meta.get('mood') or ''),
                float(meta.get('energy') or 0),
            )
            return ('ok', str(path), row, None)
        except Exception as ex:
            return ('error', str(path), None, str(ex))

    def scan(self, scan_all: bool = False) -> dict[str, Any]:
        if not self._run_lock.acquire(blocking=False):
            state = self.status()
            return state | {'alreadyRunning': True}
        started = time.perf_counter()
        try:
            settings = self.store.settings()
            roots = self.store.system_roots() if scan_all else self.store.folders()
            roots = [str(Path(x).expanduser()) for x in roots if x and Path(x).exists()]
            configured = settings.get('audioExtensions')
            enabled = set(configured) if isinstance(configured, list) and configured else {
                '.mp3', '.flac', '.wav', '.m4a', '.aac', '.ogg', '.opus', '.wma', '.aiff', '.aif', '.ape', '.webm'
            }
            enabled = {str(x).lower() if str(x).startswith('.') else '.' + str(x).lower() for x in enabled}
            minimum = max(0.0, float(settings.get('minDurationMs', 10000) or 0) / 1000.0)
            excluded = settings.get('excludedFolders') if isinstance(settings.get('excludedFolders'), list) else []
            now = int(time.time())
            self._set(running=True, phase='indexing', found=0, processed=0, updated=0, unchanged=0, skippedShort=0, errors=0, roots=len(roots), elapsedMs=0)
            if not roots:
                result = self.status() | {'running': False, 'phase': 'done'}
                self._set(**result)
                return result
            with self.store._connect() as db:
                existing_rows = db.execute("SELECT id,path,modified_at,duration FROM songs WHERE source_type='file'").fetchall()
            existing = {self._norm(row['path']): (int(row['id']), int(row['modified_at'] or 0), float(row['duration'] or 0)) for row in existing_rows}
            unchanged_ids: list[tuple[int, int]] = []
            delete_ids: list[tuple[int]] = []
            candidates: list[tuple[Path, os.stat_result]] = []
            found = unchanged = skipped_short = errors = 0
            for path in self._paths(roots, scan_all, enabled, excluded):
                found += 1
                if found % 128 == 0:
                    self._set(found=found)
                try:
                    stat = path.stat()
                except OSError:
                    errors += 1
                    continue
                previous = existing.get(self._norm(path))
                if previous and previous[1] == int(stat.st_mtime):
                    if previous[2] > 0 and previous[2] < minimum:
                        delete_ids.append((previous[0],))
                        skipped_short += 1
                    else:
                        unchanged_ids.append((now, previous[0]))
                        unchanged += 1
                    continue
                candidates.append((path, stat))
            self._set(phase='metadata', found=found, unchanged=unchanged, skippedShort=skipped_short, errors=errors)
            workers = max(2, min(12, (os.cpu_count() or 4) * 2))
            processed = updated = 0
            rows: list[tuple[Any, ...]] = []
            short_paths: list[str] = []
            with ThreadPoolExecutor(max_workers=workers, thread_name_prefix='neo-scan') as pool:
                for kind, path, row, error in pool.map(lambda x: self._probe(x, minimum), candidates, chunksize=16):
                    processed += 1
                    if kind == 'ok' and row is not None:
                        rows.append(row)
                        updated += 1
                    elif kind == 'short':
                        short_paths.append(path)
                        skipped_short += 1
                    else:
                        errors += 1
                    if processed % 32 == 0:
                        self._set(processed=processed, updated=updated, skippedShort=skipped_short, errors=errors)
            self._set(phase='database', processed=processed, updated=updated, skippedShort=skipped_short, errors=errors)
            with self.store._connect() as db:
                if unchanged_ids:
                    db.executemany('UPDATE songs SET last_seen=? WHERE id=?', unchanged_ids)
                if delete_ids:
                    db.executemany('DELETE FROM songs WHERE id=?', delete_ids)
                if short_paths:
                    db.executemany('DELETE FROM songs WHERE path=?', [(x,) for x in short_paths])
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
                    ''', [
                        (path,title,artist,album,genre,track,duration,now,modified,now,year,disc,bitrate,sample_rate,channels,file_size,bpm,key,mood,energy)
                        for path,title,artist,album,genre,track,duration,modified,year,disc,bitrate,sample_rate,channels,file_size,bpm,key,mood,energy in rows
                    ])
                if not scan_all:
                    stale = db.execute("SELECT id,path FROM songs WHERE source_type='file' AND last_seen<>?", (now,)).fetchall()
                    stale_ids = [(int(row['id']),) for row in stale if self._inside(row['path'], roots) and not Path(row['path']).exists()]
                    if stale_ids:
                        db.executemany('DELETE FROM songs WHERE id=?', stale_ids)
            elapsed = int((time.perf_counter() - started) * 1000)
            result = {
                'running': False,
                'phase': 'done',
                'found': found,
                'processed': processed,
                'updated': updated,
                'unchanged': unchanged,
                'skippedShort': skipped_short,
                'errors': errors,
                'roots': len(roots),
                'elapsedMs': elapsed,
                'workers': workers,
                'native': True,
            }
            self._set(**result)
            return result
        finally:
            if self.status().get('running'):
                self._set(running=False, phase='done', elapsedMs=int((time.perf_counter() - started) * 1000))
            self._run_lock.release()

    def auto_scan(self) -> dict[str, Any]:
        home = Path.home()
        names = ['Music', 'Downloads', 'Desktop', 'موسیقی']
        candidates = []
        for name in names:
            path = home / name
            if path.is_dir():
                candidates.append(str(path.resolve()))
        if os.name == 'nt':
            profile = os.environ.get('USERPROFILE')
            if profile:
                for name in names:
                    path = Path(profile) / name
                    if path.is_dir():
                        candidates.append(str(path.resolve()))
        added = []
        for path in dict.fromkeys(candidates):
            try:
                self.store.add_folder(path)
                added.append(path)
            except Exception:
                pass
        result = self.scan(False)
        result['autoFolders'] = added
        return result
