# Pack Maker (GPUI)

A native front end for Music Pack Maker, built with [GPUI](https://www.gpui.rs/) and [GPUI Kit](https://gpui-kit.com/). It's laid out like a classic music player: transport and seek bar on top, the library on the left, the tracks as the playlist, the selection's properties on the right, and a status bar.

It is a preview. The encoding, the Retro Rewind track list and the exports still come from the tested C# core, which this app runs headless as `MusicPackMaker.exe engine ...`. Both apps share the same saved pack and song library.

## What works here

- Every Retro Rewind track, grouped and counted like the classic sidebar, with a filter
- Drop song files or folders on a track, or on a selection to fill it in order
- Drag songs from the Songs tab onto tracks
- Volume, speed, loop and final lap speed for one track or many
- Play any track as it will sound in the pack, with seek, volume, next and previous
- Export as a zip, copy to a folder, add to the game, or take it out again, for a real Wii or for Dolphin
- Keyboard: Space play/pause, Enter play, Delete remove song, Ctrl+A, arrows and Shift+arrows, Ctrl+F filter, Ctrl+O add songs, Ctrl+S save, Ctrl+E export zip, Ctrl+Left/Right previous/next, Ctrl+` console

Still in the classic window (File > Open the classic window): the song editor, the loop editor, downloading songs, importing packs and Settings.

Don't change the same pack in both windows at once. After changing it in the classic window, use File > Reload from disk here.

## Build

Needs Rust 1.92 or newer with the MSVC toolchain, Visual Studio 2022 Build Tools with the C++ workload, and CMake.

```
cargo run --release
cargo test
```

The engine is found next to `packmaker.exe` (or in an `engine` folder beside it), at `RRMUSIC_ENGINE`, or in this repository's own `bin` or `publish` folder when run from the sources.
