using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRMusicPackMaker;

public sealed class Options
{
    public const string LoudnessEven = "Even loudness";
    public const string LoudnessPeak = "Peak only";
    public const string LoudnessOff = "Off";

    public string PackName { get; set; } = "My Music Pack";
    public string Author { get; set; } = "";
    public string RetroRewindFolder { get; set; } = "";
    public int SampleRate { get; set; } = 32000;
    public string Loudness { get; set; } = LoudnessEven;

    /// <summary>Writes the 4, 6 or 8 channels the stock game uses. Only needed outside Retro Rewind.</summary>
    public bool MultiChannelSlots { get; set; }

    /// <summary>Writes _f files for stock slots, used when the final lap speed-up setting is off.</summary>
    public bool StockFinalLapFiles { get; set; } = true;
}

public sealed class ProjectRow
{
    public string File { get; set; } = "";
    public string Source { get; set; } = "";
    public string FinalSource { get; set; } = "";
    public string Song { get; set; } = "";
    public string Credits { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LoopMode Loop { get; set; } = LoopMode.Auto;
    public string LoopStart { get; set; } = "";
    public string LoopEnd { get; set; } = "";
    public double Crossfade { get; set; }
    public int VolumeDb { get; set; }
}

public sealed class Project
{
    public Options Options { get; set; } = new();
    public List<ProjectRow> Rows { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Project Load(string path) =>
        JsonSerializer.Deserialize<Project>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"{path} is not a project file.");

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    public static Project From(Options options, IEnumerable<TrackRow> rows) => new()
    {
        Options = options,
        Rows = rows.Where(r => r.IsAssigned || r.Song.Length > 0 || r.Credits.Length > 0).Select(r => new ProjectRow
        {
            File = r.FileBase,
            Source = r.Source,
            FinalSource = r.FinalSource,
            Song = r.Song,
            Credits = r.Credits,
            Loop = r.Loop,
            LoopStart = r.LoopStart,
            LoopEnd = r.LoopEnd,
            Crossfade = r.Crossfade,
            VolumeDb = r.VolumeDb,
        }).ToList(),
    };

    /// <summary>Copies saved choices onto catalog rows. Returns the file names no row matched.</summary>
    public List<string> ApplyTo(List<TrackRow> rows)
    {
        var byFile = rows.ToDictionary(r => r.FileBase, StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();
        foreach (var saved in Rows)
        {
            if (!byFile.TryGetValue(saved.File, out var row))
            {
                unmatched.Add(saved.File);
                continue;
            }
            row.Source = saved.Source;
            row.FinalSource = saved.FinalSource;
            row.Song = saved.Song;
            row.Credits = saved.Credits;
            row.Loop = saved.Loop;
            row.LoopStart = saved.LoopStart;
            row.LoopEnd = saved.LoopEnd;
            row.Crossfade = saved.Crossfade;
            row.VolumeDb = saved.VolumeDb;
        }
        return unmatched;
    }
}

public sealed record BuildResult(string Folder, string MusicFolder, string ZipPath, int Built, int Failed, long Bytes, int Unchanged, int Encoded);

public static class PackBuilder
{
    private const double MaxShortSeconds = 15;

    // Measured from a real race: music reaches the speakers about 8 dB lower than on the title screen,
    // because the game's race mix turns it down. This wins back half of that without crushing the song.
    private const double RaceBoostDb = 4;
    private const double MaximumTotalBoostDb = 9;
    private const string KeptModName = "Kept Patches files";

    public static readonly (string Text, double Seconds)[] CrossfadeChoices =
        [("Off: exact jump", 0), ("0.25 seconds", 0.25), ("0.5 seconds", 0.5), ("1 second", 1), ("2 seconds", 2)];

    public static readonly int[] VolumeChoices = [-9, -6, -3, 0, 3, 6, 9];

    public static int CrossfadeIndex(double seconds)
    {
        int index = Array.FindIndex(CrossfadeChoices, choice => choice.Seconds == seconds);
        return Math.Max(0, index);
    }

