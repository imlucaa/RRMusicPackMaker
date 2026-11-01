using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RRMusicPackMaker;

public enum QueryKind { Empty, Link, Search, Protected }

public sealed record SearchResult(string Link, string Title, string Uploader, string Duration)
{
    public string Display => Uploader.Length > 0 ? $"{Title}   ·   {Uploader}   ·   {Duration}" : $"{Title}   ·   {Duration}";
}

public sealed record DownloadedSong(string Path, string Title, string Artist);

public sealed class DownloadFailedException(string message) : Exception(message);

/// <summary>Finds songs and fetches their audio with yt-dlp, which is installed next to the app on first use.</summary>
public static class Downloader
{
    private const string ReleaseBase = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
    private const string ToolName = "yt-dlp.exe";
    private const string ProgressPrefix = "PROGRESS";
    private const string FilePrefix = "FILE";
    private const int SearchCount = 8;

    // These services encrypt their audio, so yt-dlp cannot and should not fetch it.
    private static readonly string[] ProtectedHosts =
        ["spotify.com", "music.apple.com", "tidal.com", "deezer.com", "music.amazon.", "pandora.com"];

    public static QueryKind Classify(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return QueryKind.Empty;
        bool isLink = Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
        if (!isLink)
            return text.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase) ? QueryKind.Protected : QueryKind.Search;
        string host = uri!.Host.ToLowerInvariant();
        return ProtectedHosts.Any(host.Contains) ? QueryKind.Protected : QueryKind.Link;
    }

    /// <summary>The target always follows "--", so typed text can never be read as a yt-dlp option.</summary>
    internal static List<string> SearchArguments(string query, string? javaScript = null) =>
    [
        .. RuntimeArguments(javaScript),
        "--flat-playlist", "--no-warnings", "--encoding", "utf-8", "--ignore-config",
        "--print", "%(id)s\t%(title)s\t%(uploader,channel)s\t%(duration_string)s",
        "--", $"ytsearch{SearchCount}:{query.Trim()}",
    ];

    internal static List<string> DownloadArguments(string target, string folder, string ffmpegFolder, string? javaScript = null) =>
    [
        .. RuntimeArguments(javaScript),
        "--ignore-config", "--encoding", "utf-8", "--no-warnings",
        "-f", "bestaudio/best", "-x",
        // One song only: never a whole playlist, a live stream or something enormous.
        "--no-playlist", "--playlist-items", "1", "--match-filter", "!is_live", "--max-filesize", "300M",
        "--no-mtime", "--windows-filenames", "--ffmpeg-location", ffmpegFolder,
        "-P", folder, "-o", "%(title).120B [%(id)s].%(ext)s",
        "--progress", "--newline", "--progress-template", $"download:{ProgressPrefix} %(progress._percent_str)s",
        "--print", $"after_move:{FilePrefix}\t%(filepath)s\t%(title)s\t%(artist,creator,uploader,channel)s",
        "--", target,
    ];

    private static List<string> RuntimeArguments(string? javaScript) =>
        javaScript == null ? [] : ["--js-runtimes", $"node:{javaScript}"];

    /// <summary>YouTube needs a JavaScript runtime for some formats. yt-dlp only looks for Deno by itself.</summary>
    private static string? FindNode()
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(dir.Trim('"'), "node.exe");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    internal static SearchResult? ParseSearchLine(string line)
    {
        string[] parts = line.Split('\t');
        if (parts.Length < 4 || parts[0].Length == 0 || parts[0] == "NA")
            return null;
        // Live streams have no length, and they never finish downloading.
        if (parts[3] == "NA" || parts[3].Length == 0)
            return null;
        return new SearchResult($"https://www.youtube.com/watch?v={parts[0]}", parts[1], parts[2] == "NA" ? "" : parts[2], parts[3]);
    }

    internal static DownloadedSong? ParseFileLine(string line)
    {
        string[] parts = line.Split('\t');
        if (parts.Length < 4 || parts[0] != FilePrefix)
            return null;
        return new DownloadedSong(parts[1], parts[2], parts[3] == "NA" ? "" : parts[3]);
    }

    internal static double? ParseProgress(string line)
    {
        if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            return null;
        string number = line[ProgressPrefix.Length..].Trim().TrimEnd('%');
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) ? percent / 100 : null;
    }

    /// <summary>Turns yt-dlp's error text into something a person can act on.</summary>
    internal static string FriendlyError(string error)
    {
        string text = error.Replace("ERROR:", "", StringComparison.Ordinal).Trim();
        if (text.Contains("DRM", StringComparison.OrdinalIgnoreCase))
            return "That song is copy-protected, so it can't be downloaded. Search for the song name instead.";
        if (text.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || text.Contains("confirm your age", StringComparison.OrdinalIgnoreCase))
            return "YouTube wants a sign-in for this video. Try a different result.";
        if (text.Contains("Private video", StringComparison.OrdinalIgnoreCase) || text.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return "That video is private or unavailable. Try a different result.";
        if (text.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase))
            return "That site isn't supported. Search for the song name instead.";
        if (text.Contains("HTTP Error 4", StringComparison.OrdinalIgnoreCase))
            return "That page wasn't found. Check the link.";
        if (text.Contains("getaddrinfo", StringComparison.OrdinalIgnoreCase) || text.Contains("Unable to download", StringComparison.OrdinalIgnoreCase))
            return "Couldn't reach the site. Check your internet connection.";
        return text.Length > 0 ? text : "The download failed.";
    }

    /// <summary>Whether updating yt-dlp could help. Sites change often and an old version stops working.</summary>
    internal static bool WorthUpdating(string error) =>
        !error.Contains("DRM", StringComparison.OrdinalIgnoreCase)
        && !error.Contains("Private video", StringComparison.OrdinalIgnoreCase)
        && !error.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
        && !error.Contains("HTTP Error 4", StringComparison.OrdinalIgnoreCase)
        && !error.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)
        && !error.Contains("getaddrinfo", StringComparison.OrdinalIgnoreCase);

    public static string? FindTool()
    {
        string[] places =
        [
            Path.Combine(AppContext.BaseDirectory, ToolName),
            Path.Combine(AppStorage.Folder, "tools", ToolName),
        ];
        foreach (string place in places)
            if (File.Exists(place))
                return place;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(dir.Trim('"'), ToolName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>Installs yt-dlp if needed. The file must match the checksum published with the release.</summary>
    public static async Task<string> EnsureToolAsync(Action<string> status, CancellationToken cancel)
    {
        string? found = FindTool();
        if (found != null)
            return found;

        status("Installing yt-dlp, first time only...");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RRMusicPackMaker/1.0");
        string sums = await http.GetStringAsync(ReleaseBase + "SHA2-256SUMS", cancel);
        string expected = ExpectedHash(sums);
        byte[] tool = await http.GetByteArrayAsync(ReleaseBase + ToolName, cancel);
        string actual = Convert.ToHexString(SHA256.HashData(tool));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new DownloadFailedException("The yt-dlp download didn't match its published checksum, so it was not installed.");

        foreach (string folder in new[] { AppContext.BaseDirectory, Path.Combine(AppStorage.Folder, "tools") })
        {
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, ToolName);
                string temp = path + ".tmp";
                await File.WriteAllBytesAsync(temp, tool, cancel);
                File.Move(temp, path, true);
                return path;
            }
            catch (UnauthorizedAccessException)
            {
                // The app's own folder can be read-only, so fall back to the data folder.
            }
        }
        throw new DownloadFailedException("Couldn't save yt-dlp in the app's folder or data folder.");
    }

    internal static string ExpectedHash(string sums)
    {
        foreach (string line in sums.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == ToolName)
                return parts[0];
        }
        throw new DownloadFailedException("The yt-dlp release has no checksum for its Windows program, so it was not installed.");
    }

    public static async Task<List<SearchResult>> SearchAsync(string query, Action<string> status, CancellationToken cancel)
    {
        string tool = await EnsureToolAsync(status, cancel);
        var results = new List<SearchResult>();
        var errors = new StringBuilder();
        status("Searching...");
        int exit = await RunAsync(tool, SearchArguments(query, FindNode()), line =>
        {
            if (ParseSearchLine(line) is { } result)
                results.Add(result);
        }, line => errors.AppendLine(line), cancel);
        if (exit != 0 && results.Count == 0)
            throw new DownloadFailedException(FriendlyError(LastError(errors.ToString())));
        return results;
    }

    /// <summary>Fetches one song's audio. If it fails in a way an update could fix, updates yt-dlp and tries once more.</summary>
    public static async Task<DownloadedSong> DownloadAsync(string target, string folder, Action<string> status, Action<double> progress, CancellationToken cancel)
    {
        string tool = await EnsureToolAsync(status, cancel);
        Directory.CreateDirectory(folder);
        string ffmpeg = Path.GetDirectoryName(Tools.Find("ffmpeg"))!;
        try
        {
            try
            {
                return await DownloadOnceAsync(tool, target, folder, ffmpeg, status, progress, cancel);
            }
            catch (DownloadFailedException first) when (WorthUpdating(first.Data["raw"] as string ?? ""))
            {
                status("That failed. Updating yt-dlp and trying again...");
                int updated = await RunAsync(tool, ["-U"], _ => { }, _ => { }, cancel);
                if (updated != 0)
                    throw;
                return await DownloadOnceAsync(tool, target, folder, ffmpeg, status, progress, cancel);
            }
        }
        catch (Exception ex) when (ex is DownloadFailedException or OperationCanceledException)
        {
            RemovePartialFiles(folder);
            throw;
        }
    }

    private static async Task<DownloadedSong> DownloadOnceAsync(string tool, string target, string folder, string ffmpeg, Action<string> status, Action<double> progress, CancellationToken cancel)
    {
        DownloadedSong? song = null;
        var errors = new StringBuilder();
        status("Downloading...");
        int exit = await RunAsync(tool, DownloadArguments(target, folder, ffmpeg, FindNode()), line =>
        {
            if (ParseProgress(line) is { } fraction)
                progress(fraction);
            else if (ParseFileLine(line) is { } file)
                song = file;
        }, line => errors.AppendLine(line), cancel);

        if (exit == 0 && song != null && File.Exists(song.Path))
            return song;

        string raw = LastError(errors.ToString());
        string message = raw.Length > 0 ? FriendlyError(raw) : "Nothing was downloaded. The video may be live or over 300 MB.";
        var failure = new DownloadFailedException(message);
        failure.Data["raw"] = raw;
        throw failure;
    }

    private static string LastError(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault(line => line.StartsWith("ERROR", StringComparison.Ordinal)) ?? lines.LastOrDefault() ?? "";
    }

    private static void RemovePartialFiles(string folder)
    {
        foreach (string pattern in new[] { "*.part", "*.ytdl", "*.temp.*" })
            foreach (string file in Directory.GetFiles(folder, pattern))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Still held open for a moment. It's only a leftover, and the next run can reuse it.
                }
            }
    }

    private static async Task<int> RunAsync(string tool, IEnumerable<string> arguments, Action<string> onOutput, Action<string> onError, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUTF8"] = "1";
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new DownloadFailedException("Couldn't start yt-dlp.");
        using var kill = cancel.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It already finished.
            }
        });

        static async Task Pump(StreamReader reader, Action<string> handle)
        {
            while (await reader.ReadLineAsync() is { } line)
                handle(line);
        }

        await Task.WhenAll(Pump(process.StandardOutput, onOutput), Pump(process.StandardError, onError));
        await process.WaitForExitAsync(CancellationToken.None);
        cancel.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
}

