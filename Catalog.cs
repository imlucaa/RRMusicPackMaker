using System.Text;

namespace RRMusicPackMaker;

public enum LoopMode { Auto, WholeSong, Custom, FindLoop, NoLoop }

/// <summary>One music file the game can load, plus what the user assigned to it.</summary>
public sealed class TrackRow
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>File name without extension, e.g. n_Circuit32_n or 104_N.</summary>
    public string FileBase { get; init; } = "";

    /// <summary>More names written with the same audio. Sources disagree on some stock names.</summary>
    public string[] AliasBases { get; init; } = [];

    /// <summary>Stock slot final lap file name. Null if the slot has none.</summary>
    public string? StockFinalBase { get; init; }

    /// <summary>Retro Rewind final lap file name. Null if the row is not a Pulsar track.</summary>
    public string? PulsarFinalBase { get; init; }

    /// <summary>Channels the stock game expects in this slot.</summary>
    public int StockChannels { get; init; } = 2;

    public int? FixedRate { get; init; }
    public bool DefaultNoLoop { get; init; }
    public string Note { get; set; } = "";

    /// <summary>Retro Rewind's own files that keep playing unless this row replaces them.</summary>
    public List<string> ExistingFinalFiles { get; } = [];

    // User assignment.
    public string Source { get; set; } = "";
    public string FinalSource { get; set; } = "";
    public string Song { get; set; } = "";
    public string Credits { get; set; } = "";
    public LoopMode Loop { get; set; } = LoopMode.Auto;
    public string LoopStart { get; set; } = "";
    public string LoopEnd { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>Seconds of blend at the loop jump. Zero keeps the jump exact.</summary>
    public double Crossfade { get; set; }
    public int VolumeDb { get; set; }

    public bool IsAssigned => !string.IsNullOrWhiteSpace(Source);
}

public static class Catalog
{
    private const string CatMenu = "Menus";
    private const string CatFanfare = "Fanfares & jingles";
    private const string CatStock = "Stock race music slots";
    private const string CatStockBattle = "Stock battle music slots";
    private const string CatRetro = "RR retro tracks";
    private const string CatCustom = "RR custom tracks";
    private const string CatBattle = "RR battle arenas";
    private const string CatOther = "Other RR music files";

    private sealed record StockSlot(int[] Slots, string Name, string File, int Channels = 2);

    // Slot numbers checked against ConfigRT.pul and ConfigBT.pul of Retro Rewind 6.13.4.
    private static readonly StockSlot[] StockRace =
    [
        new([0x08, 0x00], "Luigi Circuit / Mario Circuit (shared file)", "n_Circuit32_n"),
        new([0x01], "Moo Moo Meadows", "n_Farm_n"),
        new([0x02], "Mushroom Gorge", "n_Kinoko_n"),
        new([0x04], "Toad's Factory", "STRM_N_FACTORY_N", 4),
        new([0x05], "Coconut Mall", "n_Shopping32_n"),
        new([0x06], "DK Summit", "n_Snowboard32_n"),
        new([0x07], "Wario's Gold Mine", "STRM_N_TRUCK_N", 4),
        new([0x09], "Daisy Circuit", "n_Daisy32_n"),
        new([0x0F], "Koopa Cape", "STRM_N_WATER_N", 8),
        new([0x0B], "Maple Treeway", "n_maple_n"),
        new([0x03], "Grumble Volcano", "n_Volcano32_n"),
        new([0x0E], "Dry Dry Ruins", "STRM_N_DESERT_N", 4),
        new([0x0A], "Moonview Highway", "STRM_N_RIDGEHIGHWAY_N", 4),
        new([0x0C], "Bowser's Castle", "STRM_N_KOOPA_N", 8),
        new([0x0D], "Rainbow Road", "n_Rainbow32_n"),
        new([0x10], "GCN Peach Beach", "r_GC_Beach32_n"),
        new([0x14], "DS Yoshi Falls", "r_DS_Jungle32_n"),
        new([0x19], "SNES Ghost Valley 2", "r_SFC_Obake32_n"),
        new([0x1A], "N64 Mario Raceway", "r_64_Circuit32_n"),
        new([0x1B], "N64 Sherbet Land", "r_64_Sherbet32_n"),
        new([0x1F], "GBA Shy Guy Beach", "r_AGB_Beach32_n"),
        new([0x17], "DS Delfino Square", "r_DS_Town32_n"),
        new([0x12], "GCN Waluigi Stadium", "r_GC_Stadium32_n"),
        new([0x15], "DS Desert Hills", "r_DS_Desert32_n"),
        new([0x1E], "GBA Bowser Castle 3", "r_AGB_Kuppa32_n"),
        new([0x1D], "N64 DK's Jungle Parkway", "r_64_Jungle32_n"),
        new([0x11], "GCN Mario Circuit", "r_GC_Circuit32_n"),
        new([0x18], "SNES Mario Circuit 3", "r_SFC_Circuit32_n"),
        new([0x16], "DS Peach Gardens", "r_DS_Garden32_n"),
        new([0x13], "GCN DK Mountain", "r_GC_Mountain32_n"),
        new([0x1C], "N64 Bowser's Castle", "r_64_Kuppa32_n"),
    ];

