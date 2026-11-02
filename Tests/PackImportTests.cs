using System.IO.Compression;
using System.Text;
using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

public sealed class PackImportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rrm_import_" + Guid.NewGuid().ToString("N"));

    public PackImportTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private string Pack(params string[] files)
    {
        string pack = Path.Combine(_folder, "pack", "RetroRewind6", "Patches");
        Directory.CreateDirectory(pack);
        foreach (string file in files)
            File.WriteAllText(Path.Combine(pack, file), file);
        return Path.Combine(_folder, "pack");
    }

    private static TrackRow Race(string file) => new() { Name = file, FileBase = file, PulsarFinalBase = file + "_Final" };

    private static TrackRow Stock(string file) => new() { Name = file, FileBase = file, StockFinalBase = Catalog.FinalNameOf(file), AliasBases = [file + "_alias"] };

    [Fact]
    public void Plan_MatchesByFileName_IgnoringCase_AndReportsTheRest()
    {
        var row = Race("sw2BooC_N");
        var plan = PackImporter.Plan(Pack("SW2BOOC_N.brstm", "mystery.brstm", "revo_kart.brsar", "notes.txt"), [row]);

        Assert.Same(row, Assert.Single(plan.Matches).Row);
        Assert.Equal(["mystery"], plan.Unmatched);
        Assert.Equal(1, plan.Tracks);
    }

    [Fact]
    public void Plan_ZeroInsteadOfTheLetterO_StillFindsTheTrack_AndReportsTheTypo()
    {
        var rows = Catalog.Build(null, out _);
        var plan = PackImporter.Plan(Pack("0_FanfareTA1st_32.brstm"), rows);

        var match = Assert.Single(plan.Matches);
        Assert.Equal("o_FanfareTA1st_32", match.Row.FileBase);
        Assert.Empty(plan.Unmatched);
        Assert.Equal("o_FanfareTA1st_32", Assert.Single(plan.Renamed).Fixed, ignoreCase: true);
    }

    [Fact]
    public void Plan_NoTrackHasThatName_ItStillComesInAsASong()
    {
        var plan = PackImporter.Plan(Pack("0_nothing.brstm", "a_N.brstm"), [Race("a_N")]);
        Assert.Single(plan.Matches);
        Assert.Equal("0_nothing.brstm", Path.GetFileName(Assert.Single(plan.Loose)));
        Assert.Empty(plan.Renamed);

        var copies = PackImporter.CopyFiles(plan, Path.Combine(_folder, "copies"));
        Assert.True(File.Exists(copies[plan.Loose[0]]));
    }

    // Every music file in a real shared pack, spelled exactly as it is, with its mistakes.
    private const string RealPackNames = "0_FanfareTA1st_32 n_block_F n_block_n n_BossMI_32 n_casino_f n_casino_n n_circuit32_f n_circuit32_n n_daisy32_f n_daisy32_n n_farm_f n_farm_n n_kinoko_f n_kinoko_n n_maple_f n_maple_n n_rainbow32_f n_rainbow32_n n_ryuusa_f n_ryuusa_n n_shopping32_f n_shopping32_n n_skate_F n_skate_n n_snowboard32_f n_snowboard32_n n_venice_F n_venice_n n_volcano32_f n_volcano32_n o_Crs_In_Fan o_Crs_In_Fan_Wifi o_Ending_PartA_32 o_FanfareGP1_32 o_FanfareGP1_only32 o_FanfareGP2_32 o_FanfareGP2_only32 o_FanfareGPdame_32 o_FanfareGPdame_only32 o_FanfareMIlose_32 o_FanfareMIlose_only32 o_FanfareMIwin_32 o_FanfareMIwin_only32 o_FanfareTA1st_32 o_FanfareTA1st_only32 o_hyousyou_winningrun o_Option_32 o_Start2_32_fan o_Start32_fan o_Wi-Fi_waiting32 o_Wi-Fi_waiting322 r_64_battle_F r_64_battle_n r_64_circuit32_f r_64_circuit32_n r_64_jungle32_f r_64_jungle32_n r_64_kuppa32_f r_64_kuppa32_n r_64_sherbet32_f r_64_sherbet32_n r_agb_battle_F r_agb_battle_n r_agb_beach32_f r_agb_beach32_n r_agb_kuppa32_f r_agb_kuppa32_n r_DS_battle_f r_DS_battle_n r_ds_desert32_f r_ds_desert32_n r_ds_garden32_f r_ds_garden32_n r_ds_jungle32_f r_ds_jungle32_n r_ds_town32_f r_ds_town32_n r_GC_Battle32_F r_GC_Battle32_n r_gc_beach32_f r_gc_beach32_n r_gc_circuit32_f r_gc_circuit32_n r_gc_mountain32_f r_gc_mountain32_n r_gc_stadium32_f r_gc_stadium32_n r_sfc_battle_f r_sfc_battle_n r_sfc_circuit32_f r_sfc_circuit32_n r_sfc_obake32_f r_sfc_obake32_n STRM_N_DESERT_f STRM_N_DESERT_n STRM_N_FACTORY_f STRM_N_FACTORY_n STRM_N_KOOPA_F STRM_N_KOOPA_N STRM_N_RIDGEHIGHWAY_F STRM_N_RIDGEHIGHWAY_N STRM_N_TRUCK_F STRM_N_TRUCK_N STRM_N_WATER_F STRM_N_WATER_N";

    [Fact]
    public void Plan_ARealSharedPack_EverySongFindsItsTrack_AndNothingIsLost()
    {
        var files = RealPackNames.Split(' ').Select(name => name + ".brstm").Append("r_sfc_circuit32_n .brstm").Append("music.txt").Append("revo_kart.brsar").ToArray();
        var rows = Catalog.Build(null, out _);

        var plan = PackImporter.Plan(Pack(files), rows);

        // The right-named files win, and the extra copies stay as songs instead of vanishing.
        Assert.Equal(["0_FanfareTA1st_32", "o_Wi-Fi_waiting322", "r_sfc_circuit32_n "], plan.Unmatched.Order(StringComparer.Ordinal));
        Assert.Equal(3, plan.Loose.Count);
        Assert.Empty(plan.Renamed);
        Assert.Equal(62, plan.Tracks);
        Assert.Equal(41, plan.Matches.Count(match => match.IsFinal));
        Assert.Equal("o_FanfareTA1st_32.brstm", Path.GetFileName(plan.Matches.Single(match => match.Row.FileBase == "o_FanfareTA1st_32" && !match.IsFinal).File), ignoreCase: true);
        Assert.Equal("r_sfc_circuit32_n.brstm", Path.GetFileName(plan.Matches.Single(match => match.Row.FileBase == "r_SFC_Circuit32_n" && !match.IsFinal).File));
        Assert.DoesNotContain(plan.Matches, match => match.File.EndsWith(".brsar"));
    }

    [Fact]
    public void TrackNotes_ComeFromThePacksTextFile()
    {
        string pack = Pack("n_farm_n.brstm", "n_circuit32_n.brstm", "STRM_N_KOOPA_N.brstm");
        File.WriteAllLines(Path.Combine(pack, "music.txt"),
        [
            "Hi thank you for downloading my music pack",
            "Luigi Circuit - Living in the City - Sonic R [OST] ",
            "Moo Moo Meadows -  Fairy Tail",
            "Wii Bowser's Castle - Tatsh",
            "Unknown Place - Nothing",
        ]);
        var rows = Catalog.Build(null, out _);

        var plan = PackImporter.Plan(pack, rows);
        PackImporter.Assign(plan, plan.Matches.ToDictionary(match => match.File, match => match.File), true);

        Assert.Equal("Living in the City - Sonic R [OST]", rows.Single(row => row.FileBase == "n_Circuit32_n").Song);
        Assert.Equal("Fairy Tail", rows.Single(row => row.FileBase == "n_Farm_n").Song);
        Assert.Equal("Tatsh", rows.Single(row => row.FileBase == "STRM_N_KOOPA_N").Song);
    }

    [Fact]
    public void Plan_StraySpaceInAName_StillFindsTheTrack()
    {
        var plan = PackImporter.Plan(Pack("a_N .brstm"), [Race("a_N")]);
        Assert.Single(plan.Matches);
        Assert.Empty(plan.Unmatched);
        Assert.Equal("a_N", Assert.Single(plan.Renamed).Fixed);
    }

    [Fact]
    public void Plan_NeverTakesTheGameFile()
    {
        var plan = PackImporter.Plan(Pack("a_N.brstm", "revo_kart.brsar"), [Race("a_N")]);
        Assert.All(plan.Matches, match => Assert.EndsWith(".brstm", match.File));
    }

    [Fact]
    public void Plan_RetroRewindFinalLapSong_ComesAlong()
    {
        var plan = PackImporter.Plan(Pack("a_N.brstm", "a_N_Final.brstm"), [Race("a_N")]);
        Assert.Contains(plan.Matches, match => match.IsFinal && match.File.EndsWith("a_N_Final.brstm"));
        Assert.Empty(plan.Unmatched);
    }

    [Fact]
    public void Plan_StockFinalLapFile_IsKept_WhateverItsCase()
    {
        var plan = PackImporter.Plan(Pack("n_farm_n.brstm", "n_farm_F.brstm"), [Stock("n_Farm_n")]);
        Assert.Equal(2, plan.Matches.Count);
        Assert.Single(plan.Matches, match => match.IsFinal && match.File.EndsWith("n_farm_F.brstm"));
        Assert.Equal(1, plan.Tracks);
        Assert.Empty(plan.Unmatched);
    }

    [Fact]
    public void Plan_TheRealStockNames_AllFindTheirTracks()
    {
        // File names from a real shared pack, spelled the way it spells them.
        var rows = Catalog.Build(null, out _);
        var plan = PackImporter.Plan(Pack("O_FanfareTA1st_32.brstm", "n_block_F.brstm", "n_block_n.brstm", "n_BossMI_32.brstm",
            "n_casino_f.brstm", "n_casino_n.brstm", "n_circuit32_f.brstm", "n_circuit32_n.brstm", "n_daisy32_f.brstm", "n_daisy32_n.brstm",
            "n_farm_f.brstm", "n_farm_n.brstm", "n_kinoko_f.brstm", "n_kinoko_n.brstm", "n_maple_f.brstm", "n_maple_n.brstm", "music.txt"), rows);

        Assert.Empty(plan.Unmatched);
        Assert.Equal(9, plan.Tracks);
        Assert.Equal(7, plan.Matches.Count(match => match.IsFinal));
        var names = plan.Matches.Where(match => !match.IsFinal).Select(match => match.Row.FileBase).ToList();
        Assert.Contains("n_Circuit32_n", names);
        Assert.Contains("n_BossMI", names);
        Assert.Contains("o_FanfareTA1st_32", names);
    }

    [Fact]
    public void Plan_OtherNamesOfATrack_OnlyCountOnce()
    {
        var plan = PackImporter.Plan(Pack("n_Farm_n.brstm", "n_Farm_n_alias.brstm"), [Stock("n_Farm_n")]);
        Assert.Single(plan.Matches);
    }

    [Fact]
    public void Plan_FinalLapFileWithoutItsSong_IsReported()
    {
        var plan = PackImporter.Plan(Pack("a_N_Final.brstm"), [Race("a_N")]);
        Assert.Empty(plan.Matches);
        Assert.Equal(["a_N_Final"], plan.Unmatched);
    }

    [Fact]
    public void Plan_CountsTracksThatWouldBeReplaced()
    {
        var full = Race("a_N");
        full.Source = "mine.mp3";
        var plan = PackImporter.Plan(Pack("a_N.brstm", "b_N.brstm"), [full, Race("b_N")]);
        Assert.Equal(1, plan.Conflicts);
    }

    [Fact]
    public void Assign_SetsTheSong_AndClearsOldEffects()
    {
        var row = Race("a_N");
        row.Speed = 0.8;
        row.Reverb = true;
        row.VolumeDb = 6;
        row.Loop = LoopMode.NoLoop;
        var plan = PackImporter.Plan(Pack("a_N.brstm"), [row]);
        var copies = PackImporter.CopyFiles(plan, Path.Combine(_folder, "copies"));

        Assert.Equal(1, PackImporter.Assign(plan, copies, true));

        Assert.Equal(Path.Combine(_folder, "copies", "a_N.brstm"), row.Source);
        Assert.Equal("a_N", row.Song);
        Assert.Equal(LoopMode.Auto, row.Loop);
        Assert.Equal(1, row.Speed);
        Assert.False(row.Reverb);
        Assert.Equal(0, row.VolumeDb);
    }

    [Fact]
    public void Assign_NotReplacing_KeepsTheSongAndItsFinal()
    {
        var kept = Race("a_N");
        kept.Source = "mine.mp3";
        var empty = Race("b_N");
        var plan = PackImporter.Plan(Pack("a_N.brstm", "a_N_Final.brstm", "b_N.brstm"), [kept, empty]);
        var copies = PackImporter.CopyFiles(plan, Path.Combine(_folder, "copies"));

        Assert.Equal(1, PackImporter.Assign(plan, copies, false));

        Assert.Equal("mine.mp3", kept.Source);
        Assert.Equal("", kept.FinalSource);
        Assert.True(empty.IsAssigned);
    }

    [Fact]
    public void Credits_BringBackNames_EvenWithCommasAndQuotes()
    {
        string pack = Pack("a_N.brstm");
        File.WriteAllText(Path.Combine(pack, "credits.csv"),
            "Track,Song,Credits,File Name\r\nLuigi,\"Hello, \"\"World\"\"\",Me,a_N\r\n", new UTF8Encoding(true));
        var row = Race("a_N");
        var plan = PackImporter.Plan(pack, [row]);
        PackImporter.Assign(plan, PackImporter.CopyFiles(plan, Path.Combine(_folder, "copies")), true);

        Assert.Equal("Hello, \"World\"", row.Song);
        Assert.Equal("Me", row.Credits);
    }

    [Fact]
    public void CopyFiles_MeansTheOriginalCanGoAway()
    {
        string pack = Pack("a_N.brstm");
        var row = Race("a_N");
        var plan = PackImporter.Plan(pack, [row]);
        var copies = PackImporter.CopyFiles(plan, Path.Combine(_folder, "copies"));
        PackImporter.Assign(plan, copies, true);
        Directory.Delete(pack, true);
        Assert.True(File.Exists(row.Source));
    }

    [Fact]
    public void NewFolder_NeverReusesAnImport()
    {
        string first = PackImporter.NewFolder(_folder, "My Pack");
        Directory.CreateDirectory(first);
        Assert.Equal(Path.Combine(_folder, "Imported packs", "My Pack (2)"), PackImporter.NewFolder(_folder, "My Pack"));
    }

    [Fact]
    public void ExtractZip_TakesOnlyMusicAndCredits_AndCantEscapeItsFolder()
    {
        string zip = Path.Combine(_folder, "pack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "RetroRewind6/Patches/a_N.brstm", "credits.csv", "evil.exe", "../../outside.brstm", "revo_kart.brsar" })
                using (var writer = new StreamWriter(archive.CreateEntry(name).Open()))
                    writer.Write("x");
        }

        string folder = PackImporter.ExtractZip(zip);
        try
        {
            var names = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToList();
            Assert.Equal(["a_N.brstm", "credits.csv", "outside.brstm"], names);
            Assert.All(Directory.GetFiles(folder, "*", SearchOption.AllDirectories), file => Assert.StartsWith(folder, file));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void FindInstalled_ListsThePatchesFolderAndModsWithMusic()
    {
        string rr = Path.Combine(_folder, "rr");
        string mods = Path.Combine(_folder, "mods");
        Directory.CreateDirectory(Path.Combine(rr, "Patches"));
        File.WriteAllText(Path.Combine(rr, "Patches", "a_N.brstm"), "x");
        Directory.CreateDirectory(Path.Combine(mods, "Music"));
        File.WriteAllText(Path.Combine(mods, "Music", "b_N.brstm"), "x");
        File.WriteAllText(Path.Combine(mods, "Music", "c_N.brstm"), "x");
        Directory.CreateDirectory(Path.Combine(mods, "Karts"));
        File.WriteAllText(Path.Combine(mods, "Karts", "kart.szs"), "x");

        var packs = PackImporter.FindInstalled(rr, [mods, Path.Combine(_folder, "nowhere")]);

        Assert.Equal(2, packs.Count);
        Assert.Contains("Patches", packs[0].Name);
        Assert.Equal(1, packs[0].Songs);
        Assert.Contains("Music", packs[1].Name);
        Assert.Equal(2, packs[1].Songs);
    }

    [Fact]
    public void FindInstalled_ListsPacksMadeHere_ButNotItsOwnSongFolders()
    {
        foreach (string name in new[] { "My Pack", "Imported packs", "Downloaded songs" })
        {
            string folder = Path.Combine(_folder, "output", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "a_N.brstm"), "x");
        }
        var pack = Assert.Single(PackImporter.FindInstalled("", [], Path.Combine(_folder, "output")));
        Assert.Contains("My Pack", pack.Name);
    }

    [Fact]
    public void FindInstalled_KnowsWhereEachPackCameFrom()
    {
        string output = Path.Combine(_folder, "output");
        string rr = Path.Combine(_folder, "rr");
        string mods = Path.Combine(_folder, "mods");
        foreach (string folder in new[] { Path.Combine(output, "Imported packs", "Theirs"), Path.Combine(output, "Mine"), Path.Combine(rr, "Patches"), Path.Combine(mods, "Mod") })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "a_N.brstm"), "x");
        }

        var packs = PackImporter.FindInstalled(rr, [mods], output);

        Assert.Equal(4, packs.Count);
        var patches = packs.Single(pack => pack.Name.Contains("Patches"));
        Assert.False(patches.MadeByApp || patches.IsMod);
        Assert.True(packs.Single(pack => pack.Name.Contains("Mod")).IsMod);
        Assert.True(packs.Single(pack => pack.Name == "Imported: Theirs").MadeByApp);
        Assert.True(packs.Single(pack => pack.Name.Contains("Mine")).MadeByApp);
    }

    [Fact]
    public void PlanFiles_TakesAnyAudio_AndReportsNamesWithNoTrack()
    {
        var row = Race("a_N");
        var plan = PackImporter.PlanFiles([Path.Combine(_folder, "a_N.mp3"), Path.Combine(_folder, "Some Song.mp3")], [row]);
        Assert.Same(row, Assert.Single(plan.Matches).Row);
        Assert.Equal(["Some Song"], plan.Unmatched);
    }

    [Fact]
    public void IsImportedCopy_OnlyInsideTheImportedFolder()
    {
        Assert.True(PackImporter.IsImportedCopy(Path.Combine(_folder, "Imported packs", "x", "a.brstm"), _folder));
        Assert.False(PackImporter.IsImportedCopy(Path.Combine(_folder, "a.brstm"), _folder));
        Assert.False(PackImporter.IsImportedCopy(Path.Combine(_folder, "Imported packs 2", "a.brstm"), _folder));
    }

    private string FolderWith(string name, params string[] files)
    {
        string folder = Path.Combine(_folder, name);
        Directory.CreateDirectory(folder);
        foreach (string file in files)
            File.WriteAllText(Path.Combine(folder, file), "x");
        return folder;
    }

    [Fact]
    public void DeletePlan_MusicOnlyMod_GoesCompletely()
    {
        string mod = FolderWith("mod", "a_N.brstm", "mod.ini", "revo_kart.brsar");
        var plan = PackImporter.DeletePlan(new InstalledPack("Mod", mod, 1, IsMod: true));
        Assert.True(plan.WholeFolder);
        Assert.Single(plan.Songs);
    }

    [Fact]
    public void DeletePlan_ModWithKarts_KeepsThem()
    {
        string mod = FolderWith("mod", "a_N.brstm", "mod.ini", "la_bike-fk.szs");
        var plan = PackImporter.DeletePlan(new InstalledPack("Mod", mod, 1, IsMod: true));
        Assert.False(plan.WholeFolder);
        Assert.Equal("a_N.brstm", Path.GetFileName(Assert.Single(plan.Songs)));
        Assert.Equal(1, plan.OtherFiles);
    }

    [Fact]
    public void DeletePlan_ThePatchesFolder_NeverGoesCompletely()
    {
        // Even with nothing but music in it, the folder itself belongs to Retro Rewind.
        string patches = FolderWith("Patches", "a_N.brstm");
        var plan = PackImporter.DeletePlan(new InstalledPack("Patches", patches, 1));
        Assert.False(plan.WholeFolder);
        Assert.Single(plan.Songs);
    }

    [Fact]
    public void DeletePlan_WhatTheAppMade_GoesCompletely()
    {
        string pack = FolderWith("mine", "a_N.brstm", "README.txt", "credits.csv");
        var plan = PackImporter.DeletePlan(new InstalledPack("Mine", pack, 1, MadeByApp: true));
        Assert.True(plan.WholeFolder);
    }

    [Fact]
    public void FindInstalled_NoRetroRewindFolder_IsFine() => Assert.Empty(PackImporter.FindInstalled("", []));
}
