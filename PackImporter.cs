using System.IO.Compression;
using System.Text;

namespace RRMusicPackMaker;

/// <summary>A pack the app found on this PC, ready to import.</summary>
public sealed record InstalledPack(string Name, string Folder, int Songs, bool MadeByApp = false, bool IsMod = false)
{
    public override string ToString() => $"{Name}   ·   {Songs} {(Songs == 1 ? "song" : "songs")}";
}

/// <summary>Which file goes on which track, worked out before anything is copied.</summary>
public sealed class PackImportPlan
{
    public List<(TrackRow Row, string File, bool IsFinal)> Matches { get; } = [];
    public List<string> Unmatched { get; } = [];

    /// <summary>Songs named after no track. They still come in, so they can be heard and put on a track by hand.</summary>
    public List<string> Loose { get; } = [];

    /// <summary>Files that only matched once a typo in their name was forgiven, with the name they should have had.</summary>
    public List<(string File, string Fixed)> Renamed { get; } = [];
    public Dictionary<string, (string Song, string Credits)> Credits { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Song names from a text file in the pack, for packs that list what's on each track.</summary>
    public Dictionary<TrackRow, string> TrackNotes { get; } = [];

    /// <summary>Tracks that would lose the song they have now.</summary>
    public int Conflicts => Matches.Where(match => !match.IsFinal).Count(match => match.Row.IsAssigned);
    public int Tracks => Matches.Count(match => !match.IsFinal);
}

/// <summary>Turns an existing music pack back into songs on tracks, so it can be changed and rebuilt.</summary>
public static class PackImporter
{
    private const string CreditsFile = "credits.csv";

    private const string ImportedFolderName = "Imported packs";

