# Pack Maker (GPUI)

A native front end for Music Pack Maker, built with [GPUI](https://www.gpui.rs/) and [GPUI Kit](https://gpui-kit.com/). It's laid out like a classic music player: menus and transport on top, the library on the left, the tracks as the playlist, the selection's properties on the right, and a status bar.

The encoding, the Retro Rewind track list and the exports come from the tested C# core, which this app runs without a window as `MusicPackMaker.exe engine ...`. Both apps share the same saved pack, packs, settings and song library, so either one can pick up the other's work.

## What it does

- **Tracks:** every Retro Rewind track, grouped and counted, with a filter. Drop songs or folders on a track, or on a selection to fill it. Drop them on the list to match them to tracks by file name. Right-click a track for the common actions.
- **Per track:** song name and credits, volume (with Louder and Quieter), speed, loop mode, crossfade, final lap speed, and a different song for the final lap with its own volume and speed. Hear the track, or its final lap, as it will sound in the pack.
- **Many tracks at once:** select all, only tracks with or without a song, then fill from songs you pick (shuffled or in order, only the empty ones if you like), match names, or change any setting for all of them.
- **Loop editor:** the waveform with draggable loop markers, nudge buttons, the loop finder, and hearing the jump.
- **Songs:** everything you imported, downloaded or made, with lengths and how many tracks use each. Play, use on tracks, take off tracks, open in the editor, delete.
- **Editor:** parts of songs in a row, trimmed, split, copied, reordered, blended, sped up and faded, saved as a new song.
- **Find online:** search YouTube Music and YouTube, or paste a link, at the quality you choose.
- **Packs:** several at once from the Pack menu, new, delete, and import a folder, a zip or the game's strm folder, as its own pack or into the open one. A list of packs on this PC, with delete.
- **Export:** a zip or songs straight into a folder, or add to the game and take it out again, for a real Wii or for Dolphin, with the optional original file names for CTGP.
- **Look:** eight themes (Amber, Graphite, Midnight, Oxide, Moss, Violet, Classic light, Paper), your own accent and background colors, a background picture and an app size from 75% to 200%.
- **Setup:** first-run Settings, FFmpeg install, Retro Rewind setup warnings and new-version notice.
- **Keyboard:** Space play/pause, Enter play, Delete remove song, Ctrl+A, arrows and Shift+arrows, Ctrl+F filter, Ctrl+O add songs, Ctrl+I import, Ctrl+D find online, Ctrl+L loop, Ctrl+= / Ctrl+- volume, Ctrl+S save, Ctrl+E export zip, Ctrl+N new pack, Ctrl+, settings, Ctrl+1/2/3 tabs, Ctrl+Left/Right previous/next, Ctrl+\` console.

Don't change the same pack in both windows at once. After changing it in the classic window, use File > Reload from disk here.

## Build

Needs Rust 1.92 or newer with the MSVC toolchain, Visual Studio 2022 Build Tools with the C++ workload, and CMake.

```
cargo run --release
cargo test
```

The engine is found next to `packmaker.exe` (or in an `engine` folder beside it), at `RRMUSIC_ENGINE`, or in this repository's own `bin` or `publish` folder when run from the sources. For taking pictures without clicking, `RRMUSIC_OPEN` (`settings`, `loop`, `download`, `packs`, `about`, `editor`, `songs`, `console`) opens a screen at start and `RRMUSIC_SELECT` selects tracks.
