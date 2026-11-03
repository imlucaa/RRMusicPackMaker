using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRMusicPackMaker;

/// <summary>A piece of a song on the timeline. Start and End are samples at <see cref="EditTimeline.Rate"/>.</summary>
public sealed record EditClip(string Source, long Start, long End)
{
    [JsonIgnore]
    public long Length => End - Start;

    [JsonIgnore]
    public string Name => SongLibrary.DisplayName(Source);
}

/// <summary>The song being edited: parts played one after another. Every change makes a new list, which keeps undo simple.</summary>
public static class EditTimeline
{
    public const int Rate = 44100;

    // A part shorter than this would only be a click.
    internal const long ShortestPart = Rate / 100;

    public static long Length(IReadOnlyList<EditClip> clips) => clips.Sum(clip => clip.Length);

    /// <summary>The part playing at a position, or -1 when there are no parts.</summary>
    public static int ClipAt(IReadOnlyList<EditClip> clips, long position)
    {
        long end = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            end += clips[i].Length;
            if (position < end)
                return i;
        }
        return clips.Count - 1;
    }

    public static long StartOf(IReadOnlyList<EditClip> clips, int index) => clips.Take(index).Sum(clip => clip.Length);

    /// <summary>Cuts the part at this position in two. Nothing changes if that would leave a part too short to hear.</summary>
    public static List<EditClip> SplitAt(IReadOnlyList<EditClip> clips, long position)
    {
        var result = clips.ToList();
        int index = ClipAt(clips, position);
        if (index < 0)
            return result;
        long inside = position - StartOf(clips, index);
        var clip = clips[index];
        if (inside < ShortestPart || clip.Length - inside < ShortestPart)
            return result;
        result[index] = clip with { End = clip.Start + inside };
        result.Insert(index + 1, clip with { Start = clip.Start + inside });
        return result;
    }

    /// <summary>Removes the part's audio before this position. Other parts stay. Nothing changes if too little would be left.</summary>
    public static List<EditClip> CropStart(IReadOnlyList<EditClip> clips, long position)
    {
        var result = clips.ToList();
        int index = ClipAt(clips, position);
        if (index < 0)
            return result;
        long inside = position - StartOf(clips, index);
        var clip = clips[index];
        if (inside > 0 && inside < clip.Length && clip.Length - inside >= ShortestPart)
            result[index] = clip with { Start = clip.Start + inside };
        return result;
    }

    /// <summary>Removes the part's audio after this position. Other parts stay. Nothing changes if too little would be left.</summary>
    public static List<EditClip> CropEnd(IReadOnlyList<EditClip> clips, long position)
    {
        var result = clips.ToList();
        int index = ClipAt(clips, position);
        if (index < 0)
            return result;
        long inside = position - StartOf(clips, index);
        var clip = clips[index];
        if (inside >= ShortestPart && inside < clip.Length)
            result[index] = clip with { End = clip.Start + inside };
        return result;
    }

    /// <summary>
    /// Makes every part the same length, so merged songs each get a fair turn. Parts are cut from their end.
    /// No part can grow, so the length is never more than the shortest part has.
    /// </summary>
    public static List<EditClip> MakeEqual(IReadOnlyList<EditClip> clips, long? wanted = null)
    {
        if (clips.Count == 0)
            return [];
        long length = Math.Min(clips.Min(clip => clip.Length), wanted ?? long.MaxValue);
        return clips.Select(clip => clip with { End = clip.Start + length }).ToList();
    }

    /// <summary>Moves a part so it plays just before the part now at <paramref name="insertBefore"/>.</summary>
    public static List<EditClip> Move(IReadOnlyList<EditClip> clips, int from, int insertBefore)
    {
        var result = clips.ToList();
        var clip = result[from];
        result.RemoveAt(from);
        result.Insert(insertBefore > from ? insertBefore - 1 : insertBefore, clip);
        return result;
    }

    /// <summary>The parts that play during this stretch of the timeline.</summary>
    public static List<EditClip> Slice(IReadOnlyList<EditClip> clips, long from, long length)
    {
        var result = new List<EditClip>();
        long start = 0;
        foreach (var clip in clips)
        {
            long end = start + clip.Length;
            long first = Math.Max(from, start);
            long last = Math.Min(from + length, end);
            if (last > first)
                result.Add(clip with { Start = clip.Start + first - start, End = clip.Start + last - start });
            start = end;
        }
        return result;
    }

    /// <summary>Joins the parts into one piece of audio, blending each join by up to this many samples.</summary>
    public static short[][] Render(IReadOnlyList<EditClip> clips, Func<string, short[][]> audio, int blend = 0)
    {
        var parts = clips.Select(clip =>
        {
            var channels = audio(clip.Source);
            int start = (int)Math.Clamp(clip.Start, 0, channels[0].Length);
            int end = (int)Math.Clamp(clip.End, start, channels[0].Length);
            return (Channels: channels, Start: start, Length: end - start);
        }).ToList();

        // A blend can't use more than half of a part, so each part keeps a middle of its own.
        var fades = new int[parts.Count];
        for (int i = 1; i < parts.Count; i++)
            fades[i] = Math.Min(blend, Math.Min(parts[i - 1].Length, parts[i].Length) / 2);

        int total = parts.Sum(part => part.Length) - fades.Sum();
        short[][] output = [new short[total], new short[total]];
        int position = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            position -= fades[i];
            for (int channel = 0; channel < 2; channel++)
            {
                var from = part.Channels[channel];
                var to = output[channel];
                for (int s = 0; s < fades[i]; s++)
                {
                    double angle = (s + 0.5) / fades[i] * Math.PI / 2;
                    double mixed = to[position + s] * Math.Cos(angle) + from[part.Start + s] * Math.Sin(angle);
                    to[position + s] = (short)Math.Clamp(Math.Round(mixed), short.MinValue, short.MaxValue);
                }
                Array.Copy(from, part.Start + fades[i], to, position + fades[i], part.Length - fades[i]);
            }
            position += part.Length;
        }
        return output;
    }

    /// <summary>A path for a saved song that never replaces a file already there.</summary>
    public static string UniquePath(string folder, string name)
    {
        string safe = PackBuilder.SafeName(name);
        string path = Path.Combine(folder, safe + ".wav");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{safe} ({i}).wav");
        return path;
    }
}

/// <summary>What the Editor tab was working on, so closing the app doesn't lose it.</summary>
public sealed class EditorState
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<EditClip> Clips { get; set; } = [];
    public string Name { get; set; } = "";
    public int Blend { get; set; }

    /// <summary>Which of the speed choices the saved song gets.</summary>
    public int Speed { get; set; }

    private static string PathOnDisk => Path.Combine(AppStorage.Folder, "editor.json");

    public static EditorState Load()
    {
        try
        {
            if (File.Exists(PathOnDisk))
                return JsonSerializer.Deserialize<EditorState>(File.ReadAllText(PathOnDisk), Json) ?? new EditorState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged file only costs the unsaved edit, so start empty.
        }
        return new EditorState();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the edit shouldn't interrupt making it.
        }
    }
}