    private static readonly StockSlot[] StockBattle =
    [
        new([0x20], "Delfino Pier", "n_venice_n"),
        new([0x21], "Block Plaza", "n_block_n"),
        new([0x22], "Chain Chomp Wheel", "n_casino_n"),
        new([0x23], "Funky Stadium", "n_skate_n"),
        new([0x24], "Thwomp Desert", "n_ryuusa_n"),
        new([0x25], "GCN Cookie Land", "r_GC_Battle32_n"),
        new([0x26], "DS Twilight House", "r_ds_battle_n"),
        new([0x27], "SNES Battle Course 4", "r_sfc_battle_n"),
        new([0x28], "GBA Battle Course 3", "r_agb_battle_n"),
        new([0x29], "N64 Skyscraper", "r_64_battle_n"),
    ];

    private static readonly (string Name, string File)[] Menus =
    [
        ("Title screen / main menu", "title_bg"),
        ("Offline menus", "offline_bg"),
        ("Wi-Fi menu", "wifi_globe_bg"),
        ("Wi-Fi lobby / voting", "wifi_lobby_bg"),
        ("Options menu", "o_Option_32"),
        ("Wi-Fi waiting to join / ghost replay", "o_Wi-Fi_waiting32"),
    ];

    private static readonly (string Name, string File, bool Short)[] Fanfares =
    [
        ("1st place fanfare + results", "o_FanfareGP1_32", false),
        ("1st place fanfare only", "o_FanfareGP1_only32", true),
        ("2nd-5th place fanfare + results", "o_FanfareGP2_32", false),
        ("2nd-5th place fanfare only", "o_FanfareGP2_only32", true),
        ("6th-last place fanfare + results", "o_FanfareGPdame_32", false),
        ("6th-last place fanfare only", "o_FanfareGPdame_only32", true),
        ("Battle win fanfare + results", "o_FanfareTA1st_32", false),
        ("Battle win fanfare only", "o_FanfareTA1st_only32", true),
        ("Battle draw + results", "o_FanfareBTdraw_32", false),
        ("Mission win fanfare + results", "o_FanfareMIwin_32", false),
        ("Mission win fanfare only", "o_FanfareMIwin_only32", true),
        ("Mission lose fanfare + results", "o_FanfareMIlose_32", false),
        ("Mission lose results only", "o_FanfareMIlose_only32", false),
        ("Mission boss win + results", "o_FanfareMIWinBoss_32", false),
        ("Race intro", "o_Crs_In_Fan", true),
        ("Wi-Fi race intro", "o_Crs_In_Fan_Wifi", true),
        ("Battle intro", "o_Crs_In_Fan_battle", true),
        ("Mission intro", "o_Crs_In_Fan_mission", true),
        ("Race start fanfare", "o_Start32_fan", true),
        ("Time trial start fanfare", "o_Start2_32_fan", true),
        ("Trophy presentation, main", "o_hyousyou_winningrun", false),
        ("Trophy presentation, normal", "o_hyousyou_normal", false),
        ("Trophy presentation, short", "o_hyousyou_cut", false),
        ("Trophy presentation, lose", "o_hyousyou_lose", false),
        ("Ending theme (credits)", "o_Ending_PartA_32", false),
        ("Unlocked character / cup / kart", "strm_me", true),
    ];

