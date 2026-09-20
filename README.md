# NEO Player Desktop

Private, offline-first music player for Windows. The desktop app uses React 18 + Vite for the interface and Python + SQLite + pywebview for local library access and packaging.

## Highlights

- Folder and whole-system audio scanning with duration filters
- Per-format scanning for MP3, WAV, FLAC, M4A, AAC, OGG, Opus, WMA, AIFF, APE and WebM
- Local playback, persistent queue, shuffle, repeat, speed, gapless mode and dual-deck crossfade
- Albums, artists, genres, favorites, history, playlists and nested playlist folders
- Playlist search, sorting, folder assignment, pinning, M3U/M3U8 import/export and custom ordering
- Smart local mixes based on favorites, history, skips, artist, genre, BPM and key
- Synced LRC/plain lyrics, translation, romanization and optional sidecar writing
- Per-track artwork, wallpaper, Canvas video, visualizer and audio profile
- Independent track, playlist and playlist-folder EQ, bass, stereo virtualizer and loudness profiles
- HTTP/HTTPS audio and radio streaming through a Range-aware local proxy
- Bluetooth/wired-output recovery that preserves playback position when Windows changes devices
- Optional video-to-WAV extraction for MP4, MKV, MOV, AVI, WebM and M4V
- Five ranked visual themes, light/dark/system/AMOLED modes, custom accents, density and Persian RTL support
- Local EQ, bass boost, normalization controls, BPM/key/energy analysis and sleep timer
- Full local backup/restore, cache controls, Windows Media Session integration and no account requirement
- Optional Lyrics AI engine and multilingual Whisper models downloaded only after first-use consent

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

## Build the Windows release

```powershell
./scripts/build-windows.ps1
```

The release contains a lightweight installer and portable ZIP. Faster Whisper and its native runtime are published as the separate `NEO-Player-Lyrics-AI.zip` asset and are downloaded from inside NEO+ only when the user explicitly approves it. Tiny, Base, Small, and Medium models are also optional one-time downloads and remain available offline afterward.

NEO Player is local-first: music, playlists, preferences, analysis and lyrics stay on the device.