/// <summary>Find a song by name or paste a link, then fetch it for the selected tracks.</summary>
public sealed class DownloadForm : Form
{
    private readonly string _folder;
    private readonly FieldBox _query = new("Paste a link, or type a song name") { Dock = DockStyle.Fill };
    private readonly Button _go = new() { Text = "Search", Tag = Theme.AccentTag, Width = 110, Height = 32, Margin = new Padding(8, 0, 0, 0) };
    private readonly SideList _results = new() { Dock = DockStyle.Fill, Tag = Theme.PanelTag };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, Tag = Theme.MutedTag, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ThinProgress _progress = new() { Dock = DockStyle.Bottom };
    private readonly Button _download = new() { Text = "Download", Tag = Theme.AccentTag, AutoSize = true, Height = 32, Margin = new Padding(6, 0, 0, 0), Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Height = 32 };
    private List<SearchResult> _found = [];
    private CancellationTokenSource? _work;

    public DownloadedSong? Result { get; private set; }

    public DownloadForm(string folder, int trackCount)
    {
        _folder = folder;
        Text = trackCount == 1 ? "Get a song" : $"Get a song for {trackCount} tracks";
        Width = 720;
        Height = 520;
        MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        BuildLayout();
        Theme.Apply(this);
        FormClosed += (_, _) => _work?.Cancel();
    }

