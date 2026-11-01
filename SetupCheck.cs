using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RRMusicPackMaker;

/// <summary>Spots settings outside the app that silence or quieten a pack, so nobody has to guess why it sounds wrong.</summary>
public static class SetupCheck
{
    // The settings file stores values by position, and that order belongs to one Retro Rewind version.
    // On any other version these positions could mean something else, so those checks are skipped.
    private const string KnownVersion = "6.13";
    private const int CustomMusicIndex = 19;
    private const int MusicIndex = 20;
    private const int LooseOverridesIndex = 58;
    private const int SectionHeaderSize = 12;
    private const int QuietDolphinVolume = 80;

    /// <summary>Everything worth warning about for this install. Empty when all is well or nothing can be read.</summary>
    public static List<string> Run(string retroRewindFolder)
    {
        string? dolphin = FindDolphinFolder(retroRewindFolder);
        if (dolphin == null)
            return [];
        byte[]? settings = ReadOrNull(Path.Combine(dolphin, "Wii", "shared2", "Pulsar", "RetroRewind6", "RRGameSettings.pul"));
        string? version = null;
        string versionFile = Path.Combine(retroRewindFolder, "version.txt");
        if (File.Exists(versionFile))
            version = ReadTextOrNull(versionFile);
        return Evaluate(settings, version, ReadDolphinVolume(Path.Combine(dolphin, "Config", "Dolphin.ini")));
    }

    internal static List<string> Evaluate(byte[]? settings, string? version, int? dolphinVolume)
    {
        var issues = new List<string>();
        if (settings != null && version?.Trim().StartsWith(KnownVersion, StringComparison.Ordinal) == true)
        {
            int values = FindValues(settings);
            if (values >= 0 && values + LooseOverridesIndex < settings.Length)
            {
                byte music = settings[values + MusicIndex];
                if (music == 1)
                    issues.Add("Retro Rewind's Music setting is off, so you won't hear any music. Turn it on in the game's settings.");
                else if (music == 2)
                    issues.Add("Retro Rewind's Music setting has race music off. Turn it on in the game's settings.");
                if (settings[values + CustomMusicIndex] == 1)
                    issues.Add("Retro Rewind's custom track music setting is off, so tracks won't play their own songs.");
                if (settings[values + LooseOverridesIndex] == 1)
                    issues.Add("Retro Rewind's Loose Archive Overrides setting is off, so the Patches folder won't load.");
            }
        }
        if (dolphinVolume < QuietDolphinVolume)
            issues.Add($"Dolphin's volume is {dolphinVolume}. Set it to 100 under Config > Audio.");
        return issues;
    }

    private static int FindValues(byte[] settings)
    {
        byte[] magic = Encoding.ASCII.GetBytes("SETT");
        int limit = Math.Min(settings.Length - magic.Length, 0x100);
        for (int i = 0; i <= limit; i++)
            if (settings.AsSpan(i, magic.Length).SequenceEqual(magic))
                return i + SectionHeaderSize;
        return -1;
    }

    private static string? FindDolphinFolder(string start)
    {
        string? folder = start;
        for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(folder); depth++)
        {
            if (File.Exists(Path.Combine(folder, "Config", "Dolphin.ini")))
                return folder;
            folder = Path.GetDirectoryName(folder);
        }
        return null;
    }

    internal static int? ReadDolphinVolume(string ini)
    {
        string? text = ReadTextOrNull(ini);
        if (text == null)
            return null;
        bool inSection = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
                inSection = line.Equals("[DSP]", StringComparison.OrdinalIgnoreCase);
            else if (inSection && line.StartsWith("Volume", StringComparison.OrdinalIgnoreCase) && line.Contains('='))
                return int.TryParse(line[(line.IndexOf('=') + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int volume) ? volume : null;
        }
        return null;
    }

    private static byte[]? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // Dolphin may be writing it. The next check will read it.
        }
    }

    private static string? ReadTextOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Tells people when a newer release is out. Never installs anything by itself.</summary>
public static class UpdateCheck
{
    public const string ReleasesPage = "https://github.com/imlucaa/RRMusicPackMaker/releases/latest";
    private const string LatestRelease = "https://api.github.com/repos/imlucaa/RRMusicPackMaker/releases/latest";

    /// <summary>The newer version's tag, or null when up to date or the check can't finish.</summary>
    public static async Task<string?> FindNewerAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RRMusicPackMaker");
            using var document = JsonDocument.Parse(await http.GetStringAsync(LatestRelease));
            string? tag = document.RootElement.GetProperty("tag_name").GetString();
            var current = typeof(UpdateCheck).Assembly.GetName().Version;
            return tag != null && current != null && IsNewer(tag, current) ? tag : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return null; // Being offline isn't worth bothering anyone about.
        }
    }

    internal static bool IsNewer(string tag, Version current)
    {
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            return false;
        var now = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        return new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0)) > now;
    }

    public static void OpenPage() => Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });
}