    /// <summary>Builds every row, including every track of the Retro Rewind install when one is given.</summary>
    public static List<TrackRow> Build(string? rrRoot, out string message)
    {
        var rows = new List<TrackRow>();
        foreach (var (name, file) in Menus)
            rows.Add(new TrackRow { Category = CatMenu, Name = name, FileBase = file, FixedRate = file.StartsWith("o_") ? 32000 : null });
        foreach (var (name, file, isShort) in Fanfares)
            rows.Add(new TrackRow { Category = CatFanfare, Name = name, FileBase = file, FixedRate = 32000, DefaultNoLoop = isShort });

        var stockBySlot = new Dictionary<int, TrackRow>();
        foreach (var slot in StockRace)
            AddStock(rows, stockBySlot, slot, CatStock);
        foreach (var slot in StockBattle)
            AddStock(rows, stockBySlot, slot, CatStockBattle);
        rows.Add(new TrackRow
        {
            Category = CatStockBattle,
            Name = "Galaxy Colosseum",
            FileBase = "n_BossMI",
            // The wiki lists n_BossMI_32, the spreadsheet and the file name thread list n_BossMI.
            AliasBases = ["n_BossMI_32"],
        });

        var strmFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rrRoot) || !Directory.Exists(rrRoot))
        {
            message = "Retro Rewind folder not found. Showing stock slots, menus and fanfares only.";
            return rows;
        }

        string strm = Path.Combine(rrRoot, "strm");
        if (Directory.Exists(strm))
            foreach (var file in Directory.GetFiles(strm, "*.brstm"))
                strmFiles[Path.GetFileNameWithoutExtension(file)] = Path.GetFileNameWithoutExtension(file);

        var claimed = new HashSet<string>(rows.Select(r => r.FileBase), StringComparer.OrdinalIgnoreCase);
        var usage = new Dictionary<int, List<string>>();
        int trackCount = 0;
        foreach (var (config, category) in new[] { ("ConfigRT.pul", CatRetro), ("ConfigCT.pul", CatCustom), ("ConfigBT.pul", CatBattle) })
        {
            string path = Path.Combine(rrRoot, "Binaries", config);
            if (!File.Exists(path))
                continue;
            foreach (var track in PulsarConfig.Read(path))
            {
                string stemN = FindExisting(strmFiles, track.FileStem + "_N", track.FileStem + "_n") ?? track.FileStem + "_N";
                bool hasOwn = strmFiles.ContainsKey(stemN);
                if (!hasOwn && track.Variant == 0)
                {
                    if (!usage.TryGetValue(track.MusicSlot, out var list))
                        usage[track.MusicSlot] = list = [];
                    list.Add(track.Name);
                }
                string final = FindExisting(strmFiles, track.FileStem + "_Final", track.FileStem + "_F") ?? track.FileStem + "_Final";
                var row = new TrackRow
                {
                    Category = category,
                    Name = track.Name,
                    FileBase = stemN,
                    PulsarFinalBase = final,
                    Note = hasOwn ? "Has its own music in Retro Rewind."
                        : track.Variant > 0 ? "Variant. Uses the main track's music unless you set a song here."
                        : stockBySlot.TryGetValue(track.MusicSlot, out var stock)
                            ? $"No own music in Retro Rewind. Now plays stock slot: {stock.Name}."
                            : "No own music in Retro Rewind.",
                };
                if (strmFiles.ContainsKey(final))
                    row.ExistingFinalFiles.Add(strmFiles[final]);
                if (claimed.Add(row.FileBase))
                {
                    rows.Add(row);
                    trackCount++;
                }
                claimed.Add(final);
            }
        }

        foreach (var (slot, stockRow) in stockBySlot)
            if (usage.TryGetValue(slot, out var names))
                stockRow.Note = $"Also plays on {names.Count} RR track(s) without own music: "
                    + string.Join(", ", names.Take(6)) + (names.Count > 6 ? ", ..." : "");

        foreach (var stem in strmFiles.Values.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            if (claimed.Add(stem))
                rows.Add(new TrackRow { Category = CatOther, Name = stem, FileBase = stem, Note = "Music file found in the Retro Rewind strm folder." });

        message = trackCount == 0
            ? $"No track list found in {Path.Combine(rrRoot, "Binaries")}. Showing stock slots only."
            : $"Loaded {trackCount} Retro Rewind track music slots.";
        return rows;
    }

    private static string? FindExisting(Dictionary<string, string> files, params string[] names)
    {
        foreach (var name in names)
            if (files.TryGetValue(name, out var real))
                return real;
        return null;
    }

    private static void AddStock(List<TrackRow> rows, Dictionary<int, TrackRow> bySlot, StockSlot slot, string category)
    {
        var row = new TrackRow
        {
            Category = category,
            Name = slot.Name,
            FileBase = slot.File,
            StockFinalBase = FinalNameOf(slot.File),
            StockChannels = slot.Channels,
        };
        rows.Add(row);
        foreach (int id in slot.Slots)
            bySlot[id] = row;
    }

    /// <summary>The final lap name swaps the last _n for _f, keeping its case.</summary>
    public static string FinalNameOf(string normalBase)
    {
        if (normalBase.EndsWith("_n", StringComparison.Ordinal))
            return normalBase[..^2] + "_f";
        if (normalBase.EndsWith("_N", StringComparison.Ordinal))
            return normalBase[..^2] + "_F";
        return normalBase;
    }

    /// <summary>Finds Retro Rewind on this PC. Returns the first folder holding a track list.</summary>
    public static string? DetectRetroRewind()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] dolphinRoots = [Path.Combine(appData, "Dolphin Emulator"), Path.Combine(docs, "Dolphin Emulator")];
        foreach (var root in dolphinRoots)
        {
            string[] candidates =
            [
                Path.Combine(root, "Load", "Riivolution", "WheelWizard", "RetroRewind6"),
                Path.Combine(root, "Load", "Riivolution", "RetroRewind6"),
                Path.Combine(root, "Load", "WiiSDSync", "RetroRewind6"),
            ];
            foreach (var candidate in candidates)
                if (File.Exists(Path.Combine(candidate, "Binaries", "ConfigRT.pul")))
                    return candidate;
        }
        return null;
    }
}

