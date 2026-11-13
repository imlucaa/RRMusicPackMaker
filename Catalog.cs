using System.Text;

namespace RRMusicPackMaker;

public enum LoopMode { Auto, WholeSong, Custom, FindLoop, NoLoop }

/// <summary>One music file the game can load, plus what the user assigned to it.</summary>
public sealed class TrackRow
{
    public string Category { get; init; } = "";

    /// <summary>The console a track comes from, like SNES or Wii U. Empty for custom tracks and game music.</summary>
    public string Platform { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>The track this one is a version of, when its name was "SNES X > SW2 X". Still findable by search.</summary>
    public string VariantOf { get; init; } = "";

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

    /// <summary>
    /// For one of the 32 original tracks: the file name the original game and CTGP use for its music, like n_Farm_n.
    /// Written as well when the pack is for those. Retro Rewind itself only reads its own numbered file.
    /// </summary>
    public string? OriginalBase { get; init; }

    /// <summary>The final lap file that goes with <see cref="OriginalBase"/>.</summary>
    public string? OriginalFinalBase { get; init; }

    /// <summary>Channels the original game expects in that file.</summary>
    public int OriginalChannels { get; init; } = 2;

    /// <summary>
    /// Set while a pack is built: true when this row writes the original file names. Luigi Circuit and Mario Circuit
    /// share one file, so only the first of them that has a song writes it.
    /// </summary>
    public bool OriginalActive { get; set; }

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

    /// <summary>
    /// When the song starts, in seconds. Above zero, it waits that long in silence first; below zero, it skips that
    /// much of the song's start. For fanfares that the game cuts off, or that should hit a moment later.
    /// </summary>
    public double StartDelay { get; set; }
    public int VolumeDb { get; set; }

    /// <summary>How much faster the song plays. 1 is normal speed.</summary>
    public double Speed { get; set; } = 1;

    /// <summary>Keeps the song's pitch when it's faster. Otherwise the pitch rises with the speed, like nightcore.</summary>
    public bool KeepPitch { get; set; }

    /// <summary>Adds reverb, the usual partner of a slowed song.</summary>
    public bool Reverb { get; set; }

    /// <summary>The song is a finished .brstm from an imported pack, so it's used as it is unless its settings change.</summary>
    public bool KeepFile { get; set; }

    /// <summary>How much faster the final lap file plays. 1.06 matches how the game speeds up its own music.</summary>
    public double FinalSpeed { get; set; } = PackBuilder.GameFinalSpeed;

    /// <summary>Volume for the song picked for the final lap, in dB. Null means the same as the main song.</summary>
    public int? FinalVolumeDb { get; set; }

    /// <summary>Speed for the song picked for the final lap. Zero means the same as the main song.</summary>
    public double FinalSongSpeed { get; set; }
    public bool FinalSongKeepPitch { get; set; }
    public bool FinalSongReverb { get; set; }

    public bool IsAssigned => !string.IsNullOrWhiteSpace(Source);

    /// <summary>Plays during a race or battle, where the game mixes music far below menu level.</summary>
    public bool IsRaceMusic => Category is Catalog.CatRegs or Catalog.CatRetro or Catalog.CatCustom or Catalog.CatBattle or Catalog.CatStock or Catalog.CatStockBattle;
}

public static class Catalog
{
    public const string CatMenu = "Menus";
    public const string CatFanfare = "Fanfares & jingles";
    public const string CatStock = "Stock race music";
    public const string CatStockBattle = "Stock battle music";
    public const string CatRegs = "Regs";
    public const string CatRetro = "Retros";
    public const string CatCustom = "CTs";
    public const string CatBattle = "Battle arenas";
    public const string CatOther = "Other music files";

    /// <summary>How the sidebar is grouped, top to bottom.</summary>
    public static readonly (string Title, string[] Categories)[] Groups =
    [
        ("Tracks", [CatRegs, CatRetro, CatCustom, CatBattle]),
        ("Game music", [CatMenu, CatFanfare]),
        ("Advanced", [CatStock, CatStockBattle, CatOther]),
    ];

    /// <summary>
    /// Categories kept off the sidebar. Their rows stay in the list, because imported packs often use the
    /// stock file names, and those songs still need somewhere to go. They show under All tracks and in search.
    /// </summary>
    public static readonly string[] HiddenFromSidebar = [CatStock, CatStockBattle];

