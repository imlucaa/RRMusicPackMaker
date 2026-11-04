using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRMusicPackMaker;

/// <summary>
/// The app's core without a window, for the GPUI front end. Each command reads the same saved project and settings
/// as the classic window, and answers on standard output: one JSON document, or for long work, one line per event
/// ("log", "progress", then "done" or "error"), each a JSON object.
/// </summary>
internal static class Engine
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string OutputFolder(AppSettings settings) =>
        settings.OutputFolder.Length > 0 ? settings.OutputFolder : AppStorage.DefaultOutputFolder;

    private static string DownloadFolder(AppSettings settings) => Path.Combine(OutputFolder(settings), "Downloaded songs");

    private static Project LoadProject()
    {
        try
        {
            if (File.Exists(AppStorage.AutosavePath))
                return Project.Load(AppStorage.AutosavePath);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            // A damaged autosave starts a fresh pack rather than stopping the app.
        }
        return new Project();
    }

    private static string? RetroRewindFolder(Project project) =>
        project.Options.RetroRewindFolder is { Length: > 0 } folder ? folder : RRMusicPackMaker.Catalog.DetectRetroRewind();

    private static void Emit(object value)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value, Json));
        Console.Out.Flush();
    }

    public static int Run(string[] args)
    {
        string command = args.Length > 1 ? args[1] : "";
        switch (command)
        {
            case "catalog":
                return CatalogJson();
            case "songs":
                return Songs();
            case "export":
                return Export(args);
            case "remove":
                return Remove();
            case "preview":
                return Preview(args);
            default:
                Console.Error.WriteLine("Usage: engine catalog | songs | export --mode zip|folder|game [--target PATH] [--wii|--dolphin] | remove");
                return 2;
        }
    }

    private static int CatalogJson()
    {
        var project = LoadProject();
        var rows = RRMusicPackMaker.Catalog.Build(RetroRewindFolder(project), out string message);
        Emit(new
        {
            message,
            groups = RRMusicPackMaker.Catalog.Groups
                .Select(group => new
                {
                    title = group.Title,
                    categories = group.Categories.Where(c => !RRMusicPackMaker.Catalog.HiddenFromSidebar.Contains(c)).ToArray(),
                })
                .ToArray(),
            rows = rows.Select(row => new
            {
                file = row.FileBase,
                category = row.Category,
                platform = row.Platform,
                name = row.Name,
                variantOf = row.VariantOf,
                note = row.Note,
                canHaveFinal = row.StockFinalBase != null || row.PulsarFinalBase != null,
                race = row.IsRaceMusic,
            }),
        });
        return 0;
    }

    private static int Songs()
    {
        var settings = AppStorage.LoadSettings();
        var library = new SongLibrary(() => DownloadFolder(settings));
        Emit(library.Songs.Select(song => new
        {
            path = song,
            label = library.LabelOf(song),
            kind = library.KindOf(song).ToString(),
            fromPack = library.IsFromImportedPack(song),
        }));
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Export(string[] args)
    {
        string mode = Option(args, "--mode") ?? "";
        string? target = Option(args, "--target");
        if (mode is not ("zip" or "folder" or "game") || (mode != "game" && string.IsNullOrWhiteSpace(target)))
        {
            Emit(new { @event = "error", message = "Choose zip, folder or game, and where it goes." });
            return 2;
        }

        var settings = AppStorage.LoadSettings();
        var project = LoadProject();
        if (args.Contains("--wii"))
            project.Options.TargetWii = true;
        else if (args.Contains("--dolphin"))
            project.Options.TargetWii = false;

        if (!FfmpegInstaller.IsInstalled)
        {
            Emit(new { @event = "error", message = "Making a pack needs FFmpeg. Install it from the classic app's Settings, then try again." });
            return 1;
        }

        string? rrRoot = RetroRewindFolder(project);
        var rows = RRMusicPackMaker.Catalog.Build(rrRoot, out _);
        project.ApplyTo(rows);
        if (rows.All(row => !row.IsAssigned))
        {
            Emit(new { @event = "error", message = "Add a song to at least one track first." });
            return 1;
        }

        string output = OutputFolder(settings);
        Directory.CreateDirectory(output);
        try
        {
            var result = PackBuilder.Build(project.Options, rows, output,
                message => Emit(new { @event = "log", message }),
                (done, total) => Emit(new { @event = "progress", done, total }));
            if (result.Built == 0)
            {
                Emit(new { @event = "error", message = $"No songs were made. {result.Failed} failed, see the log." });
                return 1;
            }

            string where;
            switch (mode)
            {
                case "zip":
                    PackBuilder.ExportZip(result.Folder, target!);
                    where = target!;
                    break;
                case "folder":
                    where = PackBuilder.ExportFolder(result.Folder, target!);
                    break;
                default:
                    if (rrRoot == null || !Directory.Exists(rrRoot))
                    {
                        Emit(new { @event = "error", message = "Adding to the game needs your Retro Rewind folder. Set it in the classic app's Settings." });
                        return 1;
                    }
                    PackBuilder.CopyToStrm(result.MusicFolder, rrRoot, PackBuilder.StrmBackupFolder(output));
                    where = PackBuilder.StrmFolder(rrRoot);
                    break;
            }
            string failed = result.Failed > 0 ? $", {result.Failed} failed" : "";
            Emit(new
            {
                @event = "done",
                message = $"Made {result.Built} songs{failed}, {result.Bytes / 1048576.0:0.0} MB.",
                where,
                built = result.Built,
                failed = result.Failed,
            });
            return result.Failed == 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Emit(new { @event = "error", message = ex.Message });
            return 1;
        }
    }

    /// <summary>
    /// Writes a WAV of how a track will sound in the pack, with its volume and speed, or of a plain song file.
    /// --track FILE uses that track's song from the saved project; add --final for the final lap.
    /// --song PATH previews a song file as it is.
    /// </summary>
    private static int Preview(string[] args)
    {
        try
        {
            string wav;
            if (Option(args, "--song") is { } song)
            {
                const int rate = 32000;
                wav = AudioIO.WriteRangePreview(AudioIO.Decode(song, rate), rate, 0, 60 * 60);
            }
            else if (Option(args, "--track") is { } file)
            {
                var project = LoadProject();
                var rows = RRMusicPackMaker.Catalog.Build(RetroRewindFolder(project), out _);
                project.ApplyTo(rows);
                var row = rows.FirstOrDefault(r => r.FileBase.Equals(file, StringComparison.OrdinalIgnoreCase));
                if (row is not { IsAssigned: true })
                {
                    Emit(new { @event = "error", message = "That track has no song yet." });
                    return 1;
                }
                wav = args.Contains("--final")
                    ? PackBuilder.RenderFinalPreview(project.Options, row)
                    : PackBuilder.RenderPreview(project.Options, row);
            }
            else
            {
                Emit(new { @event = "error", message = "Give --track FILE or --song PATH." });
                return 2;
            }
            Emit(new { @event = "done", path = wav });
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            Emit(new { @event = "error", message = ex.Message });
            return 1;
        }
    }

    private static int Remove()
    {
        var settings = AppStorage.LoadSettings();
        var project = LoadProject();
        string? rrRoot = RetroRewindFolder(project);
        if (rrRoot == null || !Directory.Exists(rrRoot))
        {
            Emit(new { @event = "error", message = "Retro Rewind folder not found." });
            return 1;
        }
        string output = OutputFolder(settings);
        string name = PackBuilder.SafeName(project.Options.PackName);
        string music = Path.Combine(output, name, "RetroRewind6", "Patches");
        try
        {
            int removed = PackBuilder.RemovePack(project.Options.PackName, PackBuilder.ModsFolders(), rrRoot, music, PackBuilder.StrmBackupFolder(output));
            Emit(new { @event = "done", message = $"Took {name} out of the game. {removed} files were put back or removed." });
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Emit(new { @event = "error", message = ex.Message });
            return 1;
        }
    }
}
