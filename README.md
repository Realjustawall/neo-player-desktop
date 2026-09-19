# NEO Player Desktop

A clean-room Windows desktop implementation of NEO Player focused on reliable startup, native Windows audio and a small distribution.

## Architecture

- .NET 8 + WPF
- NAudio with Windows Media Foundation decoding and WASAPI output
- SQLite persistence
- TagLibSharp metadata reading
- No bundled FFmpeg
- No bundled speech models

## Implemented in the new core

Local library scanning with source/exclusion folders, metadata, search, favorites, hidden-track persistence, history, playlist folders with cycle prevention, playlists, synced LRC parsing, backup/restore, cached local audio analysis, Smart Local Radio, dual-deck crossfade, ten-band EQ, bass enhancement, stereo width, themes, English/Persian resources and RTL switching.

## Verification

CI builds and tests the solution, publishes a self-contained compressed win-x64 executable, runs the published executable with `--self-test`, runs a WPF construction smoke test with `--ui-smoke`, verifies startup exit codes, and rejects oversized packages.

This repository intentionally does not publish a GitHub Release until the runtime smoke gates pass.