    // Oldest console first. "Wii U" is listed before "Wii" in the lookup below so it matches first.
    private static readonly string[] PlatformOrder = ["SNES", "N64", "GBA", "GCN", "DS", "Wii", "3DS", "Wii U", "Tour", "GP", "SW2", "SW", "RMX", "Beta"];
    private static readonly string[] PlatformLookup = ["Wii U", "SNES", "N64", "GBA", "GCN", "DS", "Wii", "3DS", "Tour", "GP", "SW2", "SW", "RMX", "Beta"];

    // The 32 tracks that came with Mario Kart Wii. A few are spelled differently across versions.
    private static readonly HashSet<string> RegNames = new(new[]
    {
        "Wii Luigi Circuit", "Wii Moo Moo Meadows", "Wii Mushroom Gorge", "Wii Toad's Factory",
        "Wii Mario Circuit", "Wii Coconut Mall", "Wii DK Summit", "Wii Wario's Gold Mine",
        "Wii Daisy Circuit", "Wii Koopa Cape", "Wii Maple Treeway", "Wii Grumble Volcano",
        "Wii Dry Dry Ruins", "Wii Moonview Highway", "Wii Bowser's Castle", "Wii Rainbow Road",
        "GCN Peach Beach", "DS Yoshi Falls", "SNES Ghost Valley 2", "N64 Mario Raceway",
        "N64 Sherbet Land", "GBA Shy Guy Beach", "DS Delfino Square", "GCN Waluigi Stadium",
        "DS Desert Hills", "GBA Bowser Castle 3", "N64 DK's Jungle Parkway", "N64 DK Jungle Parkway",
        "GCN Mario Circuit", "SNES Mario Circuit 3", "DS Peach Gardens", "DS Peach Garden",
        "GCN DK Mountain", "N64 Bowser's Castle",
    }.Select(NameKey));

