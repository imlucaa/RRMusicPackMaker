# RR Music Pack Maker

A Windows app for making custom music packs for [Retro Rewind](https://mkwiiki.org/wiki/Retro_Rewind), the Mario Kart Wii distribution. Drop in your songs, and it builds correctly named, looping BRSTM files.

## Features

- Lists every music slot in your Retro Rewind install, sorted into Regs, Retros, CTs, battle arenas, menus and fanfares, with a filter by console
- Converts almost any audio format to BRSTM, including MP3, FLAC, OGG, WAV and M4A
- Loop editor with a waveform, draggable markers and instant replay of the jump
- Loop finder, plus a crossfade for songs that have no clean loop
- Assign one song to many tracks, or spread a folder of songs across them
- Find official songs on YouTube Music, or paste a link
- Reports setup problems in Activity, like music turned off in the game or low Dolphin volume, and marks song files that went missing
- Sets loop, crossfade and volume on many tracks at once, plays a track's current music, and removes a pack with one click
- Keyboard shortcuts: Ctrl+F to search, Delete to remove a song, Space to play, Esc to stop
- Dark and light themes, autosave, and rebuilds that only re-encode what changed
- Adds the pack to the Patches folder and to Wheel Wizard and Wheel Wizard+, or exports it as a zip
- Even volume across songs, louder on race tracks because the game's race mix turns music down, with clean per-song boost or cut
- The exported zip has install steps and credits inside, for real Wii, Dolphin or sharing

## Requirements

- Windows 10 or 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0), unless you use the NoDotNet download

A setup window on first launch helps you install FFmpeg and find your game. The app gets these for you, with their checksums verified:

- [FFmpeg](https://ffmpeg.org/), the small shared build, installed from the setup window or from **Settings**. It's a 77 MB download and takes about 150 MB of space. If you already have it on your PATH, the app uses that.
- [yt-dlp](https://github.com/yt-dlp/yt-dlp), the first time you download a song

## Download

Get it from [Releases](https://github.com/imlucaa/RRMusicPackMaker/releases). There's no installer, so run it from any folder you can write to.

- `RRMusicPackMaker.exe` is 1.5 MB, and needs the .NET 9 Desktop Runtime.
- `RRMusicPackMaker-NoDotNet.exe` is 49 MB, and needs nothing else installed.

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

1. Open the app. If something's missing, the setup window lets you install FFmpeg and pick your Retro Rewind folder.
2. Drag a song onto a track, or select a track and click **Choose song**.
3. Click **Edit loop** to set the loop, and listen to the jump.
4. Click **Add to Patches** to play it yourself. This copies the songs to `RetroRewind6/Patches`, and adds them as a mod if you have Wheel Wizard or Wheel Wizard+, so they show in Mods and stay in `Patches`.
5. Restart Wheel Wizard if it's open.

**Real Wii, Dolphin or sharing:** click **Export pack** to save a zip in `Documents\RR Music Packs`, then **Show exported pack** to find it. Its README explains how to install it. On a real Wii, its `RetroRewind6` folder goes on the root of the SD card.

**No sound in game?** Check that the Music setting in Retro Rewind isn't set to disable music.

**Music too quiet?** Check Dolphin's volume under Config > Audio, then raise the song's **Volume**.

**Why songs get louder after installing:** the game caps each music slot with a volume stored in `revo_kart.brsar`, some as low as 51 out of 127. The app makes a copy from your own game file with the race and battle music raised to full scale, and adds it to `Patches`. It needs DolphinTool, which comes with Dolphin, and your game file. It's Nintendo's data, so it's never put in an exported pack. Turn it off in **Settings**.

**Old music still plays?** Check that the Loose Archive Overrides setting is on. It's on by default.

## Copyright

Only download and share music you have the right to use. Downloading from some sites may break their terms of service. Copy-protected services such as Spotify aren't supported.

This is a fan project, and it isn't affiliated with Nintendo or the Retro Rewind team.

## Built with

- [VGAudio](https://github.com/Thealexbarney/VGAudio) for BRSTM encoding
- [FFmpeg](https://ffmpeg.org/) for decoding audio
- [yt-dlp](https://github.com/yt-dlp/yt-dlp) for online songs
- [Pulsar](https://github.com/Retro-Rewind-Team/Pulsar) source, used as the reference for how Retro Rewind loads music
