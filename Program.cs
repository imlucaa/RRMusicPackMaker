using System.Runtime.InteropServices;

namespace RRMusicPackMaker;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    private static int Main(string[] args)
    {
        // Load the encoder now. A single-file app reads libraries from its exe on first use,
        // so if the exe is replaced while the app is open, a late load fails partway through a build.
        GC.KeepAlive(typeof(VGAudio.Containers.NintendoWare.BrstmWriter).Assembly);

        if (args.Length > 0)
        {
            AttachConsole(-1);
            try
            {
                return RunCommand(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static void Pump(int milliseconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void PumpUntil(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < 120000)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);

    /// <summary>Takes the picture through Windows itself, so native dark controls appear as they really do.</summary>
    private static void Capture(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            PrintWindow(form.Handle, hdc, 2);
            graphics.ReleaseHdc(hdc);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int RunCommand(string[] args)
    {
        switch (args[0])
        {
            case "list":
            {
                var rows = Catalog.Build(Option(args, "--rr") ?? Catalog.DetectRetroRewind(), out string message);
                Console.WriteLine(message);
                foreach (var row in rows)
                    Console.WriteLine($"{row.Category}\t{row.Name}\t{row.FileBase}\t{row.Note}");
                return 0;
            }
            case "build":
            {
                // Lets a test replace the exe before the encoder runs.
                if (Option(args, "--wait-for") is { } signal)
                    while (!File.Exists(signal))
                        Thread.Sleep(50);
                var project = Project.Load(args[1]);
                string rrRoot = project.Options.RetroRewindFolder.Length > 0
                    ? project.Options.RetroRewindFolder
                    : Catalog.DetectRetroRewind() ?? "";
                var rows = Catalog.Build(rrRoot, out string message);
                Console.WriteLine(message);
                foreach (string missing in project.ApplyTo(rows))
                    Console.WriteLine($"Warning: no track uses the file name {missing}.");
                string output = Option(args, "--out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "output");
                Directory.CreateDirectory(output);
                var result = PackBuilder.Build(project.Options, rows, output, Console.WriteLine);
                Console.WriteLine($"Built {result.Built}, failed {result.Failed}, unchanged {result.Unchanged}, encoded {result.Encoded}, {result.Bytes / 1048576.0:0.0} MB. {result.MusicFolder}");

                string? mods = Option(args, "--install-mod");
                if (mods != null)
                    Console.WriteLine($"Installed: {PackBuilder.InstallAsMod(result.MusicFolder, project.Options.PackName, project.Options.Author, mods)}");
                return result.Failed == 0 ? 0 : 1;
            }
            case "info":
            {
                foreach (string file in args.Skip(1))
                {
                    var info = AudioIO.ReadBrstmSummary(file);
                    Console.WriteLine($"{Path.GetFileName(file)}: loop={info.Loop} start={info.Start} end={info.End} rate={info.Rate} channels={info.Channels} samples={info.Samples}");
                }
                return 0;
            }
            case "get-ffmpeg":
            {
                if (FfmpegInstaller.IsInstalled && !args.Contains("--force"))
                {
                    Console.WriteLine($"Already installed: {Tools.Find("ffmpeg")}");
                    return 0;
                }
                double shown = 0;
                string folder = FfmpegInstaller.InstallAsync(Console.WriteLine, fraction =>
                {
                    if (fraction >= shown + 0.25)
                    {
                        shown = fraction;
                        Console.WriteLine($"{fraction:P0}");
                    }
                }, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"Installed in {folder}");
                return 0;
            }
            case "sound-archive":
                Console.WriteLine(SoundArchive.Prepare(AppStorage.LoadSettings(), Console.WriteLine));
                return 0;
            case "get-tool":
                Console.WriteLine(Downloader.EnsureToolAsync(Console.WriteLine, CancellationToken.None).GetAwaiter().GetResult());
                return 0;
            case "search":
                foreach (var result in Downloader.SearchAsync(args[1], Console.WriteLine, CancellationToken.None).GetAwaiter().GetResult())
                    Console.WriteLine($"{(result.Official ? "OFFICIAL" : "other")} | {result.Link} | {result.Display} | {result.Album} | {result.Plays}");
                return 0;
            case "download":
            {
                string folder = Option(args, "--out") ?? Environment.CurrentDirectory;
                double shown = 0;
                var song = Downloader.DownloadAsync(args[1], folder, Console.WriteLine, fraction =>
                {
                    if (fraction >= shown + 0.25 || fraction >= 1)
                    {
                        shown = fraction;
                        Console.WriteLine($"{fraction:P0}");
                    }
                }, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"Path: {song.Path}");
                Console.WriteLine($"Title: {song.Title}");
                Console.WriteLine($"Artist: {song.Artist}");
                return 0;
            }
            case "modsfolder":
                foreach (string mods in PackBuilder.ModsFolders())
                    Console.WriteLine(mods);
                return 0;
            case "match":
            {
                var rows = Catalog.Build(Option(args, "--rr") ?? Catalog.DetectRetroRewind(), out _);
                var (matched, unmatched) = PackBuilder.AutoMatch(args.Skip(1), rows);
                foreach (var row in rows.Where(r => r.IsAssigned))
                    Console.WriteLine($"{Path.GetFileName(row.Source)} => {row.Name} [{row.FileBase}]");
                foreach (string name in unmatched)
                    Console.WriteLine($"{name} => no single match");
                Console.WriteLine($"matched={matched} unmatched={unmatched.Count}");
                return 0;
            }
            case "findloops":
            {
                const int rate = 32000;
                foreach (var found in LoopFinder.FindCandidates(AudioIO.Decode(args[1], rate), rate))
                    Console.WriteLine($"{AudioIO.FormatTime((double)found.Start / rate)} to {AudioIO.FormatTime((double)found.End / rate)}, {(found.End - found.Start) / (double)rate:0.0} s long, match {found.Score:0.000}");
                return 0;
            }
            case "findloop":
            {
                int rate = 32000;
                var channels = AudioIO.Decode(args[1], rate);
                var found = LoopFinder.Find(channels, rate);
                Console.WriteLine(found is { } f
                    ? $"start={f.Start} end={f.End} rate={rate} lengthSeconds={(f.End - f.Start) / (double)rate:0.000} score={f.Score:0.000}"
                    : "none");
                return 0;
            }
            case "screenshot":
            {
                ApplicationConfiguration.Initialize();
                string? project = Option(args, "--project");
                string? theme = Option(args, "--theme");
                using var form = new MainForm();
                if (theme != null && (theme == "dark") != Theme.Current.IsDark)
                    form.ToggleTheme();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-5000, -5000);
                form.Show();
                string? category = Option(args, "--category");
                if (project != null)
                    form.OpenProjectFile(project, showAssignedOnly: category == null);
                if (category != null)
                    form.SelectCategory(category);
                Pump(600);
                Capture(form, args[1]);
                form.Close();
                return 0;
            }
            case "screenshot-download":
            {
                ApplicationConfiguration.Initialize();
                Theme.Set(Option(args, "--theme") == "dark");
                using var download = new DownloadForm(Path.GetTempPath(), 1);
                download.StartPosition = FormStartPosition.Manual;
                download.Location = new Point(-5000, -5000);
                download.Show();
                download.SetQueryForTest(args[2]);
                var search = download.GoAsync();
                PumpUntil(() => search.IsCompleted);
                Pump(300);
                Capture(download, args[1]);
                download.Close();
                return 0;
            }
            case "setup-test":
            {
                // Runs first-run setup with a real install, without anyone clicking.
                ApplicationConfiguration.Initialize();
                Theme.Set(Option(args, "--theme") != "light");
                using var setup = new SetupForm(new Options { RetroRewindFolder = Catalog.DetectRetroRewind() ?? "" }, new AppSettings());
                setup.StartPosition = FormStartPosition.Manual;
                setup.Location = new Point(-5000, -5000);
                setup.Show();
                Pump(300);
                Capture(setup, args[1]);
                Console.WriteLine($"Before: {setup.FfmpegStatusForTest} | button {setup.InstallVisibleForTest} | {setup.StartTextForTest}");
                if (args.Contains("--install"))
                {
                    var install = setup.InstallAsync();
                    PumpUntil(() => setup.FfmpegStatusForTest.Contains('%'));
                    Capture(setup, args[2]);
                    Console.WriteLine($"During: {setup.FfmpegStatusForTest}");
                    PumpUntil(() => install.IsCompleted);
                    Pump(300);
                    Capture(setup, args[3]);
                    Console.WriteLine($"After: {setup.FfmpegStatusForTest} | button {setup.InstallVisibleForTest} | {setup.StartTextForTest}");
                }
                setup.Close();
                return 0;
            }
            case "screenshot-settings":
            {
                ApplicationConfiguration.Initialize();
                Theme.Set(Option(args, "--theme") == "dark");
                using var settings = new SettingsForm(new Options(), new AppSettings());
                settings.StartPosition = FormStartPosition.Manual;
                settings.Location = new Point(-5000, -5000);
                settings.Show();
                Pump(300);
                Capture(settings, args[1]);
                settings.Close();
                return 0;
            }
            case "screenshot-editor":
            {
                ApplicationConfiguration.Initialize();
                Theme.Set(Option(args, "--theme") == "dark");
                using var editor = new LoopEditorForm(new TrackRow { Name = "Title screen / main menu", FileBase = "title_bg", Source = args[2] });
                editor.StartPosition = FormStartPosition.Manual;
                editor.Location = new Point(-5000, -5000);
                editor.Show();
                PumpUntil(() => editor.MarkerEnd > 1);
                var search = editor.FindLoopsForTest();
                PumpUntil(() => search.IsCompleted);
                Pump(300);
                Capture(editor, args[1]);
                editor.Close();
                return 0;
            }
            default:
                Console.Error.WriteLine("Commands: list, build <project.json> [--out DIR] [--install-mod DIR], info <file.brstm>..., findloop <file>");
                return 2;
        }
    }
}
