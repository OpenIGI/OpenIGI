using OpenIGI.Presentation.Cameras;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The backdrop cameras belong to one game each.</summary>
/// <remarks>
/// The two titles number their missions over each other: Project I.G.I.'s campaign runs 1 to 14 and
/// I.G.I. 2's runs 11 to 17, 21 to 26 and 31 to 36, so missions 11 to 14 exist in both. Keyed by the
/// number alone, browsing the sequel's first four missions in the menu showed the first game's trainyard,
/// SAM base and harbour — real places, on the wrong map, and only after a title switch had put the other
/// game's numbers in front of the same table.
/// </remarks>
public sealed class MenuCameraLibraryTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"openigi-menu-cameras-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private MenuCameraLibrary Write(string json)
    {
        File.WriteAllText(_path, json);
        return MenuCameraLibrary.Load(_path);
    }

    /// <summary>A mission number both games use resolves to each game's own camera.</summary>
    [Fact]
    public void TheSameMissionNumberInBothGamesKeepsItsOwnCamera()
    {
        MenuCameraLibrary library = Write(
            """
            [
              { "mission": 11, "radius": 150, "height": 60, "speed": 0.12, "startAngle": 0.6, "pitchBias": -0.05 },
              { "mission": 11, "title": "igi2", "radius": 300, "height": 120, "speed": 0.2, "startAngle": 1.5, "pitchBias": 0.25 }
            ]
            """);

        Assert.Equal(150.0, library.For(11, "igi1").Radius, 3);
        Assert.Equal(300.0, library.For(11, "igi2").Radius, 3);
    }

    /// <summary>An entry that names no game is Project I.G.I.'s, which is every entry authored so far.</summary>
    [Fact]
    public void AnUnmarkedEntryBelongsToTheFirstGame()
    {
        MenuCameraLibrary library = Write(
            """
            [
              { "mission": 3, "radius": 140, "height": 55, "speed": 0.12, "startAngle": 0, "pitchBias": 0 }
            ]
            """);

        Assert.Equal(140.0, library.For(3, MenuCameraLibrary.DefaultTitle).Radius, 3);

        // And the other game gets the built-in orbit rather than this one: a sensible default beats a
        // camera pointed at another map's coordinates, which is the whole of the bug.
        Assert.Equal(MenuCameraSpec.Default.Radius, library.For(3, "igi2").Radius, 3);
    }

    /// <summary>The authored places are scoped the same way, since they are coordinates on a map.</summary>
    [Fact]
    public void TheAuthoredPlacesBelongToOneGameToo()
    {
        MenuCameraLibrary library = Write(
            """
            [
              {
                "mission": 12, "radius": 150, "height": 60, "speed": 0.12, "startAngle": 0, "pitchBias": 0,
                "points": [ { "x": 1, "y": 2, "z": 3 } ]
              }
            ]
            """);

        Assert.Single(library.PointsFor(12, "igi1"));
        Assert.Empty(library.PointsFor(12, "igi2"));
    }

    /// <summary>A title's casing in the file cannot make it miss.</summary>
    [Fact]
    public void TheTitleIsMatchedWithoutRegardToCase()
    {
        MenuCameraLibrary library = Write(
            """
            [
              { "mission": 21, "title": "IGI2", "radius": 200, "height": 60, "speed": 0.12, "startAngle": 0, "pitchBias": 0 }
            ]
            """);

        Assert.Equal(200.0, library.For(21, "igi2").Radius, 3);
    }
}
