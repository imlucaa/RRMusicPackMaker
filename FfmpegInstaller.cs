using System.IO.Compression;
using System.Security.Cryptography;

namespace RRMusicPackMaker;

/// <summary>Downloads FFmpeg for people who don't have it, checks it against its published checksum, and puts it next to the app.</summary>
public static class FfmpegInstaller
{
    // The "shared" build keeps its code in DLLs that both programs reuse: a 77 MB download and 152 MB
    // installed, against 192 MB and 340 MB for the standalone build. Every format the app accepts was tested on it.
    private const string ReleaseBase = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";
    private const string ZipName = "ffmpeg-master-latest-win64-lgpl-shared.zip";
    private const string SumsName = "checksums.sha256";
    private static readonly string[] Programs = ["ffmpeg.exe", "ffprobe.exe"];

    public static bool IsInstalled => Tools.TryFind("ffmpeg") != null && Tools.TryFind("ffprobe") != null;

    public static async Task<string> InstallAsync(Action<string> status, Action<double> progress, CancellationToken cancel)
    {
        status("Downloading FFmpeg...");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RRMusicPackMaker/1.0");

        string expected = Downloader.ExpectedHash(await http.GetStringAsync(ReleaseBase + SumsName, cancel), ZipName);
        string zip = Path.Combine(Path.GetTempPath(), $"rrmusic_ffmpeg_{Guid.NewGuid():N}.zip");
        try
        {
            string actual = await DownloadAsync(http, zip, progress, cancel);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new DownloadFailedException("The FFmpeg download didn't match its published checksum, so it was not installed.");

            status("Installing FFmpeg...");
            string folder = await Task.Run(() => ExtractToFirstWritable(zip), cancel);
            Tools.RunBinary("ffmpeg", "-version");
            return folder;
        }
        finally
        {
            File.Delete(zip);
        }
    }

    /// <summary>Saves the download to a file while hashing it. Returns the hash.</summary>
    private static async Task<string> DownloadAsync(HttpClient http, string path, Action<double> progress, CancellationToken cancel)
    {
        using var response = await http.GetAsync(ReleaseBase + ZipName, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        await using var target = File.Create(path);
        var buffer = new byte[1 << 20];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancel)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancel);
            hash.AppendData(buffer, 0, read);
            done += read;
            if (total > 0)
                progress((double)done / total);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ExtractToFirstWritable(string zip)
    {
        foreach (string folder in new[] { Tools.AppFfmpegFolder, Tools.DataFfmpegFolder })
        {
            try
            {
                Extract(zip, folder);
                return folder;
            }
            catch (UnauthorizedAccessException)
            {
                // The app's own folder can be read-only, so fall back to the data folder.
            }
        }
        throw new DownloadFailedException("Couldn't save FFmpeg in the app's folder or data folder.");
    }

    /// <summary>Takes only the two programs and their DLLs out of the archive, by name, and only if both programs are there.</summary>
    internal static void Extract(string zip, string folder)
    {
        Directory.CreateDirectory(folder);
        using var archive = ZipFile.OpenRead(zip);
        var bin = archive.Entries.Where(entry => Path.GetFileName(Path.GetDirectoryName(entry.FullName.Replace('/', Path.DirectorySeparatorChar)) ?? "")
            .Equals("bin", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (string program in Programs)
            if (!bin.Any(entry => entry.Name.Equals(program, StringComparison.OrdinalIgnoreCase)))
                throw new DownloadFailedException($"The FFmpeg download has no {program}, so it was not installed.");

        foreach (var entry in bin)
        {
            bool wanted = Programs.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)
                || entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            if (!wanted)
                continue;
            // entry.Name is only the final part of the path, so nothing can land outside the folder.
            string path = Path.Combine(folder, entry.Name);
            string temp = path + ".tmp";
            entry.ExtractToFile(temp, true);
            File.Move(temp, path, true);
        }
    }
}
