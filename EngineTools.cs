using System.Text.Json;

namespace RRMusicPackMaker;

/// <summary>
/// The rest of the headless commands: songs, packs on disk, importing, downloading, loops and the song editor.
/// Lists of paths and editor parts come in as a small JSON file named by --spec, so long paths never hit the
/// command line's limits.
/// </summary>
internal static partial class Engine
{
    private sealed class Spec
    {
        public List<string> Files { get; set; } = [];
        public List<string> Tracks { get; set; } = [];
        public List<SpecClip> Clips { get; set; } = [];
        public string Name { get; set; } = "";
        public int Blend { get; set; }
        public int Speed { get; set; }
        public double FadeIn { get; set; }
        public double FadeOut { get; set; }
    }

    private sealed class SpecClip
    {
        public string Source { get; set; } = "";
        public long Start { get; set; }
        public long End { get; set; }
    }

    private static Spec ReadSpec(string[] args)
    {
        string path = Option(args, "--spec") ?? throw new InvalidDataException("Give --spec FILE.");
        return JsonSerializer.Deserialize<Spec>(File.ReadAllText(path), Json) ?? new Spec();
    }

    private static SongLibrary Library(AppSettings settings) => new(() => DownloadFolder(settings));

    private static int Guard(Func<int> work)
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException
            or JsonException or FileNotFoundException or DownloadFailedException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Emit(new { @event = "error", message = ex.Message });
            return 1;
        }
    }

    /// <summary>Songs with how long they are. Reading BRSTM headers is instant; other formats need ffprobe, a few at a time.</summary>
    private static int SongsWithLengths()
    {
        var settings = AppStorage.LoadSettings();
        var library = Library(settings);
        var songs = library.Songs;
        var lengths = new System.Collections.Concurrent.ConcurrentDictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, song =>
        {
            try
            {
                lengths[song] = AudioIO.QuickLength(song) ?? AudioIO.Probe(song).Duration;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // A length is only shown for interest.
            }
        });
        Emit(songs.Select(song => new
        {
            path = song,
            label = library.LabelOf(song),
            kind = library.KindOf(song).ToString(),
            seconds = lengths.GetValueOrDefault(song),
            owned = library.IsOwned(song),
            edited = library.PartsOf(song) != null,
        }));
        return 0;
    }

    /// <summary>Takes songs off the list. Downloads, edits and imported copies the app made go to the Recycle Bin; the user's own files stay.</summary>
    private static int DeleteSongs(string[] args) => Guard(() =>
    {
        var spec = ReadSpec(args);
        var settings = AppStorage.LoadSettings();
        var library = Library(settings);
        var gone = new List<string>();
        var kept = new List<string>();
        foreach (string song in spec.Files)
        {
            if (library.IsOwned(song) && File.Exists(song))
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(song, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    kept.Add($"{Path.GetFileName(song)}: {ex.Message}");
                    continue;
                }
            }
            gone.Add(song);
        }
        library.Remove(gone);
        Emit(new { @event = "done", message = $"Deleted {gone.Count} {(gone.Count == 1 ? "song" : "songs")}.", removed = gone, failed = kept });
        return kept.Count == 0 ? 0 : 1;
    });

    /// <summary>Packs on this PC: imported ones, ones this app made, and Wheel Wizard mods.</summary>
    private static int InstalledPacks()
    {
        var settings = AppStorage.LoadSettings();
        var project = LoadProject();
        string rr = RetroRewindFolder(project) ?? "";
        var packs = PackImporter.FindInstalled(rr, PackBuilder.ModsFolders(), OutputFolder(settings));
        Emit(packs.Select(pack => new { name = pack.Name, folder = pack.Folder, songs = pack.Songs, madeByApp = pack.MadeByApp, isMod = pack.IsMod }));
        return 0;
    }

    /// <summary>Sends a pack's songs to the Recycle Bin, or the whole folder when nothing else is in it.</summary>
    private static int DeleteInstalledPack(string[] args) => Guard(() =>
    {
        string folder = Option(args, "--folder") ?? throw new InvalidDataException("Give --folder.");
        var settings = AppStorage.LoadSettings();
        var project = LoadProject();
        var pack = PackImporter.FindInstalled(RetroRewindFolder(project) ?? "", PackBuilder.ModsFolders(), OutputFolder(settings))
            .FirstOrDefault(p => Path.GetFullPath(p.Folder).Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("That pack isn't on the list any more.");
        var plan = PackImporter.DeletePlan(pack);
        var removed = new List<string>();
        if (plan.WholeFolder)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(pack.Folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            removed.AddRange(plan.Songs);
            string manifest = pack.Folder + ".build.json";
            if (pack.MadeByApp && File.Exists(manifest))
                File.Delete(manifest);
        }
        else
        {
            foreach (string song in plan.Songs)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(song, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                removed.Add(song);
            }
        }
        Library(settings).Remove(removed);
        Emit(new { @event = "done", message = $"Deleted {removed.Count} {(removed.Count == 1 ? "song" : "songs")} from {Path.GetFileName(pack.Folder)}.", removed });
        return 0;
    });

    private static bool IsGameMusicFolder(string path, string? rrRoot)
    {
        if (!Directory.Exists(path))
            return false;
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (rrRoot is { Length: > 0 }
            && full.Equals(Path.GetFullPath(PackBuilder.StrmFolder(rrRoot)).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return true;
        string? parent = Path.GetDirectoryName(full);
        return Path.GetFileName(full).Equals("strm", StringComparison.OrdinalIgnoreCase) && parent != null
            && Directory.Exists(Path.Combine(parent, "Binaries"));
    }

    /// <summary>
    /// Imports a pack (a folder, a zip, or the game's strm folder) into the open pack, and saves it.
    /// --keep leaves tracks that already have a song alone. The front end starts a fresh pack first when wanted.
    /// </summary>
    private static int Import(string[] args) => Guard(() =>
    {
        string path = Option(args, "--path") ?? throw new InvalidDataException("Give --path.");
        bool replace = !args.Contains("--keep");
        var settings = AppStorage.LoadSettings();
        var project = LoadProject();
        string? rrRoot = RetroRewindFolder(project);
        string output = OutputFolder(settings);
        var rows = RRMusicPackMaker.Catalog.Build(rrRoot, out _);
        project.ApplyTo(rows);
        var library = Library(settings);

        string packName = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
        string? extracted = null;
        string folder = path;
        try
        {
            if (IsGameMusicFolder(path, rrRoot))
            {
                // Only the songs this app put in the game's folder are brought back, not the game's own hundreds.
                string backups = PackBuilder.StrmBackupFolder(output);
                var mine = Directory.Exists(backups)
                    ? PackBuilder.PlacedInStrm(backups).Select(name => Path.Combine(path, name)).Where(File.Exists).ToArray()
                    : [];
                if (mine.Length == 0)
                {
                    Emit(new { @event = "error", message = "That's Retro Rewind's own music folder, so there's nothing of yours in it to import." });
                    return 1;
                }
                folder = PackImporter.NewFolder(output, "Songs from strm");
                Directory.CreateDirectory(folder);
                foreach (string file in mine)
                    File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
                packName = "Songs from strm";
            }
            else if (File.Exists(path))
            {
                Emit(new { @event = "log", message = "Unpacking the zip…" });
                extracted = folder = PackImporter.ExtractZip(path);
            }

            var plan = PackImporter.Plan(folder, rows);
            foreach (var (file, spelled) in plan.Renamed)
                Emit(new { @event = "log", message = $"{Path.GetFileName(file)} has a typo in its name. It went on the {spelled} track." });
            foreach (string name in plan.Unmatched)
                Emit(new { @event = "log", message = $"No track is named {name}. It's in your songs instead." });
            if (plan.Tracks == 0 && plan.Loose.Count == 0)
            {
                Emit(new { @event = "error", message = "There are no .brstm songs in there." });
                return 1;
            }

            bool alreadyCopied = PackImporter.IsImportedCopy(folder, output);
            string destination = alreadyCopied ? folder : PackImporter.NewFolder(output, packName);
            Emit(new { @event = "log", message = "Copying the songs…" });
            var copies = alreadyCopied
                ? plan.Matches.Select(match => match.File).Concat(plan.Loose).ToDictionary(file => file, file => file, StringComparer.OrdinalIgnoreCase)
                : PackImporter.CopyFiles(plan, destination);
            int assigned = PackImporter.Assign(plan, copies, replace);
            library.Add(plan.Matches.Where(match => !match.IsFinal).Select(match => copies[match.File]).Concat(plan.Loose.Select(file => copies[file])));
            Project.From(project.Options, rows).Save(AppStorage.AutosavePath);

            string extra = plan.Loose.Count > 0 ? $" {plan.Loose.Count} matched no track, so they're only in your songs." : "";
            Emit(new
            {
                @event = "done",
                message = plan.Tracks == 0
                    ? $"{packName} has {plan.Loose.Count} songs, but none match a track by name. They're in your songs."
                    : $"Imported {assigned} songs from {packName}.{extra}",
                where = destination,
                built = assigned,
            });
            return 0;
        }
        finally
        {
            if (extracted != null)
            {
                try
                {
                    Directory.Delete(extracted, true);
                }
                catch (IOException)
                {
                    // It's in the temp folder, so Windows cleans it up later.
                }
            }
        }
    });

    /// <summary>Gives each selected track the song whose file name names it.</summary>
    private static int Match(string[] args) => Guard(() =>
    {
        var spec = ReadSpec(args);
        var project = LoadProject();
        var rows = RRMusicPackMaker.Catalog.Build(RetroRewindFolder(project), out _);
        var wanted = new HashSet<string>(spec.Tracks, StringComparer.OrdinalIgnoreCase);
        var targets = rows.Where(row => wanted.Count == 0 || wanted.Contains(row.FileBase)).ToList();
        var (matched, unmatched) = PackBuilder.AutoMatch(spec.Files, targets);
        Emit(new
        {
            matches = targets.Where(row => row.IsAssigned).Select(row => new { file = row.FileBase, source = row.Source }),
            unmatched,
            matched,
        });
        return 0;
    });

    private static int Search(string[] args) => Guard(() =>
    {
        string query = Option(args, "--query") ?? "";
        var results = Downloader.SearchAsync(query, message => Emit(new { @event = "log", message }), CancellationToken.None).GetAwaiter().GetResult();
        Emit(new
        {
            @event = "done",
            results = results.Select(r => new { link = r.Link, title = r.Title, uploader = r.Uploader, duration = r.Duration, album = r.Album, plays = r.Plays, official = r.Official }),
        });
        return 0;
    });

    private static int Download(string[] args) => Guard(() =>
    {
        string link = Option(args, "--link") ?? throw new InvalidDataException("Give --link.");
        var settings = AppStorage.LoadSettings();
        int kbps = int.TryParse(Option(args, "--kbps"), out int chosen) ? chosen : settings.DownloadKbps;
        int shown = -1;
        var song = Downloader.DownloadAsync(link, DownloadFolder(settings), message => Emit(new { @event = "log", message }), fraction =>
        {
            int percent = (int)(fraction * 100);
            if (percent != shown)
            {
                shown = percent;
                Emit(new { @event = "progress", done = percent, total = 100 });
            }
        }, CancellationToken.None, kbps).GetAwaiter().GetResult();
        Library(settings).Add(song.Path);
        Emit(new { @event = "done", message = $"Downloaded {song.Title}.", path = song.Path });
        return 0;
    });

    /// <summary>The song's shape for drawing, its saved loop, and the loops the finder suggests.</summary>
    private static int Waveform(string[] args) => Guard(() =>
    {
        string song = Option(args, "--song") ?? throw new InvalidDataException("Give --song.");
        int points = int.TryParse(Option(args, "--points"), out int n) ? Math.Clamp(n, 100, 4000) : 1200;
        const int rate = 32000;
        var channels = AudioIO.Decode(song, rate);
        int length = channels[0].Length;
        var peaks = new List<float[]>(points);
        for (int i = 0; i < points; i++)
        {
            int from = (int)((long)length * i / points), to = Math.Max(from + 1, (int)((long)length * (i + 1) / points));
            short low = 0, high = 0;
            foreach (var channel in channels)
                for (int s = from; s < to && s < length; s++)
                {
                    low = Math.Min(low, channel[s]);
                    high = Math.Max(high, channel[s]);
                }
            peaks.Add([low / 32768f, high / 32768f]);
        }
        long? loopStart = null, loopEnd = null;
        int sourceRate = rate;
        try
        {
            var info = AudioIO.Probe(song);
            (loopStart, loopEnd, sourceRate) = (info.LoopStart, info.LoopEnd, info.SampleRate);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Without ffprobe there's no saved loop to show, only the shape.
        }
        var found = args.Contains("--find") ? LoopFinder.FindCandidates(channels, rate) : [];
        Emit(new
        {
            @event = "done",
            seconds = length / (double)rate,
            peaks,
            loopStart = loopStart / (double)sourceRate,
            loopEnd = loopEnd / (double)sourceRate,
            found = found.Select(f => new { start = f.Start / (double)rate, end = f.End / (double)rate, score = f.Score }),
        });
        return 0;
    });

    /// <summary>The few seconds either side of a loop's jump, joined, so the seam can be heard.</summary>
    private static int LoopSeam(string[] args) => Guard(() =>
    {
        string song = Option(args, "--song") ?? throw new InvalidDataException("Give --song.");
        double start = double.Parse(Option(args, "--start") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        double end = double.Parse(Option(args, "--end") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        const int rate = 32000;
        var channels = AudioIO.Decode(song, rate);
        int length = channels[0].Length;
        int a = Math.Clamp((int)(start * rate), 0, length), b = Math.Clamp((int)(end * rate), a, length);
        int lead = Math.Min(3 * rate, b - a), tail = Math.Min(3 * rate, b - a);
        var joined = channels.Select(channel =>
        {
            var part = new short[lead + tail];
            Array.Copy(channel, b - lead, part, 0, lead);
            Array.Copy(channel, a, part, lead, tail);
            return part;
        }).ToArray();
        Emit(new { @event = "done", path = AudioIO.WriteRangePreview(joined, rate, 0, (lead + tail) / (double)rate + 1) });
        return 0;
    });

    /// <summary>Renders the editor's parts. With --save it becomes a new song in the downloads folder and the library.</summary>
    private static int RenderEdit(string[] args) => Guard(() =>
    {
        var spec = ReadSpec(args);
        if (spec.Clips.Count == 0)
            throw new InvalidDataException("Add a song to the editor first.");
        var clips = spec.Clips.Select(c => new EditClip(c.Source, c.Start, c.End)).ToList();
        var audio = clips.Select(c => c.Source).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(source => source, source => AudioIO.Decode(source, EditTimeline.Rate), StringComparer.OrdinalIgnoreCase);
        var speed = PackBuilder.SpeedChoices[Math.Clamp(spec.Speed, 0, PackBuilder.SpeedChoices.Length - 1)];
        var rendered = EditTimeline.ApplyFade(EditorPanel.ApplySpeed(EditTimeline.Render(clips, source => audio[source], spec.Blend), speed),
            EditTimeline.Rate, spec.FadeIn, spec.FadeOut);
        string wav = AudioIO.WriteRangePreview(rendered, EditTimeline.Rate, 0, rendered[0].Length / (double)EditTimeline.Rate + 1);
        if (!args.Contains("--save"))
        {
            Emit(new { @event = "done", path = wav });
            return 0;
        }
        var settings = AppStorage.LoadSettings();
        string folder = DownloadFolder(settings);
        Directory.CreateDirectory(folder);
        string name = spec.Name.Trim().Length > 0 ? spec.Name.Trim() : clips[0].Name + " edit";
        string target = EditTimeline.UniquePath(folder, name);
        File.Move(wav, target);
        Library(settings).AddEdit(target, clips);
        Emit(new { @event = "done", message = $"Saved {Path.GetFileNameWithoutExtension(target)}.", path = target });
        return 0;
    });

    /// <summary>A song's length in the editor's own sample rate, for laying out its part.</summary>
    private static int Length(string[] args) => Guard(() =>
    {
        string song = Option(args, "--song") ?? throw new InvalidDataException("Give --song.");
        var channels = AudioIO.Decode(song, EditTimeline.Rate);
        Emit(new { @event = "done", samples = channels[0].Length, rate = EditTimeline.Rate });
        return 0;
    });

    private static int Check()
    {
        var project = LoadProject();
        string? rr = RetroRewindFolder(project);
        string? newer = null;
        try
        {
            if (AppStorage.LoadSettings().CheckForUpdates)
                {
                var task = UpdateCheck.FindNewerAsync();
                newer = task.Wait(TimeSpan.FromSeconds(6)) ? task.Result : null;
            }
        }
        catch (AggregateException)
        {
            // No internet only means no update notice.
        }
        Emit(new
        {
            issues = rr != null && Directory.Exists(rr) ? SetupCheck.Run(rr) : [],
            ffmpeg = FfmpegInstaller.IsInstalled,
            ytdlp = Downloader.FindTool() != null,
            retroRewind = rr,
            newer,
        });
        return 0;
    }

    /// <summary>Installs yt-dlp, which finds and downloads songs, or checks it's there.</summary>
    private static int GetYtDlp() => Guard(() =>
    {
        string path = Downloader.EnsureToolAsync(message => Emit(new { @event = "log", message }), CancellationToken.None).GetAwaiter().GetResult();
        Emit(new { @event = "done", message = "yt-dlp is installed.", where = path });
        return 0;
    });

    private static int GetFfmpeg() => Guard(() =>
    {
        int shown = -1;
        string folder = FfmpegInstaller.InstallAsync(message => Emit(new { @event = "log", message }), fraction =>
        {
            int percent = (int)(fraction * 100);
            if (percent != shown)
            {
                shown = percent;
                Emit(new { @event = "progress", done = percent, total = 100 });
            }
        }, CancellationToken.None).GetAwaiter().GetResult();
        Emit(new { @event = "done", message = "FFmpeg is installed.", where = folder });
        return 0;
    });
}