    /// <summary>True for songs copied in by an earlier import, which the app is free to delete or reuse.</summary>
    internal static bool IsImportedCopy(string path, string outputFolder)
    {
        string root = Path.GetFullPath(Path.Combine(outputFolder, ImportedFolderName)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Packs on this PC: the Patches folder, each Wheel Wizard mod with music in it, then what this app imported or made.</summary>
    public static List<InstalledPack> FindInstalled(string retroRewindFolder, IEnumerable<string> modsFolders, string? outputFolder = null)
    {
        var packs = new List<InstalledPack>();
        void Add(string name, string folder, bool madeByApp = false, bool isMod = false)
        {
            int songs = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.brstm", SearchOption.AllDirectories).Length : 0;
            if (songs > 0)
                packs.Add(new InstalledPack(name, folder, songs, madeByApp, isMod));
        }

        if (retroRewindFolder.Length > 0)
            Add("Patches folder (what's in the game now)", Path.Combine(retroRewindFolder, "Patches"));
        foreach (string mods in modsFolders.Where(Directory.Exists))
            foreach (string mod in Directory.GetDirectories(mods))
                Add($"Wheel Wizard mod: {Path.GetFileName(mod)}", mod, isMod: true);
        if (outputFolder != null && Directory.Exists(outputFolder))
        {
            string imported = Path.Combine(outputFolder, ImportedFolderName);
            if (Directory.Exists(imported))
                foreach (string pack in Directory.GetDirectories(imported))
                    Add($"Imported: {Path.GetFileName(pack)}", pack, madeByApp: true);
            foreach (string pack in Directory.GetDirectories(outputFolder))
                if (Path.GetFileName(pack) is not (ImportedFolderName or "Downloaded songs"))
                    Add($"Made in this app: {Path.GetFileName(pack)}", pack, madeByApp: true);
        }
        return packs;
    }

    /// <summary>
    /// What deleting a pack would remove. A folder the app made goes completely, and so does a mod with nothing but
    /// music in it. The Patches folder and mods with other files, like karts, only lose their songs.
    /// </summary>
    public static (bool WholeFolder, string[] Songs, int OtherFiles) DeletePlan(InstalledPack pack)
    {
        var files = Directory.GetFiles(pack.Folder, "*", SearchOption.AllDirectories);
        if (pack.MadeByApp)
            return (true, files.Where(PackBuilder.IsAudioFile).ToArray(), 0);

        static bool HasExtension(string file, string extension) => Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase);
        var songs = files.Where(file => HasExtension(file, ".brstm")).ToArray();

        // A mod's settings file and its copy of revo_kart.brsar only exist for its music.
        int others = files.Count(file => !HasExtension(file, ".brstm") && !HasExtension(file, ".ini") && !HasExtension(file, ".brsar"));
        return (pack.IsMod && others == 0, songs, others);
    }

    /// <summary>A zip only has its music and credits taken out, flattened by name, so it can't write anywhere unexpected.</summary>
    public static string ExtractZip(string zip)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"rrimport_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        using var archive = ZipFile.OpenRead(zip);
        int index = 0;
        foreach (var entry in archive.Entries)
        {
            string name = Path.GetFileName(entry.FullName);
            bool wanted = name.EndsWith(".brstm", StringComparison.OrdinalIgnoreCase) || name.Equals(CreditsFile, StringComparison.OrdinalIgnoreCase);
            if (!wanted)
                continue;
            string holder = Path.Combine(folder, (index++).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(holder);
            entry.ExtractToFile(Path.Combine(holder, name));
        }
        return folder;
    }

    /// <summary>Plans a whole pack folder, along with any credits it came with.</summary>
    public static PackImportPlan Plan(string folder, IReadOnlyList<TrackRow> rows)
    {
        var plan = PlanFiles(Directory.GetFiles(folder, "*.brstm", SearchOption.AllDirectories), rows);
        foreach (string credits in Directory.GetFiles(folder, CreditsFile, SearchOption.AllDirectories).Take(1))
            ReadCredits(credits, plan.Credits);
        foreach (string notes in Directory.GetFiles(folder, "*.txt", SearchOption.AllDirectories))
            ReadTrackNotes(notes, plan);
        return plan;
    }

    /// <summary>Reads lines like "Luigi Circuit - Living in the City". Lines that name no track in this pack are ignored.</summary>
    private static void ReadTrackNotes(string path, PackImportPlan plan)
    {
        var rowsByName = new Dictionary<string, TrackRow>();
        foreach (var (row, _, isFinal) in plan.Matches)
        {
            if (isFinal)
                continue;
            foreach (string name in row.Name.Replace(" (shared file)", "").Split(" / "))
                rowsByName.TryAdd(TrackKey(name), row);
        }
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                int dash = line.IndexOf(" - ", StringComparison.Ordinal);
                if (dash <= 0)
                    continue;
                string song = line[(dash + 3)..].Trim();
                if (song.Length > 0 && rowsByName.TryGetValue(TrackKey(line[..dash]), out var row))
                    plan.TrackNotes.TryAdd(row, song);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // These are only names, so the songs can still come in without them.
        }
    }

    // Packs write "Wii Bowser's Castle" where the stock track is just "Bowser's Castle".
    private static string TrackKey(string name)
    {
        string trimmed = name.Trim();
        return Catalog.NameKey(trimmed.StartsWith("Wii ", StringComparison.OrdinalIgnoreCase) ? trimmed[4..] : trimmed);
    }

    /// <summary>Matches music files to tracks by file name, the same way the game finds them.</summary>
    public static PackImportPlan PlanFiles(IEnumerable<string> musicFiles, IReadOnlyList<TrackRow> rows)
    {
        var normal = new Dictionary<string, TrackRow>(StringComparer.OrdinalIgnoreCase);
        var finals = new Dictionary<string, TrackRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (string name in new[] { row.FileBase }.Concat(row.AliasBases))
                normal.TryAdd(name, row);
            if (row.PulsarFinalBase != null)
                finals.TryAdd(row.PulsarFinalBase, row);
            if (row.StockFinalBase != null && row.StockFinalBase != row.FileBase)
                finals.TryAdd(row.StockFinalBase, row);
        }

