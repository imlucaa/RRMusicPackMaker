using System.Text.Json;
using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

/// <summary>The headless commands the GPUI front end calls. They share the saved pack with the classic window.</summary>
[Collection("windows")]
public sealed class EngineTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "rrm_engine_" + Guid.NewGuid().ToString("N"));

    public EngineTests()
    {
        Directory.CreateDirectory(_data);
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", _data);
        Environment.SetEnvironmentVariable("RRMUSIC_RR", Path.Combine(_data, "no rr here"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", null);
        Environment.SetEnvironmentVariable("RRMUSIC_RR", null);
        Directory.Delete(_data, true);
    }

    private static (int Code, string Output) Run(params string[] args)
    {
        var before = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            int code = Engine.Run(["engine", .. args]);
            return (code, writer.ToString());
        }
        finally
        {
            Console.SetOut(before);
        }
    }

    [Fact]
    public void Catalog_ListsEveryTrack_WithGroupsForTheSidebar()
    {
        var (code, output) = Run("catalog");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        var rows = doc.RootElement.GetProperty("rows");
        Assert.True(rows.GetArrayLength() > 400);
        Assert.Contains(rows.EnumerateArray(), row => row.GetProperty("category").GetString() == RRMusicPackMaker.Catalog.CatRegs);
        var groups = doc.RootElement.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("title").GetString()).ToList();
        Assert.Contains("Tracks", groups);
        // The stock slots stay out of the sidebar, as in the classic window.
        Assert.DoesNotContain(doc.RootElement.GetProperty("groups").EnumerateArray()
            .SelectMany(g => g.GetProperty("categories").EnumerateArray()).Select(c => c.GetString()),
            category => category == RRMusicPackMaker.Catalog.CatStock);
    }

    [Fact]
    public void Songs_IsAJsonList_EvenWhenEmpty()
    {
        var (code, output) = Run("songs");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Export_WithNothingAssigned_SaysSo_AsAnErrorEvent()
    {
        var (code, output) = Run("export", "--mode", "zip", "--target", Path.Combine(_data, "p.zip"));
        Assert.NotEqual(0, code);
        using var doc = JsonDocument.Parse(output.Trim().Split('\n').Last());
        Assert.Equal("error", doc.RootElement.GetProperty("event").GetString());
    }

    [Fact]
    public void Export_WithoutATarget_IsRefused()
    {
        var (code, output) = Run("export", "--mode", "folder");
        Assert.Equal(2, code);
        Assert.Contains("\"error\"", output);
    }

    [Fact]
    public void Preview_TrackWithoutASong_IsAnError()
    {
        var (code, output) = Run("preview", "--track", "6_N");
        Assert.Equal(1, code);
        Assert.Contains("no song", output);
    }

    [Fact]
    public void UnknownCommand_ShowsUsage()
    {
        var before = Console.Error;
        using var errors = new StringWriter();
        Console.SetError(errors);
        try
        {
            Assert.Equal(2, Engine.Run(["engine", "nonsense"]));
            Assert.Contains("Usage", errors.ToString());
        }
        finally
        {
            Console.SetError(before);
        }
    }
}