    private void BuildLayout()
    {
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 54, ColumnCount = 2, Padding = new Padding(14, 12, 14, 8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _query.Margin = new Padding(0);
        top.Controls.Add(_query, 0, 0);
        top.Controls.Add(_go, 1, 0);

        var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 4) };
        holder.Controls.Add(_results);

        var note = new Label
        {
            Text = "YouTube, YouTube Music, SoundCloud, Bandcamp and more. Only download what you have the right to use.",
            AutoSize = false,
            Height = 24,
            Dock = DockStyle.Bottom,
            Tag = Theme.MutedTag,
            Padding = new Padding(14, 0, 14, 4),
        };

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 52, ColumnCount = 2, Padding = new Padding(14, 8, 14, 8), Tag = Theme.PanelTag };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.AddRange([_download, _cancel]);
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(buttons, 1, 0);

        Controls.Add(holder);
        Controls.Add(note);
        Controls.Add(_progress);
        Controls.Add(footer);
        Controls.Add(top);

        _query.Box.TextChanged += (_, _) => UpdateGoButton();
        _query.Box.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await GoAsync();
            }
        };
        _go.Click += async (_, _) => await GoAsync();
        _download.Click += async (_, _) => await DownloadSelectedAsync();
        _results.DoubleClick += async (_, _) => await DownloadSelectedAsync();
        _results.SelectedIndexChanged += (_, _) => UpdateDownloadButton();
        _cancel.Click += (_, _) =>
        {
            if (_work != null)
                _work.Cancel();
            else
                Close();
        };
        Shown += (_, _) => _query.Box.Focus();
    }

    private void UpdateDownloadButton()
    {
        _download.Enabled = _work == null && _results.SelectedIndex >= 0;
        Theme.Apply(_download);
    }

    private void UpdateGoButton()
    {
        _go.Text = Downloader.Classify(_query.Box.Text) == QueryKind.Link ? "Download" : "Search";
        _go.Enabled = _work == null;
        Theme.Apply(_go);
    }

    private void SetBusy(bool busy)
    {
        _cancel.Text = busy ? "Stop" : "Cancel";
        _query.Box.ReadOnly = busy;
        UpdateGoButton();
        UpdateDownloadButton();
        Theme.Apply(_cancel);
    }

    internal async Task GoAsync()
    {
        if (_work != null)
            return;
        string text = _query.Box.Text.Trim();
        switch (Downloader.Classify(text))
        {
            case QueryKind.Empty:
                _status.Text = "Paste a link, or type a song name.";
                break;
            case QueryKind.Protected:
                _status.Text = "That service copy-protects its music, so it can't be downloaded. Type the song name instead.";
                break;
            case QueryKind.Link:
                await DownloadAsync(text);
                break;
            default:
                await SearchAsync(text);
                break;
        }
    }

    private async Task SearchAsync(string text)
    {
        using var work = Begin();
        _results.Items.Clear();
        _found = [];
        try
        {
            _found = await Downloader.SearchAsync(text, Report, work.Token);
            if (IsDisposed)
                return;
            foreach (var result in _found)
                _results.Items.Add(new SideList.Item(result.Display, -1));
            _status.Text = _found.Count == 0 ? "No results. Try other words." : "Pick a song, then click Download.";
            if (_found.Count > 0)
                _results.SelectedIndex = 0;
        }
        catch (Exception ex) when (ex is DownloadFailedException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowFailure(ex);
        }
        finally
        {
            End();
        }
    }

    private async Task DownloadSelectedAsync()
    {
        if (_work == null && _results.SelectedIndex >= 0 && _results.SelectedIndex < _found.Count)
            await DownloadAsync(_found[_results.SelectedIndex].Link);
    }

    private async Task DownloadAsync(string target)
    {
        using var work = Begin();
        _progress.Fraction = 0;
        try
        {
            var song = await Downloader.DownloadAsync(target, _folder, Report,
                fraction => OnWindow(() => _progress.Fraction = fraction), work.Token);
            if (IsDisposed)
                return;
            Result = song;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is DownloadFailedException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            ShowFailure(ex);
        }
        finally
        {
            End();
        }
    }

    private CancellationTokenSource Begin()
    {
        _work = new CancellationTokenSource();
        SetBusy(true);
        return _work;
    }

    private void End()
    {
        _work = null;
        if (!IsDisposed)
            SetBusy(false);
    }

    private void ShowFailure(Exception ex)
    {
        if (IsDisposed)
            return;
        _progress.Fraction = 0;
        _status.Text = ex switch
        {
            OperationCanceledException => "Stopped.",
            HttpRequestException => "Couldn't reach the internet to install yt-dlp.",
            _ => ex.Message,
        };
    }

    private void Report(string message) => OnWindow(() => _status.Text = message);

    private void OnWindow(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window closed as the message arrived.
        }
    }

    // For the automated tests.
    internal void SetQueryForTest(string text) => _query.Box.Text = text;
    internal string StatusForTest => _status.Text;
    internal string GoTextForTest => _go.Text;
}
