using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The ballistic option's impact sounds: the material's own six, retail's pick and limit.</summary>
public sealed class BulletImpactSoundTests
{
    [Fact]
    public void ThePickIsAlwaysOneOfTheMaterialsOwnSix()
    {
        GameMaterial material = new()
        {
            Name = "Metal",
            BulletSounds = ["bul_met_1", "bul_met_2", "bul_met_3", "bul_met_4", "bul_met_5", "bul_met_6"],
        };

        IgiRandom random = new();
        for (int i = 0; i < 100; i++)
        {
            Assert.Contains(BulletImpactSounds.Pick(material, random), material.BulletSounds);
        }
    }

    [Fact]
    public void AMaterialNamingNoSoundsIsSilent()
    {
        Assert.Equal(string.Empty, BulletImpactSounds.Pick(new GameMaterial { Name = "Air" }, new IgiRandom()));
    }

    /// <summary>The retail limit: a timestamp plus <c>rand(6)+6</c> ticks, for the whole set.</summary>
    [Fact]
    public void TheQuietSpellIsSixToElevenTicks()
    {
        IgiRandom random = new();
        for (int i = 0; i < 100; i++)
        {
            Assert.InRange(BulletImpactSounds.QuietTicks(random), 6, 11);
        }
    }
}
