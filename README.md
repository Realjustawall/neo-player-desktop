# NEO Player Desktop

Private, offline-first music player for Windows. The desktop app uses React 18 + Vite for the interface and Python + SQLite + pywebview for local library access and packaging.

## Highlights

- Folder and whole-system audio scanning with duration filters
- Local playback, persistent queue, shuffle, repeat, speed, gapless mode and dual-deck crossfade
- Albums, artists, genres, favorites, history, playlists and nested playlist folders
- Playlist search, sorting, folder assignment, pinning, M3U/M3U8 import/export and custom ordering
- Smart local mixes based on favorites, history, skips, artist, genre, BPM and key
- Synced LRC/plain lyrics, translation, romanization and optional sidecar writing
- Per-track artwork, wallpaper, Canvas video, visualizer and audio profile
- Five ranked visual themes, light/dark/system/AMOLED modes, custom accents, density and Persian RTL support
- Local EQ, bass boost, normalization controls, BPM/key/energy analysis and sleep timer
- Full local backup/restore, cache controls, Windows Media Session integration and no account requirement

## Run locally

```powershell
npm install
npm run build
python -m pip install -r requirements.txt
python python/desktop.py
```

## Test

```powershell
npm run build
python -m unittest discover -s tests -v
python python/desktop.py --self-test
python python/desktop.py --ui-smoke
```

## Build the Windows executable

```powershell
python -m PyInstaller --noconfirm --clean --onefile --windowed --name "NEO Player" --icon "public/neo-player.ico" --add-data "dist;dist" --hidden-import webview.platforms.edgechromium python/desktop.py
```

The executable is written to `dist/NEO Player.exe`. GitHub Actions runs the same build and uploads a tested Windows x64 artifact.

NEO Player is local-first: music, playlists, preferences, analysis and lyrics stay on the device.
