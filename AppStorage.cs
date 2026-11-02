using System.Text.Json;

namespace RRMusicPackMaker;

public sealed class AppSettings
{
    /// <summary>"Light", "Dark" or "System".</summary>
    public string Theme { get; set; } = "System";
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
