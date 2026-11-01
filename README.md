# RR Music Pack Maker

A Windows app for making custom music packs for [Retro Rewind](https://mkwiiki.org/wiki/Retro_Rewind), the Mario Kart Wii distribution. Drop in your songs, and it builds correctly named, looping BRSTM files.

## Features

- Lists every music slot in your Retro Rewind install: tracks, battle arenas, menus and fanfares
- Converts almost any audio format to BRSTM, including MP3, FLAC, OGG, WAV and M4A
- Loop editor with a waveform, draggable markers and instant replay of the jump
- Loop finder, plus a crossfade for songs that have no clean loop
- Assign one song to many tracks, or spread a folder of songs across them
- Find songs online or paste a link, using yt-dlp
- Even volume across songs, and per-song volume
- Dark and light themes, autosave, and rebuilds that only re-encode what changed
- Exports a zip and a credits file, and can add the pack to Wheel Wizard as a mod

## Requirements

- Windows 10 or 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [FFmpeg](https://ffmpeg.org/) (`ffmpeg.exe` and `ffprobe.exe`), on your PATH or next to the app
- Optional: yt-dlp, which the app installs the first time you use online search

## Build

Needs the .NET 9 SDK.

```
dotnet publish RRMusicPackMaker.csproj -c Release -o publish
```

The app is then at `publish\RRMusicPackMaker.exe`.

Run the tests with:

```
dotnet test Tests
```

## How to use

1. Open the app. It finds your Retro Rewind install, or you can pick it in **Settings**.
2. Drag a song onto a track, or select a track and click **Choose song**.
3. Click **Edit loop** to set the loop, and listen to the jump.
4. Click **Build pack**.
5. Click **Add to Wheel Wizard**.

For Riivolution or a real Wii without Wheel Wizard, copy the pack's `RetroRewind6` folder over, then set the **My Stuff** option to **RR Music**.

**No sound in game?** Check that the Music setting in Retro Rewind isn't set to disable music.

## Copyright

Only download and share music you have the right to use. Downloading from some sites may break their terms of service. Copy-protected services such as Spotify aren't supported.

This is a fan project, and it isn't affiliated with Nintendo or the Retro Rewind team.

## Built with

- [VGAudio](https://github.com/Thealexbarney/VGAudio) for BRSTM encoding
- [FFmpeg](https://ffmpeg.org/) for decoding audio
- [yt-dlp](https://github.com/yt-dlp/yt-dlp) for online songs
- [Pulsar](https://github.com/Retro-Rewind-Team/Pulsar) source, used as the reference for how Retro Rewind loads music