    public static int VolumeIndex(int decibels)
    {
        int index = Array.IndexOf(VolumeChoices, decibels);
        return index >= 0 ? index : Array.IndexOf(VolumeChoices, 0);
    }

    public static string SafeName(string name)
    {
        var clean = new StringBuilder();
        foreach (char c in name.Trim())
            clean.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return clean.Length == 0 ? "Music Pack" : clean.ToString().TrimEnd('.', ' ');
    }

    public static BuildResult Build(Options options, IReadOnlyList<TrackRow> rows, string outputFolder, Action<string> log, Action<int, int>? progress = null)
    {
        var work = rows.Where(r => r.IsAssigned).ToList();
        if (work.Count == 0)
            throw new InvalidOperationException("No songs are assigned. Add a song to at least one track first.");

        string name = SafeName(options.PackName);
        string packFolder = Path.Combine(outputFolder, name);
        string musicFolder = Path.Combine(packFolder, "RetroRewind6", "Patches");

        // Packs built by older versions kept their songs in MyStuff. Reuse them, and keep that folder out of the zip.
        string oldFolder = Path.Combine(packFolder, "RetroRewind6", "MyStuff");
        if (Directory.Exists(oldFolder))
        {
            if (Directory.Exists(musicFolder))
                Directory.Delete(oldFolder, true);
            else
                Directory.Move(oldFolder, musicFolder);
        }
        Directory.CreateDirectory(musicFolder);

        string manifestPath = Path.Combine(outputFolder, name + ".build.json");
        var manifest = LoadManifest(manifestPath);
        var plans = work.Select(row => (Row: row, Key: BuildKey(options, row), Files: ExpectedFiles(options, row))).ToList();

        // Files from tracks that no longer have a song must not stay in the pack.
        var expected = new HashSet<string>(plans.SelectMany(plan => plan.Files), StringComparer.OrdinalIgnoreCase);
        foreach (string old in Directory.GetFiles(musicFolder, "*.brstm"))
            if (!expected.Contains(Path.GetFileNameWithoutExtension(old)))
                File.Delete(old);
        foreach (string key in manifest.Keys.Where(key => !work.Any(row => row.FileBase == key)).ToList())
            manifest.Remove(key);

        var pending = new List<(TrackRow Row, string Key, List<string> Files)>();
        int unchanged = 0;
        foreach (var plan in plans)
        {
            bool current = manifest.TryGetValue(plan.Row.FileBase, out string? saved) && saved == plan.Key
                && plan.Files.All(file => File.Exists(Path.Combine(musicFolder, file + ".brstm")));
            if (current)
            {
                plan.Row.Status = "Built";
                unchanged++;
            }
            else
            {
                pending.Add(plan);
            }
        }
        if (unchanged > 0)
            log($"{unchanged} song{(unchanged == 1 ? " is" : "s are")} unchanged, so not encoded again.");

        int done = unchanged;
        int failed = 0;
        int encoded = 0;
        object gate = new();
        progress?.Invoke(done, work.Count);

        // Tracks with the same song and settings are encoded once, then copied.
        Parallel.ForEach(pending.GroupBy(plan => plan.Key).ToList(), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, group =>
        {
            var members = group.ToList();
            bool encodedGroup = false;
            foreach (var plan in members)
            {
                var row = plan.Row;
                void Log(string message) => log($"{row.Name}: {message}");
                try
                {
                    if (!encodedGroup)
                    {
                        BuildRow(options, row, musicFolder, Log);
                        encodedGroup = true;
                        Interlocked.Increment(ref encoded);
                    }
                    else
                    {
                        var first = members[0].Files;
                        for (int i = 0; i < plan.Files.Count; i++)
                            File.Copy(Path.Combine(musicFolder, first[i] + ".brstm"), Path.Combine(musicFolder, plan.Files[i] + ".brstm"), true);
                    }
                    WarnAboutFinal(row, Log);
                    row.Status = "Built";
                    lock (gate)
                        manifest[row.FileBase] = plan.Key;
                }
                catch (Exception ex)
                {
                    row.Status = "Failed";
                    Log($"FAILED. {ex.Message}");
                    Interlocked.Increment(ref failed);
                    lock (gate)
                        manifest.Remove(row.FileBase);
                }
                progress?.Invoke(Interlocked.Increment(ref done), work.Count);
            }
        });

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        WriteCredits(Path.Combine(packFolder, "credits.csv"), work);
        WriteReadme(Path.Combine(packFolder, "README.txt"), options, work);

        // A zip with no music in it would only look like a finished pack.
        string zip = Path.Combine(outputFolder, name + ".zip");
        File.Delete(zip);
        var music = Directory.GetFiles(musicFolder, "*.brstm");
        if (music.Length > 0)
            ZipFile.CreateFromDirectory(packFolder, zip, CompressionLevel.Fastest, false);

        long bytes = music.Sum(f => new FileInfo(f).Length);
        return new BuildResult(packFolder, musicFolder, zip, work.Count - failed, failed, bytes, unchanged, encoded);
    }

