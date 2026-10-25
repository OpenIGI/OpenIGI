using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The parts of <c>0x463920</c> and the two airborne landings that decide what a step and a touchdown
/// actually sound like.
/// </summary>
public sealed class FootstepSoundRulesTests
{
    private static GameMaterial Metal => new()
    {
        Index = 7,
        Name = "Metal",
        Value5 = 12f,
        WalkSounds = ["walk_metal_1", "walk_metal_2", "walk_metal_3", "walk_metal_4", "walk_metal_5", "walk_metal_6"],
        LandSound = "land_metal_1",
    };

    [Fact]
    public void TheCycleIsTheAuthoredTwelveAndNotAPermutation()
    {
        // The twelve dwords at 0x53F318. The tail is irregular on purpose: 3 twice, 0 not at all.
        Assert.Equal([0, 1, 2, 3, 4, 5, 3, 1, 4, 2, 3, 5], FootstepSounds.Cycle.ToArray());
    }

    [Fact]
    public void TheCursorWalksTheCycleAndWrapsAtTwelve()
    {
        GameMaterial material = Metal;
        int cursor = 0;
        List<string> heard = [];

        for (int step = 0; step < 12; step++)
        {
            heard.Add(FootstepSounds.Next(material, ref cursor, out _));
        }

        Assert.Equal(0, cursor);
        Assert.Equal("walk_metal_1", heard[0]);
        Assert.Equal("walk_metal_6", heard[5]);
        Assert.Equal("walk_metal_4", heard[6]);
        Assert.Equal("walk_metal_6", heard[11]);
    }

    [Fact]
    public void TheReportedSlotIsTheOneTheSoundCameFrom()
    {
        GameMaterial material = Metal;
        int cursor = 0;

        for (int step = 0; step < 12; step++)
        {
            string sound = FootstepSounds.Next(material, ref cursor, out int slot);
            Assert.Equal(material.WalkSounds[slot], sound);
        }
    }

    [Theory]
    [InlineData(0, "walk_rain_1")]
    [InlineData(3, "walk_rain_4")]
    [InlineData(5, "walk_rain_6")]
    public void TheRainLayerIsNamedFromTheSameSlot(int slot, string expected)
    {
        // sprintf(buffer, "walk_rain_%1d", slot + 1) -- one-based, and the same slot the step took.
        Assert.Equal(expected, FootstepSounds.RainSoundName(slot));
    }

    [Fact]
    public void ACrouchedStepKeepsSixtySixHundredthsOfItsVolume()
    {
        Assert.Equal(0.66f, FootstepSounds.CrouchedVolumeScale, 1e-6f);
    }

    [Fact]
    public void TheLandSoundIsTheMaterialsOwnAndNotTheFallCue()
    {
        // Human+1772 plus 276, which is the field straight after the six 32-byte walk names.
        Assert.Equal("land_metal_1", FootstepSounds.LandSound(Metal));

        HumanFallImpactIsSeparate();
    }

    private static void HumanFallImpactIsSeparate()
    {
        // A harmless drop names no player_fall cue at all, which is exactly why the surface sound has
        // to exist: without it a jump landed in silence.
        OpenIGI.Game.Player.HumanFallImpact gentle =
            OpenIGI.Game.Player.HumanFallDamage.Calculate(-1000f, 1f);

        Assert.Equal(string.Empty, gentle.SoundName);
        Assert.NotEqual(0f, gentle.ViewKickUnits);
    }

    [Fact]
    public void AMaterialNamingNoLandSoundIsSilentRatherThanBroken()
    {
        GameMaterial air = new() { Index = 0, Name = "Air" };

        Assert.Equal(string.Empty, FootstepSounds.LandSound(air));
    }

    [Fact]
    public void TheHearingRadiusIsTheMaterialsOwnNumberInMetres()
    {
        // The handler posts its event with argument 5 times 4096, so the ground decides how far a step
        // carries as well as what it sounds like.
        Assert.Equal(12.0 * 4096.0, FootstepSounds.HearingRadiusInUnits(Metal));
    }

