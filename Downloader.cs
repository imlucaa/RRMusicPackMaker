using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RRMusicPackMaker;

public enum QueryKind { Empty, Link, Search, Protected }

public sealed record SearchResult(string Link, string Title, string Uploader, string Duration, string Album = "", long Plays = 0, bool Official = false)
{
    public string Display => string.Join("   ·   ", new[] { Official ? "Official" : "", Title, Uploader, Duration }.Where(part => part.Length > 0));

    /// <summary>The same video has a different address on YouTube and YouTube Music, so this is what tells results apart.</summary>
    public string VideoId => Link[(Link.IndexOf("v=", StringComparison.Ordinal) + 2)..];
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
    private const int YouTubeCount = 15;
    public const int PreviewSeconds = 60;
    private const string MusicSearchUrl = "https://music.youtube.com/youtubei/v1/search?prettyPrint=false";
    private const string MusicSongsFilter = "EgWKAQIIAWoMEA4QChADEAQQCRAF";
    private const string MusicVideosFilter = "EgWKAQIQAWoMEA4QChADEAQQCRAF";
    private const string TopicSuffix = " - Topic";
    private const double ExactNameBonus = 2;
    private const long HitPlays = 100_000;
    private const double HitFloor = 0.001;

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
        "--", $"ytsearch{YouTubeCount}:{query.Trim()}",
    ];

    internal static List<string> DownloadArguments(string target, string folder, string ffmpegFolder, string? javaScript = null) =>
    [
        .. RuntimeArguments(javaScript),
        "--ignore-config", "--encoding", "utf-8", "--no-warnings",
        "-f", "bestaudio/best", "-x",
        // One song only: never a whole playlist, a live stream or something enormous.
        "--no-playlist", "--playlist-items", "1", "--match-filter", "!is_live", "--max-filesize", "300M",
        "--no-mtime", "--windows-filenames", "--ffmpeg-location", ffmpegFolder,
        "-P", folder, "-o", "%(artist,uploader,channel)s - %(track,title).100B [%(id)s].%(ext)s",
        "--progress", "--newline", "--progress-template", $"download:{ProgressPrefix} %(progress._percent_str)s",
        "--print", $"after_move:{FilePrefix}\t%(filepath)s\t%(track,title)s\t%(artist,creator,uploader,channel)s",
        "--", target,
    ];

    /// <summary>Asks for the address of a video's audio without downloading it, so it can be previewed.</summary>
    internal static List<string> PreviewArguments(string target, string? javaScript = null) =>
    [
        .. RuntimeArguments(javaScript),
        "--ignore-config", "--encoding", "utf-8", "--no-warnings",
        "-f", "bestaudio/best", "--no-playlist", "--playlist-items", "1", "--get-url",
        "--", target,
    ];

    /// <summary>Fetches the start of a song into a temporary WAV, so it can be heard before downloading.</summary>
    public static async Task<string> PreviewAsync(string target, Action<string> status, CancellationToken cancel)
    {
        string tool = await EnsureToolAsync(status, cancel);
        string ffmpeg = Tools.Find("ffmpeg");
        status("Getting a preview...");
        string? address = null;
        var errors = new StringBuilder();
        int exit = await RunAsync(tool, PreviewArguments(target, FindNode()), line =>
        {
            if (address == null && line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                address = line.Trim();
        }, line => errors.AppendLine(line), cancel);
        if (exit != 0 || address == null)
            throw new DownloadFailedException(FriendlyError(LastError(errors.ToString())));

        string file = Path.Combine(Path.GetTempPath(), $"rrp_{Guid.NewGuid():N}.wav");
        try
        {
            int converted = await RunAsync(ffmpeg, ["-v", "error", "-nostdin", "-y", "-i", address, "-t", PreviewSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-vn", "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", file], _ => { }, _ => { }, cancel);
            if (converted != 0 || !File.Exists(file))
                throw new DownloadFailedException("Couldn't get a preview of that one. You can still download it.");
            return file;
        }
        catch
        {
            File.Delete(file);
            throw;
        }
    }

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
        string artist = parts[3] == "NA" ? "" : parts[3];
        if (artist.EndsWith(TopicSuffix, StringComparison.Ordinal))
            artist = artist[..^TopicSuffix.Length];
        return new DownloadedSong(parts[1], parts[2], artist);
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

    internal static string ExpectedHash(string sums, string file = ToolName)
    {
        foreach (string line in sums.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == file)
                return parts[0];
        }
        throw new DownloadFailedException($"The release has no checksum for {file}, so it was not installed.");
    }

    /// <summary>
    /// Searches everywhere at once, so anything can be found: remixes, leaks, covers and uploads as well as
    /// official songs. Official matches come first, and they're the only results marked Official.
    /// </summary>
    public static async Task<List<SearchResult>> SearchAsync(string query, Action<string> status, CancellationToken cancel)
    {
        status("Searching YouTube Music and YouTube...");
        var songs = TryMusicSearchAsync(query, MusicSongsFilter, cancel);
        var videos = TryMusicSearchAsync(query, MusicVideosFilter, cancel);
        var youTube = SearchYouTubeAsync(query, status, cancel);
        Exception? youTubeFailure = null;
        try
        {
            await youTube;
        }
        catch (Exception ex) when (ex is DownloadFailedException or HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The other searches may still have answers. It only matters if nothing else found anything.
            youTubeFailure = ex;
        }
        var merged = MergeResults(query, await songs, await videos, youTubeFailure == null ? youTube.Result : []);
        if (merged.Count == 0 && youTubeFailure != null)
            throw youTubeFailure;
        return merged;
    }

    /// <summary>Official matches first, then everything else that was found, with each video listed once.</summary>
    internal static List<SearchResult> MergeResults(string query, List<SearchResult> songs, List<SearchResult> videos, List<SearchResult> youTube)
    {
        var official = RankMusicResults(query, songs).Select(song => song with { Official = true });
        return official.Concat(songs).Concat(videos).Concat(youTube).DistinctBy(result => result.VideoId).ToList();
    }

    /// <summary>A failed music search isn't fatal, because the fallbacks can still answer.</summary>
    private static async Task<List<SearchResult>> TryMusicSearchAsync(string query, string filter, CancellationToken cancel)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            http.DefaultRequestHeaders.Add("Origin", "https://music.youtube.com");
            string body = JsonSerializer.Serialize(new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB_REMIX",
                        clientVersion = $"1.{DateTime.UtcNow.AddDays(-1):yyyyMMdd}.01.00",
                        hl = "en",
                    },
                },
                query,
                @params = filter,
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(MusicSearchUrl, content, cancel);
            response.EnsureSuccessStatusCode();
            return ParseMusicResults(await response.Content.ReadAsStringAsync(cancel));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            return [];
        }
    }

    /// <summary>Reads songs and videos out of a YouTube Music search reply.</summary>
    internal static List<SearchResult> ParseMusicResults(string json)
    {
        var results = new List<SearchResult>();
        using var document = JsonDocument.Parse(json);
        Walk(document.RootElement, results);
        return results;
    }

    private static void Walk(JsonElement element, List<SearchResult> results)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Walk(item, results);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("musicResponsiveListItemRenderer"))
                {
                    if (ParseMusicItem(property.Value) is { } result)
                        results.Add(result);
                }
                else
                {
                    Walk(property.Value, results);
                }
            }
        }
    }

    private static SearchResult? ParseMusicItem(JsonElement item)
    {
        if (!item.TryGetProperty("playlistItemData", out var data)
            || !data.TryGetProperty("videoId", out var idElement)
            || idElement.GetString() is not { Length: > 0 } id
            || !item.TryGetProperty("flexColumns", out var columns)
            || columns.GetArrayLength() < 2)
            return null;

        string title = string.Concat(ColumnRuns(columns[0]));
        if (title.Length == 0)
            return null;

        // The second column reads "Artist & Artist • Album • 2:02", with "42M views" in place of the album on videos.
        var groups = new List<string> { "" };
        foreach (string run in ColumnRuns(columns[1]))
        {
            if (run.Trim() == "•")
                groups.Add("");
            else
                groups[^1] += run;
        }
        string artist = groups[0].Trim();
        string duration = groups.Skip(1).LastOrDefault(group => Regex.IsMatch(group.Trim(), @"^\d+(:\d\d)+$"))?.Trim() ?? "";
        string album = groups.Skip(1).FirstOrDefault(group => group.Trim() != duration && !IsCount(group))?.Trim() ?? "";

        long plays = 0;
        foreach (string text in groups.Skip(1).Concat(columns.GetArrayLength() > 2 ? [string.Concat(ColumnRuns(columns[2]))] : []))
            if (IsCount(text))
                plays = Math.Max(plays, ParseCount(text));

        return new SearchResult($"https://music.youtube.com/watch?v={id}", title, artist, duration, album, plays);
    }

    private static List<string> ColumnRuns(JsonElement column)
    {
        var runs = new List<string>();
        if (column.TryGetProperty("musicResponsiveListItemFlexColumnRenderer", out var renderer)
            && renderer.TryGetProperty("text", out var text)
            && text.TryGetProperty("runs", out var items))
            foreach (var run in items.EnumerateArray())
                if (run.TryGetProperty("text", out var value))
                    runs.Add(value.GetString() ?? "");
        return runs;
    }

    private static bool IsCount(string text) => Regex.IsMatch(text.Trim(), @"^[\d.,]+[KMB]?\s+(plays?|views?)$", RegexOptions.IgnoreCase);

    /// <summary>Reads "6.2M plays" as 6200000.</summary>
    internal static long ParseCount(string text)
    {
        var match = Regex.Match(text, @"([\d.,]+)\s*([KMB]?)", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return 0;
        double scale = match.Groups[2].Value.ToUpperInvariant() switch { "K" => 1e3, "M" => 1e6, "B" => 1e9, _ => 1 };
        return (long)(number * scale);
    }

    /// <summary>
    /// Keeps results with an artist whose title or artist matches the search. Best first: most search
    /// words found, then popularity, where an exact name counts as much as being 100 times more played.
    /// When the top result is a real hit, tracks that almost nobody plays are dropped.
    /// </summary>
    internal static List<SearchResult> RankMusicResults(string query, List<SearchResult> results)
    {
        string[] words = Words(query);
        var scored = results
            .Where(result => result.Uploader.Length > 0 && result.Duration.Length > 0)
            .Select(result =>
            {
                string[] title = Words(result.Title);
                string[] named = [.. title, .. Words(result.Uploader)];
                string[] all = [.. named, .. Words(result.Album)];
                int direct = words.Count(word => named.Any(other => WordMatches(word, other)));
                int found = words.Count(word => all.Any(other => WordMatches(word, other)));
                bool exact = Same(words, title) || (direct == words.Length && named.Length == words.Length);
                return (Result: result, Direct: direct, Found: found, Weight: Math.Log10(result.Plays + 1) + (exact ? ExactNameBonus : 0));
            })
            .Where(item => item.Direct > 0)
            .ToList();

        // When something matches every word, partial matches are only noise.
        if (scored.Any(item => item.Found == words.Length))
            scored = scored.Where(item => item.Found == words.Length).ToList();

        var ranked = scored
            .OrderByDescending(item => item.Found)
            .ThenByDescending(item => item.Weight)
            .Select(item => item.Result)
            .DistinctBy(result => result.Link)
            .ToList();

        if (ranked.Count > 0 && ranked[0].Plays >= HitPlays)
        {
            long floor = (long)(ranked[0].Plays * HitFloor);
            ranked = ranked.Where(result => result.Plays >= floor).ToList();
        }
        return ranked.Take(SearchCount).ToList();
    }

    private static bool Same(string[] words, string[] other) =>
        words.Length == other.Length && words.Zip(other).All(pair => WordMatches(pair.First, pair.Second));

    private static string[] Words(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}*]+").Select(match => match.Value).Where(word => word.Trim('*').Length > 0).ToArray();

    /// <summary>A censored word like "f*ck" matches "fuck".</summary>
    private static bool WordMatches(string word, string other)
    {
        if (word == other)
            return true;
        if (!other.Contains('*') && !word.Contains('*'))
            return false;
        string pattern = "^" + Regex.Escape(other).Replace(@"\*", ".*") + "$";
        string wordPattern = "^" + Regex.Escape(word).Replace(@"\*", ".*") + "$";
        return Regex.IsMatch(word, pattern) || Regex.IsMatch(other, wordPattern);
    }

    private static async Task<List<SearchResult>> SearchYouTubeAsync(string query, Action<string> status, CancellationToken cancel)
    {
        string tool = await EnsureToolAsync(status, cancel);
        var results = new List<SearchResult>();
        var errors = new StringBuilder();
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
    private readonly FieldBox _query = new("Type a song name, or paste a link") { Dock = DockStyle.Fill };
    private readonly ModernButton _go = new() { Text = "Search", Tag = Theme.AccentTag, Width = 110, Height = 28, Margin = new Padding(8, 0, 0, 0) };
    private readonly SideList _results = new() { Dock = DockStyle.Fill, Tag = Theme.PanelTag };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, Tag = Theme.MutedTag, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ThinProgress _progress = new() { Dock = DockStyle.Bottom };
    private readonly ModernButton _download = new() { Text = "Download", Tag = Theme.AccentTag, AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0), Enabled = false };
    private readonly ModernButton _cancel = new() { Text = "Cancel", AutoSize = true, Height = 28 };
    private readonly ModernButton _preview = new() { Text = "▶  Preview", AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 0) };
    private readonly ModernButton _stopPreview = new() { Text = "■  Stop", AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 0), Visible = false };
    private string? _previewing;
    private List<SearchResult> _found = [];
    private CancellationTokenSource? _work;

    public DownloadedSong? Result { get; private set; }

    public DownloadForm(string folder, int trackCount)
    {
        _folder = folder;
        Text = trackCount == 1 ? "Get a song" : $"Get a song for {trackCount} tracks";
        Width = 860;
        Height = 560;
        MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.UiFont();
        BuildLayout();
        Theme.Apply(this);
        AudioPreview.Changed += ShowPreview;
        FormClosed += (_, _) =>
        {
            _work?.Cancel();
            AudioPreview.Changed -= ShowPreview;
            AudioPreview.StopFor(this);
        };
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
            Text = "Searches YouTube Music and YouTube, with official songs first. Only download what you have the right to use.",
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
        buttons.Controls.AddRange([_download, _cancel, _stopPreview, _preview]);
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
        _preview.Click += async (_, _) => await PreviewSelectedAsync();
        _stopPreview.Click += (_, _) => AudioPreview.StopFor(this);
        _results.DoubleClick += async (_, _) => await DownloadSelectedAsync();
        _results.SelectedIndexChanged += (_, _) =>
        {
            UpdateDownloadButton();
            ShowPreview();
        };
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
        _preview.Enabled = _work == null && _results.SelectedIndex >= 0;
    }

    private SearchResult? Picked => _results.SelectedIndex >= 0 && _results.SelectedIndex < _found.Count ? _found[_results.SelectedIndex] : null;

    /// <summary>The button pauses the song it's playing. On any other result it previews that one instead.</summary>
    private void ShowPreview()
    {
        if (IsDisposed)
            return;
        bool active = AudioPreview.IsActive(this);
        _stopPreview.Visible = active;
        bool thisOne = active && Picked?.Link == _previewing;
        _preview.Text = !thisOne ? "▶  Preview" : AudioPreview.State == PreviewState.Paused ? "▶  Resume" : "❚❚  Pause";
    }

    /// <summary>Plays the start of the picked result. It's only heard here, and nothing is saved.</summary>
    internal async Task PreviewSelectedAsync()
    {
        if (Picked is not { } pick)
            return;
        if (AudioPreview.IsActive(this) && pick.Link == _previewing)
        {
            AudioPreview.TogglePause();
            return;
        }
        if (_work != null)
            return;
        AudioPreview.StopFor(this);
        using var work = Begin();
        try
        {
            string file = await Downloader.PreviewAsync(pick.Link, Report, work.Token);
            if (IsDisposed)
            {
                File.Delete(file);
                return;
            }
            if (!AudioPreview.Play(file, this))
            {
                _status.Text = "Could not play the sound. Check your audio device.";
                return;
            }
            _previewing = pick.Link;
            ShowPreview();
            _status.Text = $"Playing the first {Downloader.PreviewSeconds} seconds. Nothing is downloaded yet.";
        }
        catch (Exception ex) when (ex is DownloadFailedException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or FileNotFoundException)
        {
            ShowFailure(ex);
        }
        finally
        {
            End();
        }
    }

    private void UpdateGoButton()
    {
        _go.Text = Downloader.Classify(_query.Box.Text) == QueryKind.Link ? "Download" : "Search";
        _go.Enabled = _work == null;
    }

    private void SetBusy(bool busy)
    {
        _cancel.Text = busy ? "Stop" : "Cancel";
        _query.Box.ReadOnly = busy;
        UpdateGoButton();
        UpdateDownloadButton();
    }

    internal async Task GoAsync()
    {
        if (_work != null)
            return;
        string text = _query.Box.Text.Trim();
        switch (Downloader.Classify(text))
        {
            case QueryKind.Empty:
                _status.Text = "Type a song name, or paste a link.";
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
            _status.Text = _found.Count == 0 ? "No results. Try other words."
                : $"{_found.Count} results. Preview one, then click Download.";
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
        {
            var pick = _found[_results.SelectedIndex];
            await DownloadAsync(pick.Link, pick);
        }
    }

    private async Task DownloadAsync(string target, SearchResult? pick = null)
    {
        AudioPreview.StopFor(this);
        using var work = Begin();
        _progress.Fraction = 0;
        try
        {
            var song = await Downloader.DownloadAsync(target, _folder, Report,
                fraction => OnWindow(() => _progress.Fraction = fraction), work.Token);
            if (IsDisposed)
                return;
            // The search result has the cleanest title and artist, so it wins for official songs.
            Result = pick is { Official: true } ? song with { Title = pick.Title, Artist = pick.Uploader } : song;
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
    internal int ResultCountForTest => _results.Items.Count;
    internal void SelectResultForTest(int index) => _results.SelectedIndex = index;
    internal string PreviewTextForTest => _preview.Text;
    internal bool StopPreviewVisibleForTest => _stopPreview.Visible;
}
