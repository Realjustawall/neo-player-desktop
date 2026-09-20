import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'python'))

from backend import NeoStore
from sqlite_close import _repair_schema


class LegacySchemaRepairTests(unittest.TestCase):
    def test_repairs_missing_hidden_columns_before_library_scan(self):
        with tempfile.TemporaryDirectory() as tmp:
            db_path = Path(tmp) / 'neo.sqlite3'
            db = sqlite3.connect(db_path)
            db.executescript('''
                CREATE TABLE songs(id INTEGER PRIMARY KEY AUTOINCREMENT,path TEXT NOT NULL UNIQUE,title TEXT NOT NULL,artist TEXT NOT NULL DEFAULT '',album TEXT NOT NULL DEFAULT '',genre TEXT NOT NULL DEFAULT '',track INTEGER NOT NULL DEFAULT 0,duration REAL NOT NULL DEFAULT 0,favorite INTEGER NOT NULL DEFAULT 0,added_at INTEGER NOT NULL,modified_at INTEGER NOT NULL DEFAULT 0,last_seen INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE playlists(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,created_at INTEGER NOT NULL);
                CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
            ''')
            db.close()
            store = NeoStore(tmp)
            _repair_schema(store)
            with store._connect() as check:
                song_columns = {row['name'] for row in check.execute('PRAGMA table_info(songs)')}
                playlist_columns = {row['name'] for row in check.execute('PRAGMA table_info(playlists)')}
            self.assertIn('hidden', song_columns)
            self.assertIn('source_type', song_columns)
            self.assertIn('hidden', playlist_columns)
            self.assertIn('sort_mode', playlist_columns)
            self.assertEqual(store.library(), [])
            self.assertEqual(store.playlists(), [])
            result = store.scan(False)
            self.assertEqual(result['phase'], 'done')


if __name__ == '__main__':
    unittest.main()
