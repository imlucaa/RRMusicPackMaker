# Music Pack Maker

A Windows app for making custom music packs for [Retro Rewind](https://mkwiiki.org/wiki/Retro_Rewind), the Mario Kart Wii distribution. Drop in your songs and it builds correctly named, looping music files for you.

## Download

Get it from [Releases](https://github.com/imlucaa/RRMusicPackMaker/releases). There's no installer. Run it from any folder you can write to.

- `packmaker.exe` is 2 MB and needs the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
- `packmaker-NoDotNet.exe` is 50 MB and needs nothing else.

Windows 10 or 11. You don't need Retro Rewind, Wheel Wizard or a game file to make a pack.

## How to use

1. Open the app. On first launch, a short setup window can install FFmpeg, which converts your songs.
2. Drag a song onto a track, or pick a track and click **Choose song**. Select many tracks to give them songs at once.
3. Click **Edit loop** if you want to change where the song loops.
4. Click **Export pack**. Save it as a zip, put the songs in a folder, or add them straight to your game.

On a real Wii, copy the songs into `SD:/RetroRewind6/strm`, and say yes if it asks to replace files.

## What it can do

- Converts MP3, FLAC, OGG, WAV, M4A and more to looping BRSTM files
- Loop editor, loop finder and crossfade
- Speed, volume and final lap settings for each song
- A song editor to crop, split, join and fade songs
- Search and download songs from YouTube
- Import a pack someone else made, and switch between several packs
- Export for a Wii or for Dolphin
- Optional original file names for the 32 original tracks, for CTGP and the original game

## If something's wrong

- **No sound in game:** check that Retro Rewind's Music setting isn't set to disable music.
- **Music too quiet:** raise the song's Volume.
- **Old music still plays:** check that Loose Archive Overrides is on. It's on by default.

## Build

Needs the .NET 9 SDK.

```
dotnet publish RRMusicPackMaker.csproj -c Release -o publish
dotnet test Tests
```

## Copyright

Only download and share music you have the right to use. Copy-protected services such as Spotify aren't supported.

This is a fan project. It isn't affiliated with Nintendo or the Retro Rewind team.

## Built with

[VGAudio](https://github.com/Thealexbarney/VGAudio), [FFmpeg](https://ffmpeg.org/), [yt-dlp](https://github.com/yt-dlp/yt-dlp), and the [Pulsar](https://github.com/Retro-Rewind-Team/Pulsar) source as the reference for how Retro Rewind loads music.
