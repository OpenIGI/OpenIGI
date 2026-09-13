using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The lamps the game ships as named by hand.
/// </summary>
/// <remarks>
/// Reads <c>Assets/lamps.json</c> itself rather than a copy, because the thing worth pinning is the
/// file the game loads. The list is global — a model is the same fitting in every mission and binds the
/// same textures there — so nothing here is asked about per mission, which is the point.
/// </remarks>
public sealed class LampManifestTests
{
    private static LampManifest Shipped() =>
        LampManifest.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "lamps.json"));

    [Fact]
    public void TheShippedListLoads()
    {
        Assert.NotEmpty(Shipped().Entries);
    }

    /// <summary>Every fitting the game's own catalogue names is in the list.</summary>
    /// <remarks>
    /// These are the fourteen models whose catalogue name contains LIGHT or LAMP, less
    /// <c>303_04_1</c> — a floodlight carrying no bright page at all, so which part of it burns cannot
    /// be read off the art, and it is left alone rather than guessed at. Before the list was global,
    /// four of these were named for mission 1 only and the rest for no mission at all.
    /// </remarks>
    [Theory]
    [InlineData("207_01_1", "LIGHTS_FLAT")]
    [InlineData("208_01_1", "LAMP_CIELING")]
    [InlineData("209_01_1", "MEDIUM_LIGHTS")]
    [InlineData("210_01_1", "LARGE_LIGHTS")]
    [InlineData("216_01_1", "LAMP")]
    [InlineData("226_01_1", "LAMP_CIELING")]
    [InlineData("236_01_1", "LIGHTS_ROUND")]
    [InlineData("316_01_1", "LIGHT")]
    [InlineData("324_01_1", "LIGHT")]
    [InlineData("346_01_1", "HANGER_LIGHTS")]
    [InlineData("318_01_1", "FLOODLIGHT")]
    [InlineData("335_01_1", "FLOODLIGHT")]
    [InlineData("365_01_1", "STREETLAMP")]
    [InlineData("455_06_1", "COMM_TOWER_LIGHT")]
    public void EveryCataloguedFittingIsNamed(string model, string name)
    {
        LampManifest.Entry entry = Assert.Single(
            Shipped().Entries, e => string.Equals(e.Model, model, StringComparison.OrdinalIgnoreCase));

        Assert.True(entry.IsLamp, $"{model} ({name}) is named but struck out.");
        Assert.NotEmpty(entry.Textures);
        Assert.True(entry.Radiance > 0f, $"{model} ({name}) emits nothing.");

        // The name is what says this model is a fitting at all, so the pair has to still agree.
        Assert.True(
            ModelNameRegistry.TryGetModelName(model, out string catalogued) && catalogued == name,
            $"{model} is no longer catalogued as {name}.");
    }

    /// <summary>A lit panel is named, and named dim, because it is not a light source.</summary>
    [Theory]
    [InlineData("214_01_1")]
    [InlineData("252_01_1")]
    public void TheLitPanelsAreDimmerThanAnyFitting(string model)
    {
        LampManifest.Entry entry = Assert.Single(
            Shipped().Entries, e => string.Equals(e.Model, model, StringComparison.OrdinalIgnoreCase));

        Assert.True(entry.IsLamp);
        Assert.True(
            entry.Radiance < LampDetector.LampRadiance,
            $"{model} is a lit panel and burns at {entry.Radiance}, no dimmer than a real fitting.");
    }

    /// <summary>The one thing skinned like a fitting that is not one is struck out.</summary>
    /// <remarks>
    /// <c>206_01_1</c> is an office cabinet skinned with the ceiling lamp's own page, so it passes both
    /// the shape test and the texture gate. It is the only false positive left after the gate, seven
    /// sub-meshes across the fourteen levels, and striking it out once here is what a global list buys
    /// that a per-mission one could not.
    /// </remarks>
    [Fact]
    public void TheCabinetSkinnedLikeALampIsStruckOut()
    {
        LampManifest.Entry entry = Assert.Single(
            Shipped().Entries, e => string.Equals(e.Model, "206_01_1", StringComparison.OrdinalIgnoreCase));

        Assert.False(entry.IsLamp);
    }

    /// <summary>Nothing the report named as wrongly lit is in the list.</summary>
    [Theory]
    [InlineData("373_01_1")]
    [InlineData("313_02_1")]
    [InlineData("490_02_1")]
    [InlineData("204_01_1")]
    public void TheFittingShapedFurnitureIsNotNamed(string model)
    {
        Assert.DoesNotContain(
            Shipped().Entries,
            entry => string.Equals(entry.Model, model, StringComparison.OrdinalIgnoreCase) && entry.IsLamp);
    }

    /// <summary>A model is named once, not once per mission.</summary>
    [Fact]
    public void NoModelIsNamedTwice()
    {
        IReadOnlyList<LampManifest.Entry> entries = Shipped().Entries;

        Assert.Equal(
            entries.Count,
            entries.Select(e => e.Model).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