    internal static string NameKey(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>The track's own name, without any variant part.</summary>
    internal static string MainName(string name)
    {
        int variant = name.IndexOf(" > ", StringComparison.Ordinal);
        if (variant < 0)
            variant = name.IndexOf(" (variant ", StringComparison.Ordinal);
        return variant < 0 ? name : name[..variant];
    }

    /// <summary>The "SW2 X" of "SNES X > SW2 X", when it names a console. Other names after the arrow aren't a separate version.</summary>
    internal static string? VersionName(string name)
    {
        int arrow = name.IndexOf(" > ", StringComparison.Ordinal);
        if (arrow < 0)
            return null;
        string version = name[(arrow + 3)..];
        return PlatformOf(version).Length > 0 ? version : null;
    }

    /// <summary>One of the 32 original tracks. A remake from another game, like SW2's, is a retro even though it replaces one.</summary>
    internal static bool IsReg(string name)
    {
        if (VersionName(name) is { } version && PlatformOf(version) != PlatformOf(MainName(name)))
            return false;
        return RegNames.Contains(NameKey(MainName(name)));
    }

    internal static string PlatformOf(string name)
    {
        foreach (string platform in PlatformLookup)
            if (name.StartsWith(platform + " ", StringComparison.Ordinal))
                return platform;
        return "";
    }

    public static int PlatformIndex(string platform)
    {
        int index = Array.IndexOf(PlatformOrder, platform);
        return index < 0 ? PlatformOrder.Length : index;
    }

    /// <summary>Puts rows in sidebar order. Inside a group: by console, then name, with each track's variants right after it.</summary>
    internal static List<TrackRow> Organize(IEnumerable<TrackRow> rows)
    {
        var categories = Groups.SelectMany(group => group.Categories).ToList();
        var racing = new[] { CatRegs, CatRetro, CatCustom, CatBattle };
        return rows
            .Select((row, index) => (Row: row, Index: index))
            .OrderBy(item => categories.IndexOf(item.Row.Category) is var at && at < 0 ? categories.Count : at)
            .ThenBy(item => racing.Contains(item.Row.Category) ? PlatformIndex(item.Row.Platform) : 0)
            .ThenBy(item => racing.Contains(item.Row.Category) ? MainName(item.Row.Name) : "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Index)
            .Select(item => item.Row)
            .ToList();
    }

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

    // Names follow the mkwiiki file list. Where they play comes from it and from the mariokartwii.com file
    // thread, which says the "mission" files are used for time trials, battles and competitions.
    private static readonly (string Name, string File, string About)[] Menus =
    [
        ("Title screen / main menu", "title_bg", "Plays on the title screen and the main menu."),
        ("Offline menus", "offline_bg", "Plays in the menus for single player and local multiplayer."),
        ("Wi-Fi menu", "wifi_globe_bg", "Plays in the online menus, with the globe."),
        ("Wi-Fi lobby / voting", "wifi_lobby_bg", "Plays in an online room while everyone votes for the next track."),
        ("Options menu", "o_Option_32", "Plays in the Options menu."),
        ("Wi-Fi waiting to join / ghost replay", "o_Wi-Fi_waiting32", "Plays while waiting to join an online race, and while watching a ghost replay."),
    ];

    private static readonly (string Name, string File, bool Short, string About)[] Fanfares =
    [
        ("1st place fanfare + results", "o_FanfareGP1_32", false, "Plays when you finish 1st, and carries on through the results."),
        ("1st place fanfare only", "o_FanfareGP1_only32", true, "The short jingle as you cross the line in 1st."),
        ("2nd-5th place fanfare + results", "o_FanfareGP2_32", false, "Plays when you finish 2nd to 5th, and carries on through the results."),
        ("2nd-5th place fanfare only", "o_FanfareGP2_only32", true, "The short jingle as you cross the line in 2nd to 5th."),
        ("6th-last place fanfare + results", "o_FanfareGPdame_32", false, "Plays when you finish 6th or lower, and carries on through the results."),
        ("6th-last place fanfare only", "o_FanfareGPdame_only32", true, "The short jingle as you cross the line in 6th or lower."),
        ("Battle win fanfare + results", "o_FanfareTA1st_32", false, "Plays when you win a battle as the top player, and carries on through the results."),
        ("Battle win fanfare only", "o_FanfareTA1st_only32", true, "The short jingle when you win a battle as the top player."),
        ("Battle draw + results", "o_FanfareBTdraw_32", false, "Plays when a battle ends in a draw, and carries on through the results."),
        ("Mission win fanfare + results", "o_FanfareMIwin_32", false, "Plays when you finish a time trial, and when your team wins a battle but you aren't the top player. Named for missions, which Mario Kart Wii doesn't have."),
        ("Mission win fanfare only", "o_FanfareMIwin_only32", true, "The short jingle when you finish a time trial. Named for missions, which Mario Kart Wii doesn't have."),
        ("Mission lose fanfare + results", "o_FanfareMIlose_32", false, "Plays when you lose a battle, and carries on through the results. Named for missions, which Mario Kart Wii doesn't have."),
        ("Mission lose results only", "o_FanfareMIlose_only32", false, "Plays when you lose a battle. Named for missions, which Mario Kart Wii doesn't have."),
        ("Mission boss win + results", "o_FanfareMIWinBoss_32", false, "Plays when you win a competition, and carries on through the results. Named for missions, which Mario Kart Wii doesn't have."),
        ("Race intro", "o_Crs_In_Fan", true, "Plays while the camera flies over the track before an offline race."),
        ("Wi-Fi race intro", "o_Crs_In_Fan_Wifi", true, "Plays while the camera flies over the track before an online race."),
        ("Battle intro", "o_Crs_In_Fan_battle", true, "Plays while the camera flies over the arena before a battle."),
        ("Mission intro", "o_Crs_In_Fan_mission", true, "Left over from the unused mission mode. Where it still plays isn't known for sure."),
        ("Race start fanfare", "o_Start32_fan", true, "The short jingle as a race starts."),
        ("Time trial start fanfare", "o_Start2_32_fan", true, "The short jingle as a time trial starts."),
        ("Trophy presentation, main", "o_hyousyou_winningrun", false, "The trophy ceremony after a Grand Prix, the full version."),
        ("Trophy presentation, normal", "o_hyousyou_normal", false, "The trophy ceremony after a Grand Prix, a shorter version."),
        ("Trophy presentation, short", "o_hyousyou_cut", false, "The trophy ceremony after a Grand Prix, the shortest version."),
        ("Trophy presentation, lose", "o_hyousyou_lose", false, "Plays after a Grand Prix when you didn't win a trophy."),
        ("Ending theme (credits)", "o_Ending_PartA_32", false, "Plays during the ending credits."),
        ("Unlocked character / cup / kart", "strm_me", true, "The jingle when you unlock a character, cup or kart."),
    ];

    /// <summary>Builds every row, including every track of the Retro Rewind install when one is given.</summary>
    public static List<TrackRow> Build(string? rrRoot, out string message)
    {
        var rows = new List<TrackRow>();
        foreach (var (name, file, about) in Menus)
            rows.Add(new TrackRow { Category = CatMenu, Name = name, FileBase = file, FixedRate = file.StartsWith("o_") ? 32000 : null, Note = about });
        foreach (var (name, file, isShort, about) in Fanfares)
            rows.Add(new TrackRow { Category = CatFanfare, Name = name, FileBase = file, FixedRate = 32000, DefaultNoLoop = isShort, Note = about });

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

        // Without an install, the list built into the app stands in for it, so a pack
        // for a real Wii needs nothing from Retro Rewind or Wheel Wizard on this PC.
        var builtIn = HasTrackList(rrRoot) ? null : BuiltInTracks.Load();
        if (builtIn == null && !HasTrackList(rrRoot))
        {
            message = "Retro Rewind folder not found. Showing stock slots, menus and fanfares only.";
            return rows;
        }

        var strmFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in builtIn?.MusicFiles ?? InstalledMusicFiles(rrRoot!))
            strmFiles[name] = name;

        var claimed = new HashSet<string>(rows.Select(r => r.FileBase), StringComparer.OrdinalIgnoreCase);
        var usage = new Dictionary<int, List<string>>();
        int trackCount = 0;
        // Pulsar numbers every track in one list: retro tracks, then custom tracks, then battle arenas, each group
        // filling whole cups of four. A track's music can be named by that number (strm/344_N.brstm) as well as by
        // its file name, and battle arenas have been seen to need the number, so their songs are written both ways.
        int numberOffset = 0;
        foreach (var (config, category) in new[] { ("ConfigRT.pul", CatRetro), ("ConfigCT.pul", CatCustom), ("ConfigBT.pul", CatBattle) })
        {
            int mainIndex = 0;
            IEnumerable<PulsarConfig.Track> tracks;
            if (builtIn != null)
            {
                tracks = builtIn.Tracks.GetValueOrDefault(config, []);
            }
            else
            {
                string path = Path.Combine(rrRoot!, "Binaries", config);
                if (!File.Exists(path))
                    continue;
                tracks = PulsarConfig.Read(path);
            }
            foreach (var track in tracks)
            {
                int? number = track.Variant == 0 ? numberOffset + mainIndex++ : null;
                string stemN = FindExisting(strmFiles, track.FileStem + "_N", track.FileStem + "_n") ?? track.FileStem + "_N";
                bool hasOwn = strmFiles.ContainsKey(stemN);
                if (!hasOwn && track.Variant == 0)
                {
                    if (!usage.TryGetValue(track.MusicSlot, out var list))
                        usage[track.MusicSlot] = list = [];
                    list.Add(track.Name);
                }
                string final = FindExisting(strmFiles, track.FileStem + "_Final", track.FileStem + "_F") ?? track.FileStem + "_Final";
                string? version = VersionName(track.Name);
                string shownName = version ?? track.Name;
                string variantOf = version != null ? MainName(track.Name) : "";
                string variantNote = variantOf.Length > 0 ? $"Version of {variantOf}. " : "";
                string rowCategory = category == CatRetro && IsReg(track.Name) ? CatRegs : category;
                var original = rowCategory == CatRegs && track.Variant == 0
                    ? StockRace.FirstOrDefault(slot => slot.Slots.Contains(track.MusicSlot))
                    : null;
                var row = new TrackRow
                {
                    Category = rowCategory,
                    OriginalBase = original?.File,
                    OriginalFinalBase = original == null ? null : FinalNameOf(original.File),
                    OriginalChannels = original?.Channels ?? 2,
                    Platform = PlatformOf(MainName(shownName)),
                    Name = shownName,
                    VariantOf = variantOf,
                    FileBase = stemN,
                    AliasBases = category == CatBattle && number != null && !hasOwn ? [$"{number}_N"] : [],
                    PulsarFinalBase = final,
                    Note = variantNote + (hasOwn ? "Has its own music in Retro Rewind."
                        : track.Variant > 0 ? "Variant. Uses the main track's music unless you set a song here."
                        : stockBySlot.TryGetValue(track.MusicSlot, out var stock)
                            ? $"No own music in Retro Rewind. Now plays stock slot: {stock.Name}."
                            : "No own music in Retro Rewind."),
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
            numberOffset += (mainIndex + 3) / 4 * 4;
        }

        foreach (var (slot, stockRow) in stockBySlot)
            if (usage.TryGetValue(slot, out var names))
                stockRow.Note = $"Also plays on {names.Count} RR track(s) without own music: "
                    + string.Join(", ", names.Take(6)) + (names.Count > 6 ? ", ..." : "");

        foreach (var stem in strmFiles.Values.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            if (claimed.Add(stem))
                rows.Add(new TrackRow { Category = CatOther, Name = stem, FileBase = stem, Note = "Music file found in the Retro Rewind strm folder." });

        message = builtIn != null
            ? $"Using the built-in list of {trackCount} Retro Rewind {builtIn.Version} tracks. If you have a newer version, pick its folder in Settings."
            : $"Loaded {trackCount} Retro Rewind track music slots.";
        return Organize(rows);
    }

    /// <summary>True when a folder has a Retro Rewind track list to read.</summary>
    private static bool HasTrackList(string? rrRoot) =>
        !string.IsNullOrWhiteSpace(rrRoot) && new[] { "ConfigRT.pul", "ConfigCT.pul", "ConfigBT.pul" }.Any(config => File.Exists(Path.Combine(rrRoot, "Binaries", config)));

    private static IEnumerable<string> InstalledMusicFiles(string rrRoot)
    {
        string strm = Path.Combine(rrRoot, "strm");
        return Directory.Exists(strm) ? Directory.GetFiles(strm, "*.brstm").Select(file => Path.GetFileNameWithoutExtension(file)) : [];
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
        if (Environment.GetEnvironmentVariable("RRMUSIC_RR") is { Length: > 0 } custom)
            return custom;
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
        return PackBuilder.FindSdCards().FirstOrDefault();
    }
}

/// <summary>
/// A copy of Retro Rewind's track names and music file names, with no audio in it. It lets the app list every
/// track on a PC with no install. Update it with the track-list command when Retro Rewind adds tracks.
/// </summary>
public sealed class BuiltInTracks
{
    private const string ResourceName = "RetroRewindTracks.tsv";

    public string Version { get; private init; } = "";
    public Dictionary<string, List<PulsarConfig.Track>> Tracks { get; } = [];
    public List<string> MusicFiles { get; } = [];

    /// <summary>Returns null only if the list is missing from the app, and then the stock tracks still show.</summary>
    public static BuiltInTracks? Load()
    {
        using var stream = typeof(BuiltInTracks).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        var list = Parse(reader.ReadToEnd());
        return list.Tracks.Count > 0 ? list : null;
    }

    internal static BuiltInTracks Parse(string text)
    {
        string version = "";
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('\t')).ToList();
        foreach (var parts in lines.Where(parts => parts[0] == "version" && parts.Length > 1))
            version = parts[1];
        var list = new BuiltInTracks { Version = version };
        foreach (var parts in lines)
        {
            if (parts[0] == "music" && parts.Length > 1)
            {
                list.MusicFiles.Add(parts[1]);
            }
            else if (parts[0] == "track" && parts.Length >= 6)
            {
                if (!list.Tracks.TryGetValue(parts[1], out var tracks))
                    list.Tracks[parts[1]] = tracks = [];
                tracks.Add(new PulsarConfig.Track(parts[2], parts[3], int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
        return list;
    }

    /// <summary>Writes the list from a real install.</summary>
    public static string Write(string rrRoot)
    {
        var text = new StringBuilder();
        string versionFile = Path.Combine(rrRoot, "version.txt");
        text.Append("version\t").Append(File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "").Append('\n');
        foreach (string config in new[] { "ConfigRT.pul", "ConfigCT.pul", "ConfigBT.pul" })
            foreach (var track in PulsarConfig.Read(Path.Combine(rrRoot, "Binaries", config)))
                text.Append($"track\t{config}\t{track.Name}\t{track.FileStem}\t{track.Variant}\t{track.MusicSlot}\n");
        foreach (string file in Directory.GetFiles(Path.Combine(rrRoot, "strm"), "*.brstm").Order(StringComparer.OrdinalIgnoreCase))
            text.Append("music\t").Append(Path.GetFileNameWithoutExtension(file)).Append('\n');
        return text.ToString();
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
