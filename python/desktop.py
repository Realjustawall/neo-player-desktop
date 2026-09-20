from __future__ import annotations

import argparse
import os
import sys
import tempfile
import time
import urllib.request
from pathlib import Path

import webview
import sqlite_close  # noqa: F401 - patches sqlite3 connection context behavior
from backend import NeoStore, create_server

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


class DesktopApi:
    def pick_folder(self) -> str | None:
        try:
            result = webview.windows[0].create_file_dialog(webview.FileDialog.FOLDER)
            if not result:
                return None
            return result[0] if isinstance(result, (list, tuple)) else str(result)
        except Exception:
            return None

    def pick_file(self, extensions: tuple[str, ...] | None = None) -> str | None:
        try:
            result = webview.windows[0].create_file_dialog(webview.FileDialog.OPEN)
            if not result:
                return None
            value = result[0] if isinstance(result, (list, tuple)) else str(result)
            if extensions and Path(value).suffix.lower() not in {x.lower() for x in extensions}:
                return None
            return value
        except Exception:
            return None

    def save_file(self, filename: str = 'NEO-Backup.json') -> str | None:
        try:
            result = webview.windows[0].create_file_dialog(webview.FileDialog.SAVE, save_filename=filename)
            if not result:
                return None
            return result[0] if isinstance(result, (list, tuple)) else str(result)
        except Exception:
            return None

    def minimize(self) -> None:
        if webview.windows:
            webview.windows[0].minimize()

    def toggle_fullscreen(self) -> None:
        if webview.windows:
            webview.windows[0].toggle_fullscreen()


def run_self_test() -> int:
    with tempfile.TemporaryDirectory(prefix='neo-player-test-') as tmp:
        store = NeoStore(tmp)
        defaults = store.settings()
        assert defaults['accent'] == 'orange'
        assert defaults['themeMode'] == 'dark'
        assert defaults['minDurationMs'] == 10_000
        store.patch_settings({'accent': 'blue', 'volume': 0.5, 'language': 'fa'})
        updated = store.settings()
        assert updated['accent'] == 'blue'
        assert updated['language'] == 'fa'
        playlist = store.create_playlist('Offline Mix')
        assert playlist['name'] == 'Offline Mix'
        assert store.playlist(playlist['id'])['songs'] == []
        folder = store.create_playlist_folder('Folder')
        assert folder['name'] == 'Folder'
        assert isinstance(store.system_roots(), list)
    print('NEO_SELF_TEST_OK')
    return 0


def run_ui_smoke() -> int:
    with tempfile.TemporaryDirectory(prefix='neo-player-ui-') as tmp:
        store = NeoStore(tmp)
        server = create_server(store, resource_path('dist'))
        rendered = {'ok': False}
        try:
            with urllib.request.urlopen(server.url + '/api/health', timeout=5) as response:
                payload = response.read().decode('utf-8')
                assert 'true' in payload.lower()
            with urllib.request.urlopen(server.url + '/', timeout=5) as response:
                html = response.read().decode('utf-8')
                assert '<div id="root"></div>' in html
            window = webview.create_window(APP_NAME, server.url, width=1180, height=760, min_size=(900, 600), js_api=DesktopApi())
            def verify_after_start():
                deadline = time.time() + 6
                while time.time() < deadline:
                    try:
                        value = window.evaluate_js("Boolean(document.querySelector('.app-shell')) && document.body.innerText.includes('NEO PLAYER')")
                        if value:
                            rendered['ok'] = True
                            break
                    except Exception:
                        pass
                    time.sleep(.25)
                window.destroy()
            webview.start(verify_after_start, gui='edgechromium')
            assert rendered['ok'], 'React DOM did not render in packaged WebView2'
        finally:
            server.stop()
    print('NEO_UI_SMOKE_OK')
    return 0


def run_app() -> int:
    store = NeoStore(data_dir())
    server = create_server(store, resource_path('dist'))
    try:
        webview.create_window(APP_NAME, server.url, width=1360, height=860, min_size=(960, 640), js_api=DesktopApi(), background_color='#0b0b0b')
        webview.start(gui='edgechromium')
        return 0
    finally:
        server.stop()


def main() -> int:
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--self-test', action='store_true')
    parser.add_argument('--ui-smoke', action='store_true')
    args, _ = parser.parse_known_args()
    if args.self_test:
        return run_self_test()
    if args.ui_smoke:
        return run_ui_smoke()
    return run_app()


if __name__ == '__main__':
    raise SystemExit(main())