    private static Dictionary<string, string> LoadManifest(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            // A damaged record only means everything is encoded again.
        }
        return [];
    }

    /// <summary>The file names one track writes, in a fixed order: main, other names, then final lap.</summary>
    internal static List<string> ExpectedFiles(Options options, TrackRow row)
    {
        var files = new List<string> { row.FileBase };
        files.AddRange(row.AliasBases);
        if (row.StockFinalBase != null && (row.FinalSource.Length > 0 || options.StockFinalLapFiles))
            files.Add(row.StockFinalBase);
        if (row.PulsarFinalBase != null && row.FinalSource.Length > 0)
            files.Add(row.PulsarFinalBase);
        return files;
    }

    /// <summary>Changes whenever the track's output would change. Two tracks with the same key produce the same audio.</summary>
    internal static string BuildKey(Options options, TrackRow row)
    {
        static string Stamp(string path)
        {
            if (path.Length == 0)
                return "none";
            var info = new FileInfo(path);
            return info.Exists ? $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" : $"{path}|missing";
        }

        string text = string.Join("\n",
            "5",
            Stamp(row.Source),
            Stamp(row.FinalSource),
            row.FixedRate ?? options.SampleRate,
            row.Loop,
            row.DefaultNoLoop,
            row.LoopStart,
            row.LoopEnd,
            row.Crossfade.ToString(CultureInfo.InvariantCulture),
            EffectiveVolumeDb(options, row),
            options.Loudness,
            options.MultiChannelSlots ? row.StockChannels : 2,
            options.StockFinalLapFiles,
            row.StockFinalBase != null,
            row.PulsarFinalBase != null,
            row.AliasBases.Length);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>The song's own volume setting, plus the race boost when the pack evens out its songs.</summary>
    internal static double EffectiveVolumeDb(Options options, TrackRow row)
    {
        if (options.Loudness != Options.LoudnessEven || !row.IsRaceMusic)
            return row.VolumeDb;
        return Math.Min(row.VolumeDb + RaceBoostDb, Math.Max(row.VolumeDb, MaximumTotalBoostDb));
    }

    private static void BuildRow(Options options, TrackRow row, string musicFolder, Action<string> log)
    {
        double volume = EffectiveVolumeDb(options, row);
        if (!File.Exists(row.Source))
            throw new FileNotFoundException($"Song file not found: {row.Source}");

        int rate = row.FixedRate ?? options.SampleRate;
        var mode = row.Loop == LoopMode.Auto && row.DefaultNoLoop ? LoopMode.NoLoop : row.Loop;
        var song = Processing.Prepare(row.Source, rate, mode, row.LoopStart, row.LoopEnd, options.Loudness, log, volume, row.Crossfade);

        // The file stops at the loop end, so anything after it is not stored.
        double seconds = (song.Loop ? song.LoopEnd : song.Length) / (double)rate;
        if (!song.Loop && seconds > MaxShortSeconds)
            log($"Warning: {seconds:0} seconds with no loop. A very long fanfare can freeze the game.");

        int channels = options.MultiChannelSlots ? row.StockChannels : 2;
        foreach (string file in new[] { row.FileBase }.Concat(row.AliasBases))
            AudioIO.WriteBrstm(song, Path.Combine(musicFolder, file + ".brstm"), channels, rate);

        // Stock slots play their _f file when the final lap speed-up setting is off.
        if (row.StockFinalBase != null)
        {
            if (row.FinalSource.Length > 0)
            {
                var final = Processing.Prepare(row.FinalSource, rate, LoopMode.Auto, "", "", options.Loudness, log, volume, row.Crossfade);
                AudioIO.WriteBrstm(final, Path.Combine(musicFolder, row.StockFinalBase + ".brstm"), channels, rate);
            }
            else if (options.StockFinalLapFiles)
            {
                int fastRate = (int)Math.Round(rate * 1.06);
                AudioIO.WriteBrstm(song, Path.Combine(musicFolder, row.StockFinalBase + ".brstm"), channels, fastRate);
            }
        }

        // Retro Rewind speeds up the normal file by itself, so a final lap file is only for a different song.
        if (row.PulsarFinalBase != null && row.FinalSource.Length > 0)
        {
            var final = Processing.Prepare(row.FinalSource, rate, LoopMode.Auto, "", "", options.Loudness, log, volume, row.Crossfade);
            AudioIO.WriteBrstm(final, Path.Combine(musicFolder, row.PulsarFinalBase + ".brstm"), 2, rate);
        }

        string loopText = song.Loop
            ? $"loop {AudioIO.FormatTime((double)song.LoopStart / rate)} to {AudioIO.FormatTime((double)song.LoopEnd / rate)}"
            : "no loop";
        log($"{row.FileBase}.brstm, {rate} Hz, {channels} channels, {AudioIO.FormatTime(seconds)}, {loopText}.");
    }

    private static void WarnAboutFinal(TrackRow row, Action<string> log)
    {
        if (row.PulsarFinalBase != null && row.FinalSource.Length == 0 && row.ExistingFinalFiles.Count > 0)
            log($"Warning: Retro Rewind has {row.ExistingFinalFiles[0]}.brstm for this track. "
                + "It still plays on the final lap with the speed-up setting off, unless you set a final lap song.");
    }

    /// <summary>Gives each track a song from the list in shuffled order, reshuffling if there are more tracks than songs.</summary>
    public static void FillFromFolder(IReadOnlyList<string> files, IEnumerable<TrackRow> rows, Random random)
    {
        var order = new List<string>();
        foreach (var row in rows)
        {
            if (order.Count == 0)
                order = files.OrderBy(_ => random.Next()).ToList();
            string file = order[^1];
            order.RemoveAt(order.Count - 1);
            row.Source = file;
            row.Song = Path.GetFileNameWithoutExtension(file);
            row.Status = "";
        }
    }

    private static void WriteCredits(string path, IEnumerable<TrackRow> rows)
    {
        static string Csv(string value) => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        var text = new StringBuilder("Track,Song,Credits,File Name\r\n");
        foreach (var row in rows)
            text.Append(string.Join(",", Csv(row.Name), Csv(row.Song), Csv(row.Credits), Csv(row.FileBase))).Append("\r\n");
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }

    private static void WriteReadme(string path, Options options, IEnumerable<TrackRow> rows)
    {
        var text = new StringBuilder();
        text.AppendLine(options.PackName);
        if (options.Author.Length > 0)
            text.AppendLine($"By {options.Author}");
        text.AppendLine();
        text.AppendLine("INSTALL");
        text.AppendLine("Real Wii: copy the RetroRewind6 folder from this zip to the root of your SD card and merge it with the one there.");
        text.AppendLine("The songs must end up in SD:/RetroRewind6/Patches.");
        text.AppendLine();
        text.AppendLine("Wheel Wizard or Wheel Wizard+: import this zip as a mod, then turn the mod on.");
        text.AppendLine();
        text.AppendLine("Dolphin with Riivolution: copy the RetroRewind6 folder into your Riivolution folder and merge it with the one there.");
        text.AppendLine();
        text.AppendLine("No sound? Check that Retro Rewind's Music setting isn't set to disable music.");
        text.AppendLine("Old music still plays? Check that its Loose Archive Overrides setting is on, which is the default.");
        text.AppendLine();
        text.AppendLine("TRACKS");
        foreach (var row in rows)
            text.AppendLine(row.Song.Length > 0 ? $"{row.Name}: {row.Song}{(row.Credits.Length > 0 ? $" ({row.Credits})" : "")}" : row.Name);
        File.WriteAllText(path, text.ToString());
    }

    /// <summary>Songs that would replace a different file already in the Patches folder.</summary>
    public static List<string> PatchesConflicts(string musicFolder, string retroRewindFolder)
    {
        string target = Path.Combine(retroRewindFolder, "Patches");
        var conflicts = new List<string>();
        if (!Directory.Exists(target))
            return conflicts;
        foreach (string file in Directory.GetFiles(musicFolder, "*.brstm"))
        {
            var existing = new FileInfo(Path.Combine(target, Path.GetFileName(file)));
            if (existing.Exists && existing.Length != new FileInfo(file).Length)
                conflicts.Add(existing.Name);
        }
        return conflicts;
    }

    /// <summary>Copies the pack's songs into Patches. Other files there are left alone. Returns how many were copied.</summary>
    public static int CopyToPatches(string musicFolder, string retroRewindFolder)
    {
        string target = Path.Combine(retroRewindFolder, "Patches");
        Directory.CreateDirectory(target);
        var files = Directory.GetFiles(musicFolder, "*.brstm");
        foreach (string file in files)
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        return files.Length;
    }

    /// <summary>
    /// Takes the pack out of the game: its mods, and its songs in Patches unless another mod still needs them.
    /// The built pack and the original songs are left alone. Returns how many files left Patches.
    /// </summary>
    public static int RemovePack(string packName, IEnumerable<string> modsFolders, string retroRewindFolder, string? musicFolder)
    {
        string name = SafeName(packName);
        var folders = modsFolders.ToList();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (musicFolder != null && Directory.Exists(musicFolder))
            foreach (string file in Directory.GetFiles(musicFolder, "*.brstm"))
                files.Add(Path.GetFileName(file));
        foreach (string mods in folders)
        {
            string folder = Path.Combine(mods, name);
            if (!Directory.Exists(folder))
                continue;
            foreach (string file in Directory.GetFiles(folder))
                if (!Path.GetExtension(file).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                    files.Add(Path.GetFileName(file));
            Directory.Delete(folder, true);
        }

        var stillNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string mods in folders.Where(Directory.Exists))
            foreach (string file in Directory.GetFiles(mods, "*", SearchOption.AllDirectories))
                stillNeeded.Add(Path.GetFileName(file));

        int removed = 0;
        string patches = Path.Combine(retroRewindFolder, "Patches");
        foreach (string file in files.Where(file => !stillNeeded.Contains(file)))
        {
            string path = Path.Combine(patches, file);
            if (File.Exists(path))
            {
                File.Delete(path);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>Roughly how big one track's files will be. DSP-ADPCM stores 14 samples in 8 bytes, per channel.</summary>
    internal static long EstimateBytes(double seconds, int rate, int channels, bool hasFinalFile)
    {
        long bytes = (long)(seconds * rate * channels * 8 / 14);
        return hasFinalFile ? bytes * 2 : bytes;
    }

    /// <summary>Other mods that replace any of the same songs as this pack.</summary>
    public static List<string> ModsSharingSongs(string musicFolder, string packName, string modsFolder)
    {
        string name = SafeName(packName);
        var songs = new HashSet<string>(Directory.GetFiles(musicFolder, "*.brstm").Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        if (!Directory.Exists(modsFolder))
            return result;
        foreach (string folder in Directory.GetDirectories(modsFolder))
        {
            string title = Path.GetFileName(folder);
            if (!title.Equals(name, StringComparison.OrdinalIgnoreCase)
                && Directory.GetFiles(folder, "*.brstm", SearchOption.AllDirectories).Any(f => songs.Contains(Path.GetFileName(f))))
                result.Add(title);
        }
        return result;
    }

    /// <summary>
    /// Every mods folder Wheel Wizard or Wheel Wizard+ may read. Both use the data folder saved in the
    /// registry if it was moved, and older versions always use the default one, so both get the pack.
    /// </summary>
    public static List<string> ModsFolders()
    {
        if (Environment.GetEnvironmentVariable("RRMUSIC_MODS") is { Length: > 0 } custom)
            return [custom];

        var folders = new List<string>();
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\WheelWizard");
        if (key?.GetValue("AppDataLocation") is string moved && Directory.Exists(moved))
            folders.Add(Path.Combine(moved, "Mods"));
        string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CT-MKWII");
        if (Directory.Exists(standard))
            folders.Add(Path.Combine(standard, "Mods"));
        return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Files in the Patches folder that no mod owns. Wheel Wizard deletes these once any mod is enabled.</summary>
    public static List<string> FindUnownedPatches(string retroRewindFolder, IEnumerable<string> modsFolders, IEnumerable<string>? packSongs = null)
    {
        string patches = Path.Combine(retroRewindFolder, "Patches");
        if (!Directory.Exists(patches))
            return [];
        // The pack's own songs are about to be owned by its mod, so they aren't at risk.
        var owned = new HashSet<string>(packSongs?.Select(song => Path.GetFileName(song)!) ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (string modsFolder in modsFolders)
            if (Directory.Exists(modsFolder))
                foreach (var file in Directory.GetFiles(modsFolder, "*", SearchOption.AllDirectories))
                    owned.Add(Path.GetFileName(file));
        return Directory.GetFiles(patches).Where(f => !owned.Contains(Path.GetFileName(f))).ToList();
    }

    /// <summary>Adds the pack to Wheel Wizard's mods. Wheel Wizard copies it into Patches when the game launches.</summary>
    public static string InstallAsMod(string musicFolder, string packName, string author, string modsFolder, IEnumerable<string>? keepFiles = null)
    {
        string name = SafeName(packName);
        string folder = WriteMod(modsFolder, name, author.Length > 0 ? author : "-1", 0);
        foreach (var old in Directory.GetFiles(folder, "*.brstm"))
            File.Delete(old);
        foreach (var file in Directory.GetFiles(musicFolder, "*.brstm"))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
        RemoveFromKeptMod(musicFolder, modsFolder);
        WinOverSharedSongs(modsFolder, name, ModsSharingSongs(musicFolder, packName, modsFolder));

        var keep = keepFiles?.ToList() ?? [];
        if (keep.Count > 0)
        {
            string keepFolder = WriteMod(modsFolder, KeptModName, "-1", 1);
            foreach (var file in keep)
                File.Copy(file, Path.Combine(keepFolder, Path.GetFileName(file)), true);
        }
        return folder;
    }

    /// <summary>In Wheel Wizard the lowest priority number wins, so the pack must rank above mods with the same songs.</summary>
    private static void WinOverSharedSongs(string modsFolder, string name, List<string> others)
    {
        if (others.Count == 0)
            return;
        string ini = Path.Combine(modsFolder, name, name + ".ini");
        int mine = ReadPriority(ini) ?? 0;
        int best = others.Select(other => ReadPriority(Path.Combine(modsFolder, other, other + ".ini")) ?? int.MaxValue).Min();
        if (mine < best)
            return;
        var lines = File.ReadAllLines(ini).Select(line =>
            line.StartsWith("Priority", StringComparison.OrdinalIgnoreCase) ? $"Priority = {best - 1}" : line);
        File.WriteAllLines(ini, lines);
    }

    private static int? ReadPriority(string ini)
    {
        if (!File.Exists(ini))
            return null;
        foreach (string line in File.ReadAllLines(ini))
            if (line.StartsWith("Priority", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(line.IndexOf('=') + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
        return null;
    }

    private static string WriteMod(string modsFolder, string title, string author, int priorityOffset)
    {
        string folder = Path.Combine(modsFolder, title);
        Directory.CreateDirectory(folder);
        string ini = Path.Combine(folder, title + ".ini");

        int priority = priorityOffset;
        if (File.Exists(ini))
        {
            foreach (string line in File.ReadAllLines(ini))
                if (line.StartsWith("Priority", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line[(line.IndexOf('=') + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int saved))
                    priority = saved;
        }
        else if (Directory.Exists(modsFolder))
        {
            foreach (string other in Directory.GetFiles(modsFolder, "*.ini", SearchOption.AllDirectories))
                foreach (string line in File.ReadAllLines(other))
                    if (line.StartsWith("Priority", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(line[(line.IndexOf('=') + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int used))
                        priority = Math.Max(priority, used + 1);
        }

        File.WriteAllText(ini, $"[Mod]\r\nName = {title}\r\nAuthor = {author}\r\nModID = -1\r\nIsEnabled = True\r\nPriority = {priority}\r\n");
        return folder;
    }

    /// <summary>Older versions could save the pack's own songs in the kept mod, where stale copies linger. Clears them.</summary>
    private static void RemoveFromKeptMod(string musicFolder, string modsFolder)
    {
        string kept = Path.Combine(modsFolder, KeptModName);
        if (!Directory.Exists(kept))
            return;
        foreach (string song in Directory.GetFiles(musicFolder, "*.brstm"))
            File.Delete(Path.Combine(kept, Path.GetFileName(song)));
        string ini = KeptModName + ".ini";
        if (Directory.GetFiles(kept).All(file => Path.GetFileName(file).Equals(ini, StringComparison.OrdinalIgnoreCase)))
            Directory.Delete(kept, true);
    }

    private static readonly string[] AudioExtensions = [".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".opus", ".wma", ".brstm", ".webm", ".mp4"];

    public static bool IsAudioFile(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Assigns songs to rows by file name. Returns how many matched, plus the names that did not.</summary>
    public static (int Matched, List<string> Unmatched) AutoMatch(IEnumerable<string> files, IReadOnlyList<TrackRow> rows)
    {
        var unmatched = new List<string>();
        int matched = 0;
        var taken = new HashSet<TrackRow>();
        var keys = rows.Select(r => (Row: r, Names: MatchNames(r.Name), File: Normalize(r.FileBase))).ToList();
        foreach (string file in files)
        {
            string title = Normalize(Path.GetFileNameWithoutExtension(file));
            var exact = keys.Where(k => k.File == title || k.Names.Contains(title)).ToList();
            List<TrackRow> found;
            if (exact.Count > 0)
            {
                found = exact.Select(k => k.Row).ToList();
            }
            else
            {
                var contained = keys
                    .Select(k => (k.Row, Length: k.Names.Where(n => n.Length >= 5 && title.Contains(n)).Select(n => n.Length).DefaultIfEmpty(0).Max()))
                    .Where(k => k.Length > 0)
                    .ToList();
                int longest = contained.Count == 0 ? 0 : contained.Max(k => k.Length);
                found = contained.Where(k => k.Length == longest).Select(k => k.Row).ToList();
            }
            if (found.Count == 1 && !taken.Add(found[0]))
            {
                unmatched.Add($"{Path.GetFileName(file)} (another song in this batch already matched {found[0].Name})");
            }
            else if (found.Count == 1)
            {
                found[0].Source = file;
                if (found[0].Song.Length == 0)
                    found[0].Song = Path.GetFileNameWithoutExtension(file);
                matched++;
            }
            else
            {
                unmatched.Add(Path.GetFileName(file));
            }
        }
        return (matched, unmatched);
    }

    /// <summary>The names a song file may use for a row: "A / B (note)" gives A and B, "Main > Variant" gives Variant.</summary>
    private static string[] MatchNames(string rowName)
    {
        if (rowName.Contains("(variant "))
            return [];
        string name = rowName.Contains(" > ") ? rowName[(rowName.IndexOf(" > ") + 3)..] : rowName;
        int note = name.IndexOf(" (", StringComparison.Ordinal);
        if (note > 0)
            name = name[..note];
        return name.Split(" / ").Select(Normalize).ToArray();
    }

    private static string Normalize(string text)
    {
        var clean = new StringBuilder();
        foreach (char c in text.ToLowerInvariant())
            if (char.IsLetterOrDigit(c))
                clean.Append(c);
        return clean.ToString();
    }
}
