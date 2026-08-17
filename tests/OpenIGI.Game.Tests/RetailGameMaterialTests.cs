using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Reads the game material table, which is what decides the mark a round leaves.
/// </summary>
/// <remarks>
/// The whole decal system hangs off this one script: a collision face names a row, the row names a
/// texture, and a row that names none is never even recorded as an impact.
/// </remarks>
public sealed class RetailGameMaterialTests
{
    private static GameMaterialRegistry Load()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        resources.Register(
            GameMaterialRegistry.ScriptResource,
            fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));

        GameLoop loop = new(new ManualTimeSource());
        GameMaterialRegistry materials = new();
        materials.Register(loop.Natives);
        materials.Load(resources, loop.Natives);
        return materials;
    }

    [RequiresGameDataFact]
    public void TheTableReadsAndIsIndexedByItsDeclarationOrder()
    {
        GameMaterialRegistry materials = Load();

        // SetActiveMaterial picks the row, so row 0 is whatever it selected first.
        Assert.True(materials.Count > 1, $"only {materials.Count} material(s) read.");
        Assert.True(materials.TryGet(0, out GameMaterial first));
        Assert.Equal("Air", first.Name);
    }

    [RequiresGameDataFact]
    public void AirLeavesNoMarkAtAll()
    {
        GameMaterialRegistry materials = Load();
        Assert.True(materials.TryGet(0, out GameMaterial air));

        // The guard the shot loop uses. Air names no texture, so a round through it records nothing --
        // that is the mechanism, not an omission.
        Assert.False(air.RecordsImpacts);
        Assert.Equal(string.Empty, air.Entry.EntryTexture);
    }

    [RequiresGameDataFact]
    public void WoodNamesItsOwnBulletHoleAndItsOwnSounds()
    {
        GameMaterialRegistry materials = Load();
        GameMaterial wood = Find(materials, "Wood");

        // The field order is what a positional reader has to get right: six walk sounds, a landing
        // sound, a rain sound, then six bullet sounds. If the cursor were one out, these would be
        // walk sounds.
        Assert.Equal("walk_wood_1", wood.WalkSounds[0]);
        Assert.Equal("walk_wood_6", wood.WalkSounds[5]);
        Assert.Equal("land_ground_5", wood.LandSound);
        Assert.Equal("rain_2", wood.RainSound);
        Assert.Equal("bul_wood_1", wood.BulletSounds[0]);
        Assert.Equal("bul_wood_6", wood.BulletSounds[5]);

        Assert.True(wood.RecordsImpacts);
        Assert.Equal("bullethole_wood.tex", wood.Entry.EntryTexture);
        Assert.Equal("bullethole_wood.tex", wood.Entry.ExitTexture);
        Assert.Equal("1011_01_1", wood.Entry.Model);
    }

    [RequiresGameDataFact]
    public void TheMarkSizeIsAuthoredInMetresAndStoredInUnits()
    {
        GameMaterialRegistry materials = Load();
        GameMaterial wood = Find(materials, "Wood");

        // 0.03 m, which the reader multiplies by 4096 -- a three-centimetre hole.
        Assert.Equal(0.03f, wood.Entry.SizeInMetres, 4);
        Assert.Equal(0.03f * 4096f, wood.Entry.SizeInUnits, 2);
    }

    [RequiresGameDataFact]
    public void EveryMaterialDeclaresSixWalkAndSixBulletSounds()
    {
        GameMaterialRegistry materials = Load();

        foreach (GameMaterial material in materials.ByIndex.Values)
        {
            Assert.Equal(GameMaterialRegistry.WalkSoundCount, material.WalkSounds.Count);
            Assert.Equal(GameMaterialRegistry.BulletSoundCount, material.BulletSounds.Count);
        }
    }

    [RequiresGameDataFact]
    public void MostMaterialsLeaveAMarkAndSomeDeliberatelyDoNot()
    {
        GameMaterialRegistry materials = Load();

        int marking = materials.ByIndex.Values.Count(m => m.RecordsImpacts);
        int silent = materials.Count - marking;

        // Both groups exist, which is what makes the guard meaningful rather than dead code.
        Assert.True(marking > 0, "no material leaves a mark.");
        Assert.True(silent > 0, "every material leaves a mark, so the empty-texture guard is dead.");
    }

    [Theory]
    // The binocular detector's sight ray passes through these: Water, Glass, Fence, MetalLadder,
    // MetalFence -- glass and wire do not bound retail's sector visibility, so a guard behind them
    // stays marked.
    [InlineData(2, true)]
    [InlineData(14, true)]
    [InlineData(18, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    // Solid surfaces occlude, and Air (0) must NOT be transparent: the terrain trace reports material
    // 0 by default, so treating it as see-through would let contacts show through a hill.
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(6, false)]
    [InlineData(11, false)]
    public void SightTransparencyMatchesTheSeeThroughMaterials(int globalMaterial, bool expected)
    {
        Assert.Equal(expected, GameMaterial.IsSightTransparent(globalMaterial));
    }

    private static GameMaterial Find(GameMaterialRegistry materials, string name)
    {
        GameMaterial? found = materials.ByIndex.Values.FirstOrDefault(
            m => string.Equals(m.Name, name, StringComparison.Ordinal));

        Assert.True(found is not null, $"no material named '{name}'.");
        return found!;
    }
}
