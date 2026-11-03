using System.Text.Json;

namespace RRMusicPackMaker;

public sealed class AppSettings
{
    public string OutputFolder { get; set; } = "";

    /// <summary>Raises the music volumes in the player's own copy of revo_kart.brsar when installing.</summary>
    public bool BoostMusicSlots { get; set; } = true;

    /// <summary>The player's Mario Kart Wii file, if Wheel Wizard can't tell us where it is.</summary>
    public string GameFile { get; set; } = "";

    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Set once the first-run setup has been seen.</summary>
    public bool SetupDone { get; set; }

    /// <summary>Where the last pack was exported, so the next export starts there.</summary>
    public string ExportFolder { get; set; } = "";

    /// <summary>A Wheel Wizard Mods folder chosen by hand, for when it isn't found automatically.</summary>
    public string ModsFolder { get; set; } = "";

    /// <summary>Off for people who don't use Wheel Wizard, so packs only go in the Patches folder.</summary>
    public bool UseWheelWizard { get; set; } = true;

    /// <summary>How big the app is drawn, in percent. It changes the next time the app opens.</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>The accent color as #RRGGBB, or empty for the standard amber.</summary>
    public string AccentColor { get; set; } = "";

    /// <summary>The app's background color as #RRGGBB, or empty for black.</summary>
    public string BackgroundColor { get; set; } = "";

    /// <summary>The highest audio bitrate to download at, in kbps. Zero means the best a video has.</summary>
    public int DownloadKbps { get; set; }

    /// <summary>Whether the last export was for a real Wii (true) or for Dolphin (false).</summary>
    public bool ExportForWii { get; set; } = true;

    /// <summary>A picture shown behind the main window, darkened so text stays readable. Empty for plain black.</summary>
    public string BackgroundImage { get; set; } = "";
}

/// <summary>Where the app keeps its settings and the autosaved project.</summary>
public static class AppStorage
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder
    {
        get
        {
            string? custom = Environment.GetEnvironmentVariable("RRMUSIC_DATA");
            string folder = string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RRMusicPackMaker")
                : custom;
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string AutosavePath => Path.Combine(Folder, "autosave.rrmusic.json");
    private static string SettingsPath => Path.Combine(Folder, "settings.json");
    private static string PacksPath => Path.Combine(Folder, "packs.json");

    /// <summary>The packs that aren't open right now, each with its tracks, songs and settings, by name.</summary>
    public static Dictionary<string, Project> LoadPacks()
    {
        try
        {
            if (File.Exists(PacksPath))
                return new Dictionary<string, Project>(JsonSerializer.Deserialize<Dictionary<string, Project>>(File.ReadAllText(PacksPath)) ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged list only costs the packs on it. Their songs are still where they were.
        }
        return new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase);
    }

    public static void SavePacks(Dictionary<string, Project> packs)
    {
        string temp = PacksPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(packs, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, PacksPath, true);
    }

    public static string DefaultOutputFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RR Music Packs");

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A damaged settings file only costs the theme and output folder, so start fresh.
        }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings) =>
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));

    /// <summary>Writes through a temporary file, so a crash mid-save cannot damage the last good copy.</summary>
    public static void WriteAutosave(Project project)
    {
        string temp = AutosavePath + ".tmp";
        project.Save(temp);
        File.Move(temp, AutosavePath, true);
    }
}
