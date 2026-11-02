using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace RRMusicPackMaker;

public enum PreviewState { Stopped, Playing, Paused }

/// <summary>
/// Plays one temporary WAV at a time for the whole app, with pause and resume. Starting a new one stops
/// the last. It takes over the file and deletes it when playback ends. Only call it from the window thread.
/// </summary>
public static class AudioPreview
{
    private const string Alias = "rrmusicpreview";

    private static object? _owner;
    private static string? _file;
    private static System.Windows.Forms.Timer? _watch;

    /// <summary>Fires whenever playback starts, pauses, resumes or ends.</summary>
    public static event Action? Changed;

    public static PreviewState State { get; private set; }

    /// <summary>True while this window's music is playing or paused.</summary>
    public static bool IsActive(object owner) => State != PreviewState.Stopped && ReferenceEquals(_owner, owner);

    /// <summary>How far into the file playback is.</summary>
    public static long PositionMilliseconds
    {
        get
        {
            string text = Query("position");
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long position) ? position : 0;
        }
    }

    /// <summary>Returns false if the sound couldn't start, like when there's no audio device. The file is deleted either way once done.</summary>
    public static bool Play(string file, object owner)
    {
        Stop();
        if (Send($"open \"{ShortPath(file)}\" type waveaudio alias {Alias}") != 0)
        {
            Delete(file);
            return false;
        }
        Send($"set {Alias} time format milliseconds");
        if (Send($"play {Alias}") != 0)
        {
            Send($"close {Alias}");
            Delete(file);
            return false;
        }

        _file = file;
        _owner = owner;
        State = PreviewState.Playing;
        _watch = new System.Windows.Forms.Timer { Interval = 100 };
        _watch.Tick += (_, _) =>
        {
            if (State == PreviewState.Playing && Query("mode") == "stopped")
                Stop();
        };
        _watch.Start();
        Changed?.Invoke();
        return true;
    }

    public static void TogglePause()
    {
        if (State == PreviewState.Playing)
        {
            Send($"pause {Alias}");
            State = PreviewState.Paused;
        }
        else if (State == PreviewState.Paused)
        {
            Send($"resume {Alias}");
            State = PreviewState.Playing;
        }
        else
        {
            return;
        }
        Changed?.Invoke();
    }

    public static void Stop()
    {
        if (State == PreviewState.Stopped)
            return;
        _watch?.Dispose();
        _watch = null;
        Send($"stop {Alias}");
        Send($"close {Alias}");
        State = PreviewState.Stopped;
        _owner = null;
        if (_file != null)
            Delete(_file);
        _file = null;
        Changed?.Invoke();
    }

    /// <summary>Stops only if this window's music is the one playing.</summary>
    public static void StopFor(object owner)
    {
        if (IsActive(owner))
            Stop();
    }

    /// <summary>This Windows call rejects paths over about 128 characters, which a long user name can reach.</summary>
    private static string ShortPath(string file)
    {
        var result = new StringBuilder(260);
        int length = GetShortPathNameW(file, result, result.Capacity);
        return length > 0 && length < result.Capacity ? result.ToString() : file;
    }

    private static void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It's in the temp folder, so Windows cleans it up later.
        }
    }

    private static string Query(string item)
    {
        var result = new StringBuilder(64);
        return Send($"status {Alias} {item}", result) == 0 ? result.ToString() : "";
    }

    private static int Send(string command, StringBuilder? result = null) =>
        mciSendStringW(command, result, result?.Capacity ?? 0, IntPtr.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetShortPathNameW(string path, StringBuilder result, int length);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendStringW(string command, StringBuilder? result, int length, IntPtr callback);
}
