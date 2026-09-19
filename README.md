# NEO Player Desktop — Alpha 0.1

Windows port of NEO Player, designed as an offline-first local music player rather than a web wrapper.

## Alpha 0.1 scope

This branch starts from the most complete Windows implementation currently available and mirrors the Android feature set wherever Windows has an equivalent. The application is WPF/.NET 8 and uses a native local-audio pipeline.

### Playback

- Local playback through FFmpeg PCM decoding and NAudio output
- Dual-deck overlapping crossfade with equal-power curves
- Gapless handoff and preloading
- AutoMix planning using BPM, beat/phrase position, Camelot/key compatibility and energy
- Playback speed, shuffle, repeat, persistent queue and resume
- Per-track 10-band EQ, bass enhancement, stereo widening and loudness controls
- Smart / ReplayGain / analysis-based normalization
- Sleep timer with fade
- Output-device recovery

### Library and collections

- Incremental local-folder scanning with include/exclude source folders
- Stable Windows file IDs where available
- Songs, albums, artists, genres, folders, playlists and categories
- Nested playlist folders with cycle prevention
- Per-playlist sort and list/grid preferences
- Pinning, favorites, hidden songs and reversible metadata overrides
- Search history, debounce and autocomplete
- M3U8 import/export and local backup/restore

### Lyrics and offline intelligence

- Plain and synchronized LRC lyrics
- Translation and romanization layers
- Adjacent sidecar discovery
- Timestamp authoring and forced alignment
- Offline Vosk transcription with English and Persian models
- BPM / beat-grid / phrase / musical-key / Camelot / energy / valence / danceability / mood analysis
- ReplayGain cache and local recommendations
- Smart Local Radio and time-of-day / history-based mixes

### Track+

- Per-track artwork override
- Per-track wallpaper/background
- Per-track theme/accent/background/secondary colors
- Local Canvas video with crop/fit/stretch, speed and loop range
- Waveform, spectrum and pulse visualizer modes

### Windows integration

- System Media Transport Controls metadata/timeline
- Media keyboard/headset buttons
- System tray controls and minimize-to-tray
- Compact Player window
- Start-with-Windows option
- English/Persian UI and RTL layout
- Dark, Light, System and AMOLED themes with original NEO accent palette and dynamic artwork accent

## Build

GitHub Actions builds `win-x64` on Windows with .NET 8. The Alpha artifact is self-contained and the build workflow bundles FFmpeg plus the small English and Persian Vosk models so the portable artifact has the required local engines available.

Project file: `src/NeoPlayer.Windows/NeoPlayer.Windows.csproj`

> Alpha software: compile/build verification does not replace real-device QA with large libraries, unusual codecs, Bluetooth devices and different Windows hardware/audio drivers.
