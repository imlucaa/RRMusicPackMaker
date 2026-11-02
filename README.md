# Music Pack Maker

A Windows app for making custom music packs for [Retro Rewind](https://mkwiiki.org/wiki/Retro_Rewind), the Mario Kart Wii distribution. Drop in your songs, and it builds correctly named, looping BRSTM files.

## Features

- Lists every music slot in your Retro Rewind install, sorted into Regs, Retros, CTs, battle arenas, menus and fanfares, with a filter by console
- Converts almost any audio format to BRSTM, including MP3, FLAC, OGG, WAV and M4A
- Loop editor with a waveform, draggable markers and instant replay of the jump
- Loop finder, plus a crossfade for songs that have no clean loop
- Change any song's speed, on one track or the whole pack: slowed and reverb, faster at the same pitch, or nightcore with a higher pitch
- Editor tab: crop a song by dragging its edges, split it, drag parts to reorder them, drag in other songs to merge them with an optional blend, and save the result as a new song. Delete downloads you no longer want, which sends them to the Recycle Bin
- Songs tab: every song you import, download or edit, with what it's made from and which tracks use it. Play and pause it, take it off tracks, keep editing it, or delete it
- A dropdown on each track to pick from your songs
- Merge songs and make each part play for the same length of time
- Import a pack from what's on your PC, any folder or zip, or single song files, and its songs go back on their tracks. Imported songs stay exactly as they were unless you change their settings. Delete any pack from the same list, which keeps non-music files like karts, and listen to any imported song
- Assign one song to many tracks, or spread a folder of songs across them
- Search YouTube Music and YouTube for anything, with official songs first, and hear a preview before downloading. You can also paste a link
- Shows setup problems in the log, like music turned off in the game, and marks song files that went missing
- Sets loop, crossfade, volume and speed on many tracks at once, plays a track's current music, and removes a pack with one click
- Hear a track's song with its speed, reverb and volume before making the pack, then pause, resume or stop it
- Keyboard shortcuts: Ctrl+F to search, Delete to remove a song, Space to play and pause, Esc to stop
- Dark and light themes, autosave, and rebuilds that only re-encode what changed
- Adds the pack to the Patches folder and to Wheel Wizard and Wheel Wizard+, or exports it as a folder or zip anywhere you choose
- Even volume across songs, louder on race tracks because the game's race mix turns music down, with clean per-song boost or cut
- The exported folder has install steps and credits inside, for real Wii, Dolphin or sharing

## Requirements

- Windows 10 or 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0), unless you use the NoDotNet download

A short setup window on first launch shows the folders it found and the recommended settings, already chosen, so you can just click **Start**. Everything in it is optional. The app gets these for you, with their checksums verified:

- [FFmpeg](https://ffmpeg.org/), the small shared build, installed from the setup window or from **Settings**. It's a 77 MB download and takes about 150 MB of space. If you already have it on your PATH, the app uses that.
- [yt-dlp](https://github.com/yt-dlp/yt-dlp), the first time you download a song

## Download

Get it from [Releases](https://github.com/imlucaa/RRMusicPackMaker/releases). There's no installer, so run it from any folder you can write to.

- `MusicPackMaker.exe` is 1.7 MB, and needs the .NET 9 Desktop Runtime.
- `MusicPackMaker-NoDotNet.exe` is 49 MB, and needs nothing else installed.

## Build

Needs the .NET 9 SDK.

```
dotnet publish RRMusicPackMaker.csproj -c Release -o publish
```

The app is then at `publish\MusicPackMaker.exe`.

Run the tests with:

```
dotnet test Tests
```

## How to use

1. Open the app. The first time, a short setup window lets you install FFmpeg, check your folders and change any settings.
2. Drag a song onto a track, or select a track and click **Choose song**.
3. Click **Edit loop** to set the loop, and listen to the jump.
4. Click **Add to Patches** to play it yourself. This copies the songs to `RetroRewind6/Patches`, and adds them as a mod if you have Wheel Wizard or Wheel Wizard+, so they show in Mods and stay in `Patches`.
5. Restart Wheel Wizard if it's open.

**Real Wii, Dolphin or sharing:** click **Export pack**, then **Choose folder** or **Save as zip**, and pick where the pack goes. It comes with a README that explains how to install it. On a real Wii, its `RetroRewind6` folder goes on the root of the SD card.

**Export pack** can also add the pack to Wheel Wizard's Mods list, without copying anything to `Patches` until you launch the game.

**Real Wii with no ISO on your PC:** click **Export pack**, then **Choose folder**, and copy the `RetroRewind6` folder it makes to the root of your SD card. Songs end up in `SD:/RetroRewind6/Patches`, which Retro Rewind loads without My Stuff. The ISO is only used for the optional volume raise.

**No sound in game?** Check that the Music setting in Retro Rewind isn't set to disable music.

**Music too quiet?** Raise the song's **Volume**.

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
