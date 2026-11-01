using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RRMusicPackMaker;

/// <summary>
/// Raises the music volumes stored in revo_kart.brsar, which cap how loud any song can play.
/// The patched copy is made from the user's own game file and is only ever installed on their PC.
/// It's the game's data, so it must never go into an exported pack.
/// </summary>
public static class SoundArchive
{
    public const string FileName = "revo_kart.brsar";

    // In-game tests: 1.5x and 2x the stock value played equally loud, at the song's own level, so
    // the game stops at full scale, and 3x played quieter partway through. 127 is full scale.
    private const byte FullVolume = 127;
    private const byte StreamSound = 2;
    private const int VolumeOffset = 0x14;
    private const int TypeOffset = 0x16;

    private static string CachePath => Path.Combine(AppStorage.Folder, "sound", FileName);

    /// <summary>Once the patched copy exists, the game file isn't needed any more.</summary>
    public static bool IsReady => File.Exists(CachePath);

    /// <summary>Sets every race and battle music slot to full volume. Returns how many changed.</summary>
    internal static int BoostMusic(byte[] data)
    {
        if (data.Length < 0x28 || Encoding.ASCII.GetString(data, 0, 4) != "RSAR")
            throw new InvalidDataException("That isn't a sound archive.");

        int symbols = Offset(data, 0x10) + 8;
        int info = Offset(data, 0x18) + 8;
        if (Encoding.ASCII.GetString(data, symbols - 8, 4) != "SYMB" || Encoding.ASCII.GetString(data, info - 8, 4) != "INFO")
            throw new InvalidDataException("The sound archive has an unknown layout, so it wasn't changed.");

        int names = symbols + Offset(data, symbols);
        int nameCount = Offset(data, names);
        int sounds = info + Offset(data, info + 4);
        int soundCount = Offset(data, sounds);

        int streams = 0;
        int changed = 0;
        for (int i = 0; i < soundCount; i++)
        {
            int entry = info + Offset(data, sounds + 4 + i * 8 + 4);
            if (data[entry + TypeOffset] != StreamSound)
                continue;
            streams++;

            int nameIndex = Offset(data, entry);
            if (nameIndex >= nameCount)
                continue;
            int start = symbols + Offset(data, names + 4 + nameIndex * 4);
            int end = Array.IndexOf(data, (byte)0, start);
            string name = Encoding.ASCII.GetString(data, start, end - start);

            // STRM_N_ and STRM_R_ are the race and battle music. STRM_O_ is fanfares, which keep their balance.
            if ((name.StartsWith("STRM_N_", StringComparison.Ordinal) || name.StartsWith("STRM_R_", StringComparison.Ordinal))
                && data[entry + VolumeOffset] < FullVolume)
            {
                data[entry + VolumeOffset] = FullVolume;
                changed++;
            }
        }
        if (streams == 0)
            throw new InvalidDataException("The sound archive has no music, so it wasn't changed.");
        return changed;
    }

    private static int Offset(byte[] d, int at)
    {
        if (at < 0 || at + 4 > d.Length)
            throw new InvalidDataException("The sound archive is damaged, so it wasn't changed.");
        return (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];
    }

    /// <summary>Returns the patched copy, making it the first time. Throws with a clear message if the game can't be read.</summary>
    public static string Prepare(AppSettings settings, Action<string> status)
    {
        if (File.Exists(CachePath))
            return CachePath;

        string game = FindGame(settings) ?? throw new DownloadFailedException("Couldn't find your Mario Kart Wii file. Pick it in Settings.");
        string tool = FindDolphinTool() ?? throw new DownloadFailedException("Couldn't find DolphinTool, which comes with Dolphin.");

        status("Raising the music volume, first time only...");
        string temp = Path.Combine(Path.GetTempPath(), "rrmusic_sound_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            Extract(tool, game, temp);
            string extracted = Directory.GetFiles(temp, FileName, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new DownloadFailedException("Your game file has no sound archive.");

            byte[] data = File.ReadAllBytes(extracted);
            int changed = BoostMusic(data);
            status($"Raised {changed} music slots to full volume.");

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            string partial = CachePath + ".tmp";
            File.WriteAllBytes(partial, data);
            File.Move(partial, CachePath, true);
            return CachePath;
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    /// <summary>Forgets the patched copy, for when the game file changes.</summary>
    public static void ClearCache()
    {
        if (File.Exists(CachePath))
            File.Delete(CachePath);
    }

    private static void Extract(string tool, string game, string folder)
    {
        var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "extract", "-i", game, "-o", folder, "-p", "DATA", "-s", "sound/" + FileName, "-q" })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new DownloadFailedException("Couldn't start DolphinTool.");
        var errors = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new DownloadFailedException($"DolphinTool couldn't read your game file. {errors.Result.Trim()}");
    }

    /// <summary>Copies the archive to a folder unless it's already there, or removes it when it's no longer wanted.</summary>
    public static void Place(string? archive, bool wanted, string folder)
    {
        string target = Path.Combine(folder, FileName);
        if (archive == null)
        {
            if (!wanted && File.Exists(target))
                File.Delete(target);
            return;
        }
        Directory.CreateDirectory(folder);
        var source = new FileInfo(archive);
        var existing = new FileInfo(target);
        if (existing.Exists && existing.Length == source.Length && existing.LastWriteTimeUtc == source.LastWriteTimeUtc)
            return;
        File.Copy(archive, target, true);
    }

    internal static string? FindGame(AppSettings settings)
    {
        if (File.Exists(settings.GameFile))
            return settings.GameFile;
        foreach (string data in WheelWizardDataFolders())
        {
            string? game = ReadPath(Path.Combine(data, "RR.json"), "base-file") ?? ReadPath(Path.Combine(data, "config.json"), "GameLocation");
            if (File.Exists(game))
                return game;
        }
        return null;
    }

    private static string? FindDolphinTool()
    {
        var folders = new List<string>();
        foreach (string data in WheelWizardDataFolders())
            if (ReadPath(Path.Combine(data, "config.json"), "DolphinLocation") is { } dolphin && Path.GetDirectoryName(dolphin) is { } folder)
                folders.Add(folder);
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dolphin-x64"));
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dolphin-x64"));
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dolphin"));
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(dir => dir.Trim('"')));
        foreach (string folder in folders)
        {
            string path = Path.Combine(folder, "DolphinTool.exe");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static IEnumerable<string> WheelWizardDataFolders() =>
        PackBuilder.ModsFolders().Select(Path.GetDirectoryName).OfType<string>().Where(Directory.Exists);

    private static string? ReadPath(string jsonFile, string key)
    {
        try
        {
            if (!File.Exists(jsonFile))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllText(jsonFile));
            return document.RootElement.TryGetProperty(key, out var value) ? value.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
