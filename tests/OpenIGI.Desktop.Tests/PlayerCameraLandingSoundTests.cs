using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The surface half of a landing: 0x40BE40 (falling) and 0x40BBD0 (jumping) both play the land name
/// of the material under the feet at <c>Human+1704</c>, every time they touch down.
/// </summary>
public sealed class PlayerCameraLandingSoundTests
{
    private static readonly Vector3d Feet = new(12000.0, 8000.0, 4000.0);

    [Fact]
    public void OpenTerrainLandsOnTheGroundRowAtTheFeet()
    {
        PlayerCamera camera = Landed(
            new GameMaterial { Index = 3, Name = FootstepSounds.TerrainMaterialName, LandSound = "land_ground_2" });

        (string Sound, Vector3d PositionInUnits)? landing = camera.TakeLandingSound();

        Assert.NotNull(landing);
        Assert.Equal("land_ground_2", landing.Value.Sound);
        Assert.Equal(Feet, landing.Value.PositionInUnits);
    }

    [Fact]
    public void TheLevelsOwnTerrainMaterialWinsOverTheGroundRow()
    {
        PlayerCamera camera = Landed(
            new GameMaterial { Index = 3, Name = FootstepSounds.TerrainMaterialName, LandSound = "land_ground_2" },
            new GameMaterial { Index = 9, Name = "Snow", LandSound = "land_ground_4" });
        camera.TerrainGameMaterialIndex = 9;

        (string Sound, Vector3d PositionInUnits)? landing = camera.TakeLandingSound();

        Assert.NotNull(landing);
        Assert.Equal("land_ground_4", landing.Value.Sound);
    }

    [Fact]
    public void ARowNamingNoLandSoundLandsInSilence()
    {
        // Retail hands the empty name to the bank, which finds nothing and plays nothing.
        PlayerCamera camera = Landed(
            new GameMaterial { Index = 3, Name = FootstepSounds.TerrainMaterialName, LandSound = string.Empty });

        Assert.Null(camera.TakeLandingSound());
    }

    [Fact]
    public void NoMaterialTableMeansNoLandingSound()
    {
        PlayerCamera camera = new();
        camera.UseWalls(ObjectCollisionIndex.Build([]));
        camera.Body.Position = Feet;

        Assert.Null(camera.TakeLandingSound());
    }

    private static PlayerCamera Landed(params GameMaterial[] rows)
    {
        GameMaterialRegistry materials = new();
        foreach (GameMaterial row in rows)
        {
            materials.Add(row);
        }

        PlayerCamera camera = new();
        camera.UseMaterials(materials);
        camera.UseWalls(ObjectCollisionIndex.Build([]));
        camera.Body.Position = Feet;
        return camera;
    }
}
