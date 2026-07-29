using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What a round leaves behind, and the rule that decides whether it leaves anything.
/// </summary>
public sealed class BulletMarksTests
{
    private static WorldRayHit Hit(int material) =>
        new(new Vector3d(100, 200, 300), new Vector3f(0f, 0f, 1f), 10.0, material);

    private static GameMaterialRegistry Table()
    {
        // Two rows: one that names a texture and one that does not, which is exactly the split the
        // retail table has and the split the guard exists for.
        GameMaterialRegistry registry = new();
        registry.Add(new GameMaterial
        {
            Index = 0,
            Name = "Air",
            Entry = new GunShotEffect(true, true, string.Empty, string.Empty, 0.03f, string.Empty),
        });

        // Named for the row every impact is attributed to while the face-to-material lookup is unread.
        registry.Add(new GameMaterial
        {
            Index = 1,
            Name = BulletMarks.FallbackMaterial,
            Entry = new GunShotEffect(true, true, "bullethole_wood.tex", "exit.tex", 0.03f, "1011_01_1"),
        });

        return registry;
    }

    [Fact]
    public void AMaterialThatNamesNoTextureLeavesNothing()
    {
        // The shot loop's own guard: a material naming no texture is not marked faintly or with a
        // default, it is skipped. Here the whole table is such a material.
        GameMaterialRegistry table = new();
        table.Add(new GameMaterial
        {
            Index = 0,
            Name = "Air",
            Entry = new GunShotEffect(true, true, string.Empty, string.Empty, 0.03f, string.Empty),
        });

        BulletMarks marks = new();
        Assert.False(marks.TryAdd(Hit(0), table));
        Assert.Empty(marks.Marks);
    }

    [Fact]
    public void AnEmptyTableLeavesNothing()
    {
        BulletMarks marks = new();
        Assert.False(marks.TryAdd(Hit(99), new GameMaterialRegistry()));
        Assert.Empty(marks.Marks);
    }

    [Fact]
    public void AMarkTakesItsTextureAndSizeFromTheMaterial()
    {
        BulletMarks marks = new();
        Assert.True(marks.TryAdd(Hit(1), Table()));

        BulletMark mark = Assert.Single(marks.Marks);
        Assert.Equal("bullethole_wood.tex", mark.Texture);
        Assert.Equal(0.03f * 4096f, mark.SizeInUnits, 2);
    }

    [Fact]
    public void AnExitWoundTakesTheExitTexture()
    {
        BulletMarks marks = new();
        Assert.True(marks.TryAdd(Hit(1), Table(), isEntry: false));
        Assert.Equal("exit.tex", marks.Marks[0].Texture);
    }

    [Fact]
    public void AMarkIsLiftedOffTheSurfaceItIsOn()
    {
        // Otherwise it fights the wall for depth. The lift is the same five millimetres the shot uses
        // to step off a penetrated face.
        BulletMarks marks = new();
        Assert.True(marks.TryAdd(Hit(1), Table()));

        Assert.Equal(300.0 + 20.48, marks.Marks[0].Position.Z, 3);
    }

    [Fact]
    public void TheOldestMarksAreDroppedPastTheCapacity()
    {
        BulletMarks marks = new(capacity: 4);
        GameMaterialRegistry table = Table();

        for (int i = 0; i < 10; i++)
        {
            marks.TryAdd(Hit(1), table);
        }

        Assert.Equal(4, marks.Marks.Count);
        Assert.Equal(10, marks.TotalAdded);
    }
}