        var plan = new PackImportPlan();
        // Files named exactly right go first, so a misspelled copy of the same song can never take its place.
        bool Exact(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            return normal.ContainsKey(name) || finals.ContainsKey(name);
        }
        var files = musicFiles.OrderByDescending(Exact).ThenBy(file => file, StringComparer.OrdinalIgnoreCase).ToList();
        var placed = new HashSet<TrackRow>();
        var matchedFinals = new List<(TrackRow Row, string File)>();
        foreach (string file in files)
        {
            string written = Path.GetFileNameWithoutExtension(file);
            string name = written;
            bool misspelled = false;

            // Shared packs sometimes add a stray space, or spell the game's o_ names with a zero.
            // The game wouldn't load those, but the track they're for is clear.
            if (!normal.ContainsKey(name) && !finals.ContainsKey(name))
            {
                string spelled = name.Trim();
                if (spelled.StartsWith("0_", StringComparison.Ordinal))
                    spelled = "o" + spelled[1..];
                if (normal.ContainsKey(spelled) || finals.ContainsKey(spelled))
                {
                    name = spelled;
                    misspelled = true;
                }
            }

            if (normal.TryGetValue(name, out var row))
            {
                if (placed.Add(row))
                {
                    plan.Matches.Add((row, file, false));
                    if (misspelled)
                        plan.Renamed.Add((file, name));
                }
                else if (misspelled)
                {
                    // The right file is already on the track, and this one may be a different song, so keep it in the list.
                    plan.Unmatched.Add(written);
                    plan.Loose.Add(file);
                }

                // Otherwise it's one of the track's other names, which hold the same audio, so one copy is enough.
            }
            else if (finals.TryGetValue(name, out var final))
            {
                matchedFinals.Add((final, file));
            }
            else
            {
                plan.Unmatched.Add(name);
                plan.Loose.Add(file);
            }
        }

        // A pack's final lap file may be its own version of the song, so it's kept rather than made again.
        foreach (var (row, file) in matchedFinals)
        {
            if (placed.Contains(row))
                plan.Matches.Add((row, file, true));
            else
            {
                plan.Unmatched.Add(Path.GetFileNameWithoutExtension(file));
                plan.Loose.Add(file);
            }
        }
        return plan;
    }

    /// <summary>Copies the songs somewhere of their own, because the source may be a folder that installing a pack replaces.</summary>
    public static Dictionary<string, string> CopyFiles(PackImportPlan plan, string destination)
    {
        Directory.CreateDirectory(destination);
        var copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in plan.Matches.Select(match => match.File).Concat(plan.Loose))
        {
            string copy = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, copy, true);
            copies[file] = copy;
        }
        return copies;
    }

    /// <summary>A new folder for this import, so importing twice never mixes two packs.</summary>
    public static string NewFolder(string outputFolder, string name)
    {
        string root = Path.Combine(outputFolder, ImportedFolderName);
        string safe = PackBuilder.SafeName(name);
        string folder = Path.Combine(root, safe);
        for (int i = 2; Directory.Exists(folder); i++)
            folder = Path.Combine(root, $"{safe} ({i})");
        return folder;
    }

    /// <summary>Puts the songs on their tracks, with settings that keep them as they are in the pack. Returns how many tracks changed.</summary>
    public static int Assign(PackImportPlan plan, Dictionary<string, string> copies, bool replaceAssigned)
    {
        var skipped = new HashSet<TrackRow>();
        int assigned = 0;
        foreach (var (row, file, isFinal) in plan.Matches.OrderBy(match => match.IsFinal))
        {
            if (isFinal)
            {
                if (!skipped.Contains(row))
                    row.FinalSource = copies[file];
                continue;
            }
            if (row.IsAssigned && !replaceAssigned)
            {
                skipped.Add(row);
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(file);
            row.Source = copies[file];
            row.Status = "";

            // Auto uses the loop stored in the file, so the pack's own loop points carry over.
            row.Loop = LoopMode.Auto;
            row.LoopStart = "";
            row.LoopEnd = "";
            row.Crossfade = 0;
            row.Speed = 1;
            row.KeepPitch = false;
            row.Reverb = false;
            row.VolumeDb = 0;
            var credits = plan.Credits.GetValueOrDefault(name);
            row.Song = credits.Song is { Length: > 0 } ? credits.Song : plan.TrackNotes.GetValueOrDefault(row, name);
            row.KeepFile = Path.GetExtension(file).Equals(".brstm", StringComparison.OrdinalIgnoreCase);
            if (credits.Credits is { Length: > 0 })
                row.Credits = credits.Credits;
            assigned++;
        }
        return assigned;
    }

    /// <summary>Reads what the app writes in an exported pack: Track, Song, Credits, File Name. Other packs may not have one.</summary>
    private static void ReadCredits(string path, Dictionary<string, (string Song, string Credits)> credits)
    {
        try
        {
            var lines = File.ReadAllLines(path).Select(SplitCsv).ToList();
            if (lines.Count == 0)
                return;
            int song = lines[0].FindIndex(cell => cell.Equals("Song", StringComparison.OrdinalIgnoreCase));
            int credit = lines[0].FindIndex(cell => cell.Equals("Credits", StringComparison.OrdinalIgnoreCase));
            int file = lines[0].FindIndex(cell => cell.Equals("File Name", StringComparison.OrdinalIgnoreCase));
            if (song < 0 || credit < 0 || file < 0)
                return;
            foreach (var line in lines.Skip(1).Where(line => line.Count > Math.Max(song, Math.Max(credit, file))))
                credits[line[file]] = (line[song], line[credit]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Credits are only names, so the songs can still come in without them.
        }
    }

    internal static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                cell.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }
        cells.Add(cell.ToString());
        return cells;
    }
}