/// <summary>Reads track names, music slots and file names from a Pulsar Config*.pul file.</summary>
public static class PulsarConfig
{
    public sealed record Track(string Name, string FileStem, int Variant, int MusicSlot);

    private const int BmgTracks = 0x20000;
    private const int BmgVariants = 0x420000;

    public static List<Track> Read(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 0x14 || Encoding.ASCII.GetString(data, 0, 4) != "PULS")
            throw new InvalidDataException($"{path} is not a Pulsar config file.");

        int cups = (int)U32(data, 0x0C);
        int bmg = (int)U32(data, 0x10);
        if (Encoding.ASCII.GetString(data, cups, 4) != "CUPS")
            throw new InvalidDataException($"{path} has no CUPS section.");

        int trackCount = U16(data, cups + 0x0C) * 4;
        int table = cups + 0x1C;
        var messages = ReadBmg(data, bmg);

        var result = new List<Track>();
        foreach (var (key, stem) in ReadFileNames(data, bmg))
        {
            int index = key & 0x0FFF;
            int variant = key >> 12;
            if (index >= trackCount || stem == "***" || stem.Length == 0)
                continue;

            int musicSlot = data[table + index * 8 + 1];
            messages.TryGetValue(BmgTracks + index, out string? name);
            name = Clean(name) ?? $"Track {index}";
            if (variant > 0)
            {
                messages.TryGetValue(BmgVariants + (index << 4) + variant, out string? variantName);
                variantName = Clean(variantName);
                name = variantName != null && variantName != name ? $"{name} > {variantName}" : $"{name} (variant {variant})";
            }
            result.Add(new Track(name, stem, variant, musicSlot));
        }
        return result;
    }

    private static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        name = name.Trim();
        return name.EndsWith('*') ? name[..^1].TrimEnd() : name;
    }

    private static IEnumerable<(int Key, string Stem)> ReadFileNames(byte[] data, int bmg)
    {
        int size = (int)U32(data, bmg + 8);
        int start = bmg + size;
        if (start + 4 > data.Length || Encoding.ASCII.GetString(data, start, 4) != "FILE")
            yield break;

        string text = Encoding.Latin1.GetString(data, start, data.Length - start);
        foreach (string raw in text.Split('\n').Skip(1))
        {
            string line = raw.Trim();
            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            int pipe = line.IndexOf('|', equals);
            if (pipe < 0)
                continue;
            if (int.TryParse(line[..equals], System.Globalization.NumberStyles.HexNumber, null, out int key))
                yield return (key, line[(equals + 1)..pipe].Trim());
        }
    }

    private static Dictionary<int, string> ReadBmg(byte[] data, int bmg)
    {
        var result = new Dictionary<int, string>();
        if (Encoding.ASCII.GetString(data, bmg, 8) != "MESGbmg1")
            return result;

        int blockCount = (int)U32(data, bmg + 0x0C);
        int offset = bmg + 0x20;
        int inf = -1, dat = -1, mid = -1;
        for (int i = 0; i < blockCount; i++)
        {
            string magic = Encoding.ASCII.GetString(data, offset, 4);
            if (magic == "INF1") inf = offset;
            else if (magic == "DAT1") dat = offset;
            else if (magic == "MID1") mid = offset;
            offset += (int)U32(data, offset + 4);
        }
        if (inf < 0 || dat < 0 || mid < 0)
            return result;

        int entrySize = U16(data, inf + 0x0A);
        int count = U16(data, mid + 0x08);
        for (int i = 0; i < count; i++)
        {
            int id = (int)U32(data, mid + 0x10 + i * 4);
            int textOffset = (int)U32(data, inf + 0x10 + i * entrySize);
            result[id] = ReadString(data, dat + 8 + textOffset);
        }
        return result;
    }

    private static string ReadString(byte[] data, int cursor)
    {
        var text = new StringBuilder();
        while (cursor + 1 < data.Length)
        {
            int unit = U16(data, cursor);
            if (unit == 0)
                break;
            if (unit == 0x1A)
            {
                if (cursor + 2 >= data.Length || data[cursor + 2] == 0)
                    break;
                cursor += data[cursor + 2];
                continue;
            }
            text.Append((char)unit);
            cursor += 2;
        }
        return text.ToString();
    }

    private static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    private static uint U32(byte[] d, int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
}
