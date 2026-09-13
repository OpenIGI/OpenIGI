using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Reading the hand-written lamp list.
/// </summary>
/// <remarks>
/// The list is a flat array of models that applies to the whole game. It used to be keyed by mission,
/// which said the same thing up to fourteen times over — a model is the same fitting in every level and
/// binds the same textures there — and in practice named ten models for mission 1 and nothing for the
/// other thirteen.
/// </remarks>
public sealed class LampManifestLoadTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lamps-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
    }

    private LampManifest Write(string json)
    {
        File.WriteAllText(_path, json);
        return LampManifest.Load(_path);
    }

    [Fact]
    public void ReadsAFlatListThatNamesNoMission()
    {
        LampManifest manifest = Write(
            """
            {
              "lamps": [
                { "model": "316_01_1", "textures": ["316_04_1", "316_03_1"], "radiance": 4.5, "note": "a fitting" }
              ]
            }
            """);

        LampManifest.Entry entry = Assert.Single(manifest.Entries);

        Assert.Equal("316_01_1", entry.Model);
        Assert.Equal(["316_04_1", "316_03_1"], entry.Textures);
        Assert.Equal(4.5f, entry.Radiance);
        Assert.Equal("a fitting", entry.Note);
        Assert.True(entry.IsLamp);
    }

    /// <summary>An entry that names no radiance burns like an ordinary fitting.</summary>
    [Fact]
    public void RadianceDefaultsToAnOrdinaryFitting()
    {
        LampManifest manifest = Write("""{ "lamps": [ { "model": "226_01_1" } ] }""");

        Assert.Equal(LampDetector.LampRadiance, Assert.Single(manifest.Entries).Radiance);
    }

    /// <summary>Naming no texture leaves the emitting sub-mesh to be chosen.</summary>
    [Fact]
    public void TexturesMayBeOmitted()
    {
        LampManifest manifest = Write("""{ "lamps": [ { "model": "226_01_1" } ] }""");

        Assert.Empty(Assert.Single(manifest.Entries).Textures);
    }

    /// <summary>A detection is struck out by naming the model and saying it is not one.</summary>
    [Fact]
    public void LampFalseStrikesOutADetection()
    {
        LampManifest manifest = Write(
            """{ "lamps": [ { "model": "206_01_1", "lamp": false, "note": "a cabinet" } ] }""");

        Assert.False(Assert.Single(manifest.Entries).IsLamp);
    }

    /// <summary>An entry with no model names nothing and is skipped rather than throwing.</summary>
    [Fact]
    public void AnEntryWithoutAModelIsSkipped()
    {
        LampManifest manifest = Write("""{ "lamps": [ { "note": "nothing" }, { "model": "316_01_1" } ] }""");

        Assert.Equal("316_01_1", Assert.Single(manifest.Entries).Model);
    }

    /// <summary>Comments and trailing commas are allowed, because a person maintains this by hand.</summary>
    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        LampManifest manifest = Write(
            """
            {
              // the fittings
              "lamps": [
                { "model": "316_01_1" },
              ],
            }
            """);

        Assert.Single(manifest.Entries);
    }

    /// <summary>A broken file never stops the game; every level falls back to the detector alone.</summary>
    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ }")]
    [InlineData("""{ "lamps": { } }""")]
    [InlineData("""{ "missions": { "1": [ { "model": "316_01_1" } ] } }""")]
    public void AnythingUnreadableNamesNothing(string json)
    {
        Assert.Empty(Write(json).Entries);
    }

    [Fact]
    public void AMissingFileNamesNothing()
    {
        Assert.Empty(LampManifest.Load(Path.Combine(Path.GetTempPath(), "no-such-lamps.json")).Entries);
        Assert.Empty(LampManifest.Empty.Entries);
    }
}
