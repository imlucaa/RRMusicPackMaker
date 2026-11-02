using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRMusicPackMaker;

public enum SongKind { Imported, Downloaded, Edited }

/// <summary>Every song the user has imported, downloaded or made in the Editor, newest first. It's remembered between sessions.</summary>
public sealed partial class SongLibrary
{
    private sealed class SavedList
    {
        public List<string> Songs { get; set; } = [];
        public Dictionary<string, List<EditClip>> Edits { get; set; } = [];
    }

    private readonly Func<string> _downloadsFolder;
    private readonly List<string> _added;
    private readonly Dictionary<string, List<EditClip>> _edits;

    /// <summary>Fires when a song joins or leaves the list.</summary>
    public event Action? Changed;

    public SongLibrary(Func<string> downloadsFolder)
    {
        _downloadsFolder = downloadsFolder;
        var saved = Load();
        _added = saved.Songs;
        _edits = new Dictionary<string, List<EditClip>>(saved.Edits, StringComparer.OrdinalIgnoreCase);
    }

    private static string ListPath => Path.Combine(AppStorage.Folder, "songs.json");

    /// <summary>Songs that still exist: the ones added here, then anything else in the downloads folder.</summary>
    public List<string> Songs
    {
        get
        {
            var songs = new List<string>(_added);
            try
            {
                string folder = _downloadsFolder();
                if (Directory.Exists(folder))
                    songs.AddRange(new DirectoryInfo(folder).GetFiles().Where(file => PackBuilder.IsAudioFile(file.FullName))
                        .OrderByDescending(file => file.LastWriteTimeUtc).Select(file => file.FullName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable folder only hides its songs until it can be read again.
            }
            return songs.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void Add(string path) => Add([path]);

    public void Add(IEnumerable<string> paths)
    {
        bool changed = false;
        foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path) && PackBuilder.IsAudioFile(path)))
        {
            string full = Path.GetFullPath(path);
            if (_added.Contains(full, StringComparer.OrdinalIgnoreCase))
                continue;
            _added.Insert(0, full);
            changed = true;
        }
        if (changed)
            SaveAndTell();
    }

    /// <summary>Lists a song made in the Editor, and remembers its parts so it can be opened and worked on again.</summary>
    public void AddEdit(string path, IEnumerable<EditClip> parts)
    {
        string full = Path.GetFullPath(path);
        _edits[full] = parts.ToList();
        if (!_added.Contains(full, StringComparer.OrdinalIgnoreCase))
            _added.Insert(0, full);
        SaveAndTell();
    }

    public void Remove(string path)
    {
        string full = Path.GetFullPath(path);
        _added.RemoveAll(song => song.Equals(full, StringComparison.OrdinalIgnoreCase));
        _edits.Remove(full);
        SaveAndTell();
    }

    public void Remove(IEnumerable<string> paths)
    {
        var gone = new HashSet<string>(paths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        _added.RemoveAll(gone.Contains);
        foreach (string path in gone)
            _edits.Remove(path);
        SaveAndTell();
    }

    private bool IsDownload(string path)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        return folder != null && folder.Equals(Path.GetFullPath(_downloadsFolder()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for downloads and for the copies made when importing a pack. Anything else is the user's own file.</summary>
    public bool IsOwned(string path)
    {
        if (IsDownload(path))
            return true;
        string downloads = Path.GetFullPath(_downloadsFolder()).TrimEnd(Path.DirectorySeparatorChar);
        string? output = Path.GetDirectoryName(downloads);
        return output != null && PackImporter.IsImportedCopy(path, output);
    }

    public SongKind KindOf(string path) =>
        _edits.ContainsKey(Path.GetFullPath(path)) ? SongKind.Edited
        : IsDownload(path) ? SongKind.Downloaded
        : SongKind.Imported;

    /// <summary>The parts an edited song was made from, or null if it wasn't made in the Editor.</summary>
    public IReadOnlyList<EditClip>? PartsOf(string path) => _edits.GetValueOrDefault(Path.GetFullPath(path));

    /// <summary>The names of the songs an edited song was made from.</summary>
    public List<string> MadeFrom(string path) =>
        (PartsOf(path) ?? []).Select(part => part.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The song's name, marked when it was changed in the Editor so it stands out from the originals.</summary>
    public string LabelOf(string path) => KindOf(path) == SongKind.Edited ? $"{DisplayName(path)}  ·  edited" : DisplayName(path);

    /// <summary>A song's name without the video ID that downloads carry at the end of their file names.</summary>
    public static string DisplayName(string path) => VideoId().Replace(Path.GetFileNameWithoutExtension(path), "");

    [GeneratedRegex(@"\s*\[[A-Za-z0-9_-]{11}\]$")]
    private static partial Regex VideoId();

    private void SaveAndTell()
    {
        try
        {
            File.WriteAllText(ListPath, JsonSerializer.Serialize(new SavedList { Songs = _added, Edits = _edits }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list still works this session, and the next change tries to save it again.
        }
        Changed?.Invoke();
    }

    private static SavedList Load()
    {
        try
        {
            if (!File.Exists(ListPath))
                return new SavedList();
            string text = File.ReadAllText(ListPath);

            // Before edited songs were tracked, this file was only a list of paths.
            if (text.TrimStart().StartsWith('['))
                return new SavedList { Songs = JsonSerializer.Deserialize<List<string>>(text) ?? [] };
            return JsonSerializer.Deserialize<SavedList>(text) ?? new SavedList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged list only costs the names on it. The songs themselves are untouched.
            return new SavedList();
        }
    }
}