/// <summary>Asks which pack to bring in: one already in the game, or any folder or zip.</summary>
public sealed class ImportPackForm : Form
{
    private readonly SideList _installed = new() { Dock = DockStyle.Fill, Tag = Theme.PanelTag };
    private readonly ModernButton _import = new() { Text = "Import this pack", Tag = Theme.AccentTag, AutoSize = true, Height = 28 };
    private readonly ModernButton _folder = new() { Text = "Choose folder", AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 0) };
    private readonly ModernButton _zip = new() { Text = "Choose zip", AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 0) };
    private readonly ModernButton _files = new() { Text = "Choose files", AutoSize = true, Height = 28, Margin = new Padding(0) };
    private readonly ModernButton _delete = new() { Text = "Delete", AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 0) };
    private readonly Func<InstalledPack, Task<bool>>? _deletePack;
    private readonly bool _deleteOnly;
    private readonly Label _currentTitle;
    private readonly ModernButton _cancel = new() { Text = "Cancel", AutoSize = true, Height = 28 };
    private readonly List<InstalledPack> _packs;

    /// <summary>The folder or zip that was picked.</summary>
    public string? Chosen { get; private set; }

    /// <summary>Single songs that were picked instead of a pack.</summary>
    public string[]? ChosenFiles { get; private set; }

    public ImportPackForm(List<InstalledPack> packs, Func<InstalledPack, bool>? deletePack = null)
        : this(packs, deletePack == null ? null : pack => Task.FromResult(deletePack(pack)))
    {
    }

    /// <summary>With deleteOnly, the window is for deleting packs and has no way to import one.</summary>
    public ImportPackForm(List<InstalledPack> packs, Func<InstalledPack, Task<bool>>? deletePack, bool deleteOnly = false)
    {
        _packs = packs;
        _deletePack = deletePack;
        _deleteOnly = deleteOnly;
        Text = deleteOnly ? "Delete a music pack" : "Import a music pack";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.UiFont();

        var page = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 20, 22, 18) };
        page.Controls.Add(new Label { Text = deleteOnly ? "Which pack do you want to delete?" : "Which pack do you want to bring in?", AutoSize = true, Font = Theme.TitleFont(14f), Margin = new Padding(0, 0, 0, 4) });
        page.Controls.Add(new Label
        {
            Text = deleteOnly ? "Its songs go to the Recycle Bin. Karts and other files in it stay." : "Its songs go back on their tracks, ready to change and rebuild.",
            AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 14),
        });

        var currentCard = new CardPanel { Width = 600, Height = 236, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 10) };
        _currentTitle = new Label { Text = packs.Count > 0 ? "On this PC" : "No packs found on this PC", AutoSize = true, Dock = DockStyle.Top, Font = Theme.TitleFont(10f), Padding = new Padding(6, 2, 0, 8) };
        var currentBottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        currentBottom.Controls.Add(_import);
        currentBottom.Controls.Add(_delete);
        foreach (var pack in packs)
            _installed.Items.Add(new SideList.Item(pack.ToString(), -1));
        if (packs.Count > 0)
            _installed.SelectedIndex = 0;
        currentCard.Controls.Add(_installed);
        currentCard.Controls.Add(_currentTitle);
        currentCard.Controls.Add(currentBottom);
        page.Controls.Add(currentCard);

        var otherCard = new CardPanel { Width = 600, Height = 72, Padding = new Padding(16, 12, 16, 8), Margin = new Padding(0, 0, 0, 10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        text.Controls.Add(new Label { Text = "Another pack", AutoSize = true, Font = Theme.TitleFont(10f), Margin = new Padding(0) });
        text.Controls.Add(new Label { Text = "A folder, a zip or single songs.", AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 1, 0, 0) });
        grid.Controls.Add(text, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(10, 0, 0, 0) };
        buttons.Controls.AddRange([_folder, _zip, _files]);
        grid.Controls.Add(buttons, 1, 0);
        otherCard.Controls.Add(grid);
        if (!deleteOnly)
            page.Controls.Add(otherCard);

        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 6, 0, 0) };
        footer.Controls.Add(_cancel);
        page.Controls.Add(footer);
        Controls.Add(page);

        _installed.SelectedIndexChanged += (_, _) => ShowButtons();
        ShowButtons();
        _delete.Click += async (_, _) => await DeleteSelected();
        _files.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Pick the songs",
                Multiselect = true,
                Filter = "Audio files|*.brstm;*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.wma;*.webm;*.mp4|All files|*.*",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                ChosenFiles = dialog.FileNames;
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        _import.Click += (_, _) => FinishWithSelected();
        _installed.DoubleClick += (_, _) => FinishWithSelected();
        _folder.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Pick the pack's folder", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                Finish(dialog.SelectedPath);
        };
        _zip.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Pick the pack's zip", Filter = "Zip files|*.zip|All files|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                Finish(dialog.FileName);
        };
        _cancel.Click += (_, _) => Close();
        CancelButton = _cancel;
        Theme.Apply(this);
    }

    private void ShowButtons()
    {
        int index = _installed.SelectedIndex;
        bool any = index >= 0 && index < _packs.Count;
        _import.Enabled = any;
        _import.Visible = !_deleteOnly;

        _delete.Visible = any && _deletePack != null;
    }

    private async Task DeleteSelected()
    {
        int index = _installed.SelectedIndex;
        if (_deletePack == null || index < 0 || index >= _packs.Count)
            return;

        // Deleting a big pack takes a while, so the window shows it's working and can't be used twice.
        var title = _currentTitle.Text;
        _currentTitle.Text = "Deleting... this can take a while for a big pack.";
        foreach (var control in new Control[] { _delete, _import, _installed, _folder, _zip, _files, _cancel })
            control.Enabled = false;
        bool deleted;
        try
        {
            deleted = await _deletePack(_packs[index]);
        }
        finally
        {
            foreach (var control in new Control[] { _delete, _installed, _folder, _zip, _files, _cancel })
                control.Enabled = true;
            _currentTitle.Text = title;
        }
        if (!deleted)
        {
            ShowButtons();
            return;
        }
        _packs.RemoveAt(index);
        _installed.Items.RemoveAt(index);
        _installed.SelectedIndex = _packs.Count > 0 ? Math.Min(index, _packs.Count - 1) : -1;
        if (_packs.Count == 0)
            _currentTitle.Text = "No packs found on this PC";
        ShowButtons();
    }

    // For the automated tests.
    internal bool DeleteVisibleForTest => _delete.Visible;
    internal bool ImportEnabledForTest => _import.Enabled;
    internal int PackCountForTest => _installed.Items.Count;
    internal void SelectPackForTest(int index) => _installed.SelectedIndex = index;
    internal void DeleteForTest() => DeleteSelected().GetAwaiter().GetResult();
    internal bool ImportVisibleForTest => _import.Visible;

    private void FinishWithSelected()
    {
        if (_installed.SelectedIndex >= 0 && _installed.SelectedIndex < _packs.Count)
            Finish(_packs[_installed.SelectedIndex].Folder);
    }

    private void Finish(string path)
    {
        Chosen = path;
        DialogResult = DialogResult.OK;
        Close();
    }
}
