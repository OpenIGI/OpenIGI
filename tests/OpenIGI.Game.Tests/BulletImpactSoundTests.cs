using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The ballistic option's impact sounds: the material's own six, retail's pick and limit.</summary>
public sealed class BulletImpactSoundTests
{
    private static readonly string[] RetailRicochetSounds =
        ["riccochet_1", "riccochet_2", "riccochet_3", "riccochet_4"];

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

    [Fact]
    public void HumanAndSurfaceContactsResolveTheirRetailRows()
    {
        GameMaterialRegistry materials = new();
        materials.Add(new GameMaterial { Index = 0, Name = "Ground", BulletSounds = ["bul_dirt_1"] });
        materials.Add(new GameMaterial { Index = 6, Name = "Metal", BulletSounds = ["bul_metal_nrm_1"] });
        materials.Add(new GameMaterial { Index = 62, Name = "Flesh", BulletSounds = ["bul_flesh_1"] });

        Assert.Equal(
            "bul_flesh_1",
            BulletImpactSounds.PickForImpact(
                materials,
                struckHuman: true,
                surfaceMaterial: 6,
                random: new IgiRandom()));
        Assert.Equal(
            "bul_metal_nrm_1",
            BulletImpactSounds.PickForImpact(
                materials,
                struckHuman: false,
                surfaceMaterial: 6,
                random: new IgiRandom()));
        // An unresolvable row is silent, not dirt. 0x47BAAD reads the six names off the material the
        // trace handed back; there is no name lookup to fall back through.
        Assert.Equal(
            string.Empty,
            BulletImpactSounds.PickForImpact(
                materials,
                struckHuman: false,
                surfaceMaterial: 999,
                random: new IgiRandom()));
    }

    [Fact]
    public void AKnownSilentSurfaceRowDoesNotFallBackToGround()
    {
        GameMaterialRegistry materials = new();
        materials.Add(new GameMaterial { Index = 0, Name = "Ground", BulletSounds = ["bul_dirt_1"] });
        materials.Add(new GameMaterial { Index = 2, Name = "Water" });

        Assert.Equal(
            string.Empty,
            BulletImpactSounds.PickForImpact(
                materials,
                struckHuman: false,
                surfaceMaterial: 2,
                random: new IgiRandom()));
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

    /// <summary>
    /// The pick is <c>rand() % 6</c> over the row's six slots (<c>0x47BAAD</c>:
    /// <c>32 * (rand % 6) + row + 340</c>), never <c>rand(named)</c>. Half the retail table leaves
    /// slots empty on purpose — <c>Air</c>, <c>Water</c> and <c>Snow</c> name one of six, <c>Ground</c>,
    /// <c>BloodyFlesh</c> and <c>Plastic</c> three — so those rows are meant to fall silent for the
    /// slots they do not name, and narrowing the roll to the named ones would make them sound every time.
    /// </summary>
    [Fact]
    public void AnUnnamedSlotIsSilentRatherThanRolledAway()
    {
        GameMaterial water = new()
        {
            Name = "Water",
            BulletSounds = ["Hit0", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty],
        };

        IgiRandom random = new();
        int silent = 0;
        for (int i = 0; i < 600; i++)
        {
            if (BulletImpactSounds.Pick(water, random).Length == 0)
            {
                silent++;
            }
        }

        Assert.InRange(silent, 400, 600);
    }

    /// <summary>
    /// A retail impact is two sounds. Beside the material's own sample, <c>0x47C330</c> picks one of
    /// four <c>riccochet_*</c> samples — two <c>c</c>s, as the retail sound bank spells it — by
    /// <c>rand() % 4</c>, reading nothing off the surface.
    /// </summary>
    [Fact]
    public void TheRicochetWhizzIsOneOfFourAndIgnoresTheSurface()
    {
        Assert.Equal(RetailRicochetSounds, BulletImpactSounds.RicochetSounds);

        IgiRandom random = new();
        HashSet<string> seen = [];
        for (int i = 0; i < 400; i++)
        {
            string picked = BulletImpactSounds.PickRicochet(random);
            Assert.Contains(picked, BulletImpactSounds.RicochetSounds);
            seen.Add(picked);
        }

        // The bank also defines riccochet_5, which the retail table does not index and so never plays.
        Assert.Equal(4, seen.Count);
        Assert.DoesNotContain("riccochet_5", BulletImpactSounds.RicochetSounds);
    }
}