    [Fact]
    public void TheTerrainNamesItsOwnGameMaterialRatherThanAlwaysBeingGround()
    {
        // Argument 1 of CreateTerrainMaterial is the game material row, not a flag word: 0x4CB810
        // takes its low byte and selects scene+3388+4*index, the same array a mesh face indexes.
        Dictionary<int, TerrainMaterial> snow = new()
        {
            [0] = new TerrainMaterial(0, 20, new TerrainMaterialBand[TerrainMaterial.BandCount]),
            [1] = new TerrainMaterial(1, 20, new TerrainMaterialBand[TerrainMaterial.BandCount]),
        };

        // 20 is Snow in material.qvm's row order, which is what levels 7, 8, 11 and 12 declare.
        Assert.Equal(20, FootstepSounds.TerrainGameMaterialIndex(snow));
    }

    [Fact]
    public void TheOrdinaryLevelsWalkOnGroundWhichIsRowOne()
    {
        Dictionary<int, TerrainMaterial> earth = new()
        {
            [0] = new TerrainMaterial(0, 1, new TerrainMaterialBand[TerrainMaterial.BandCount]),
        };

        Assert.Equal(1, FootstepSounds.TerrainGameMaterialIndex(earth));
    }

    [Fact]
    public void ALevelWithNoTerrainMaterialsFallsBackRatherThanGuessing()
    {
        Assert.Null(FootstepSounds.TerrainGameMaterialIndex(new Dictionary<int, TerrainMaterial>()));
        Assert.Equal("Ground", FootstepSounds.TerrainMaterialName);
    }

    [Fact]
    public void TheLowestIdDecidesSoTheAnswerDoesNotDependOnDictionaryOrder()
    {
        Dictionary<int, TerrainMaterial> mixed = new()
        {
            [3] = new TerrainMaterial(3, 20, new TerrainMaterialBand[TerrainMaterial.BandCount]),
            [1] = new TerrainMaterial(1, 1, new TerrainMaterialBand[TerrainMaterial.BandCount]),
        };

        Assert.Equal(1, FootstepSounds.TerrainGameMaterialIndex(mixed));
    }

    [Fact]
    public void ARowThatNamesNoWalkIsNotAFloor()
    {
        // Air, Water, Flesh, BloodyFlesh, HumanCollision, MetalLadder and MetalFence declare six empty
        // walk names between them. Level 1's human-collision hull is entirely material 21, so probing
        // movement collision for a footstep found a floor made of nothing.
        GameMaterial humanCollision = new()
        {
            Index = 21,
            Name = "HumanCollision",
            WalkSounds = ["", "", "", "", "", ""],
        };

        Assert.False(FootstepSounds.NamesAWalk(humanCollision));
        Assert.True(FootstepSounds.NamesAWalk(Metal));
    }

    [Fact]
    public void ARowWithEvenOneNamedWalkCounts()
    {
        GameMaterial air = new()
        {
            Index = 0,
            Name = "Air",
            WalkSounds = ["Walk0", "", "", "", "", ""],
        };

        // Air names a placeholder rather than nothing, so it passes -- and the cycle then draws five
        // empty slots out of six. It is excluded by never being the surface, not by this test.
        Assert.True(FootstepSounds.NamesAWalk(air));
    }

    [Fact]
    public void AnEmptyRowIsNotAFloorEither()
    {
        Assert.False(FootstepSounds.NamesAWalk(new GameMaterial { Index = 22, Name = "MetalLadder" }));
    }

    [Fact]
    public void TheProbeReachesFromJustAboveTheGroundToHalfAMetreBelow()
    {
        (Core.Numerics.Vector3d from, Core.Numerics.Vector3d to) =
            FootstepSounds.Probe(new Core.Numerics.Vector3d(10.0, 20.0, 100.0));

        Assert.Equal(100.0 + 204.8, from.Z, 3);
        Assert.Equal(100.0 - 2048.0, to.Z, 3);
        Assert.Equal(10.0, from.X, 3);
        Assert.Equal(20.0, to.Y, 3);
    }
}
