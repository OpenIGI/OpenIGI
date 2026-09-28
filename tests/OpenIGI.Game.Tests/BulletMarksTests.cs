using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What a round leaves behind, and the rules that decide whether it leaves anything.
/// </summary>
public sealed class BulletMarksTests
{
    // Any object will do as the thing a mark is stuck to; the rules only compare identities.
    private static readonly object Wall = new();

    private static WorldRayHit Hit(int material, double x = 100) =>
        new(new Vector3d(x, 200, 300), new Vector3f(0f, 0f, 1f), 10.0, material);

    private static GameMaterialRegistry Table()
    {
        // Rows that name a texture and rows that do not, which is exactly the split the retail table has
        // and the split the guard exists for.
        GameMaterialRegistry registry = new();
        registry.Add(new GameMaterial
        {
            Index = 0,
            Name = "Air",
            Entry = new GunShotEffect(true, true, string.Empty, string.Empty, 0.03f, string.Empty),
        });

        registry.Add(new GameMaterial
        {
            Index = 1,
            Name = "Wood",
            Entry = new GunShotEffect(true, true, "bullethole_wood.tex", "exit.tex", 0.03f, "1011_01_1"),
            Exit = new GunShotEffect(false, false, "far_wood.tex", "far_wood_exit.tex", 0.06f, "1011_01_1"),
        });

        // A row that names a texture but sits at a different index, so a mark proves the struck face's
        // own row was read rather than any single row standing in for the whole table.
        registry.Add(new GameMaterial
        {
            Index = 2,
            Name = "Glass",
            Entry = new GunShotEffect(true, true, "hole_plexi1.tex", "hole_plexi1.tex", 0.02f, "1011_01_1"),
        });

        // Fence records no impact at all, which is why the original leaves a chain fence unmarked.
        registry.Add(new GameMaterial
        {
            Index = 3,
            Name = "Fence",
            Entry = new GunShotEffect(true, true, string.Empty, string.Empty, 0.03f, string.Empty),
        });

        // The row an index the table does not hold falls back to.
        registry.Add(new GameMaterial
        {
            Index = 4,
            Name = BulletMarks.FallbackMaterial,
            Entry = new GunShotEffect(true, true, "hole_concrete1.tex", "hole_concrete1.tex", 0.03f, "1009_01_1"),
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
        Assert.False(marks.TryAdd(Hit(0), Wall, table));
        Assert.Empty(marks.Marks);
    }

    [Fact]
    public void AnEmptyTableLeavesNothing()
    {
        BulletMarks marks = new();
        Assert.False(marks.TryAdd(Hit(99), Wall, new GameMaterialRegistry()));
        Assert.Empty(marks.Marks);
    }

    [Fact]
    public void AnUnknownMaterialUsesTheAuthoredConcreteFallback()
    {
        GameMaterialRegistry table = Table();
        BulletMarks marks = new();

        Assert.True(table.TryGetByName(BulletMarks.FallbackMaterial, out GameMaterial concrete));
        Assert.True(marks.TryAdd(Hit(99), Wall, table));
        Assert.Equal(concrete.Entry.EntryTexture, Assert.Single(marks.Marks).Texture);
    }

    [Fact]
    public void AMarkTakesItsTextureAndSizeFromTheMaterial()
    {
        BulletMarks marks = new();
        Assert.True(marks.TryAdd(Hit(1), Wall, Table()));

        BulletMark mark = Assert.Single(marks.Marks);
        Assert.Equal("bullethole_wood.tex", mark.Texture);
        Assert.Equal(0.03f * 4096f, mark.SizeInUnits, 2);
        Assert.Same(Wall, mark.Surface);
    }

    [Fact]
    public void AnExitWoundTakesTheExitTexture()
    {
        BulletMarks marks = new();
        Assert.True(marks.TryAdd(Hit(1), Wall, Table(), isEntry: false));
        Assert.Equal("exit.tex", marks.Marks[0].Texture);
    }

    /// <summary>
    /// A round coming OUT of a surface is recorded with the material's second block
    /// (<c>+604</c>/<c>+608</c>), which has its own textures and its own size.
    /// </summary>
    [Fact]
    public void AnExitCrossingUsesTheMaterialsSecondBlock()
    {
        BulletMarks marks = new();
        Assert.True(marks.TryAdd(Hit(1), Wall, Table(), isEntry: false, exitCrossing: true));

        BulletMark mark = Assert.Single(marks.Marks);
        Assert.Equal("far_wood_exit.tex", mark.Texture);
        Assert.Equal(0.06f * 4096f, mark.SizeInUnits, 2);
    }

    [Fact]
    public void AMarkIsLiftedOffTheSurfaceItIsOn()
    {
        // Otherwise it fights the wall for depth. The lift is the same five millimetres the shot uses
        // to step off a penetrated face.
        BulletMarks marks = new();
        Assert.True(marks.TryAdd(Hit(1), Wall, Table()));

        Assert.Equal(300.0 + 20.48, marks.Marks[0].Position.Z, 3);
    }

    /// <summary>
    /// The mark comes from the row the struck face names, not from one row standing in for every
    /// surface. <c>MefCollisionMesh.GlobalMaterial</c> resolves a face's per-mesh selector to the
    /// global row (<c>0x4C1840</c> writes <c>scene[3388 + 4·u16]</c>), and the collision index reports
    /// that row, so wood must read as wood and glass as glass.
    /// </summary>
    [Fact]
    public void EachStruckFaceTakesItsOwnMaterialsTexture()
    {
        GameMaterialRegistry table = Table();
        BulletMarks marks = new();

        Assert.True(marks.TryAdd(Hit(1), Wall, table));
        Assert.True(marks.TryAdd(Hit(2, x: 5000), Wall, table));

        Assert.Collection(
            marks.Marks,
            wood => Assert.Equal("bullethole_wood.tex", wood.Texture),
            glass => Assert.Equal("hole_plexi1.tex", glass.Texture));
    }

    /// <summary>
    /// A fence is shot through, not marked. Both retail fence rows — <c>Fence</c> and
    /// <c>MetalFence</c> — name no entry texture, and the shot loop skips a material that names none
    /// rather than marking it with a default.
    /// </summary>
    [Fact]
    public void AFenceIsNeverMarked()
    {
        GameMaterialRegistry table = Table();
        BulletMarks marks = new();

        Assert.False(marks.TryAdd(Hit(3), Wall, table));
        Assert.Empty(marks.Marks);
    }

    /// <summary>
    /// Terrain has no object for the decal message to reach — the trace's terrain pass zeroes the
    /// struck task (<c>0x4CC2A0</c>) — so open ground is never marked, whatever its material says.
    /// </summary>
    [Fact]
    public void AHitOnNothingOwnedLeavesNoMark()
    {
        BulletMarks marks = new();

        Assert.False(marks.TryAdd(Hit(1), surface: null, Table()));
        Assert.Empty(marks.Marks);

        WorldTraceHit terrain = new(
            default, new Vector3f(0f, 0f, 1f), 10.0, 0.5, 1, WorldTraceHitKind.Terrain);
        Assert.Null(BulletMarks.SurfaceOf(terrain));
    }

    /// <summary><c>0x47D510</c>: a mark on top of one already there, facing the same way, is skipped.</summary>
    [Fact]
    public void AMarkOnTopOfAnotherIsSkipped()
    {
        GameMaterialRegistry table = Table();
        BulletMarks marks = new();

        Assert.True(marks.TryAdd(Hit(1), Wall, table));

        // Well inside one and a half half-widths of the first.
        Assert.False(marks.TryAdd(Hit(1, x: 100 + 100), Wall, table));

        // Beyond it, a second mark lands.
        Assert.True(marks.TryAdd(Hit(1, x: 100 + 200), Wall, table));
        Assert.Equal(2, marks.Marks.Count);
    }

    /// <summary>The ring in <c>0x47D806</c>: one object keeps 200 marks and overwrites its oldest.</summary>
    [Fact]
    public void OneObjectKeepsItsLatestMarksOnly()
    {
        GameMaterialRegistry table = Table();
        BulletMarks marks = new();

        for (int i = 0; i < BulletMarks.MarksPerSurface + 10; i++)
        {
            Assert.True(marks.TryAdd(Hit(1, x: i * 1000.0), Wall, table));
        }

        Assert.Equal(BulletMarks.MarksPerSurface, marks.Marks.Count);
        Assert.Equal(10 * 1000.0, marks.Marks[0].Position.X, 3);
        Assert.Equal(BulletMarks.MarksPerSurface + 10, marks.TotalAdded);
    }

    /// <summary>
    /// <c>0x47D03A</c> pools four objects' worth of marks. A fifth object takes the pool entry of the
    /// one marked longest ago, and every mark on that one goes.
    /// </summary>
    [Fact]
    public void AFifthMarkedObjectEvictsTheStalest()
    {
        GameMaterialRegistry table = Table();
        BulletMarks marks = new();
        object[] walls = [new(), new(), new(), new(), new()];

        for (int i = 0; i < 4; i++)
        {
            Assert.True(marks.TryAdd(Hit(1), walls[i], table));
        }

        // Touch the first again, so the second is now the stalest.
        Assert.True(marks.TryAdd(Hit(1, x: 5000), walls[0], table));
        Assert.True(marks.TryAdd(Hit(1), walls[4], table));

        Assert.Equal(BulletMarks.MaximumSurfaces, marks.SurfaceCount);
        Assert.DoesNotContain(marks.Marks, mark => ReferenceEquals(mark.Surface, walls[1]));
        Assert.Equal(2, marks.Marks.Count(mark => ReferenceEquals(mark.Surface, walls[0])));
    }

    /// <summary>The marks are a child of the object: when it goes, so do they.</summary>
    [Fact]
    public void ForgettingAnObjectRemovesItsMarks()
    {
        GameMaterialRegistry table = Table();
        BulletMarks marks = new();
        object pane = new();

        Assert.True(marks.TryAdd(Hit(2), pane, table));
        Assert.True(marks.TryAdd(Hit(1), Wall, table));

        marks.Forget(pane);

        Assert.Same(Wall, Assert.Single(marks.Marks).Surface);
        Assert.Equal(1, marks.SurfaceCount);
    }

    /// <summary>
    /// <c>0x47D920</c> drops a mark any of whose four corners has no surface under it, which is what
    /// keeps a hole from hanging off the edge of a wall.
    /// </summary>
    [Fact]
    public void AMarkOverhangingAnEdgeIsDropped()
    {
        GameMaterialRegistry table = Table();
        BulletMarks marks = new();

        // A wall that ends at x = 100: a probe to its left meets it, one to its right meets nothing.
        WorldRayCast edge = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = default;
            return origin.X <= 100.0;
        };

        Assert.False(marks.TryAdd(Hit(1, x: 100), Wall, table, probe: edge));
        Assert.True(marks.TryAdd(Hit(1, x: -1000), Wall, table, probe: edge));
        Assert.Single(marks.Marks);
    }

    /// <summary>
    /// The descendants of <c>RigidDynCubeObj</c> that register a null decal handler take no marks at
    /// all: a switch, a wire and an elevator floor indicator.
    /// </summary>
    [Theory]
    [InlineData("Switch")]
    [InlineData("Wire")]
    [InlineData("ElevatorFloorIndicator")]
    public void TypesThatRefuseTheDecalMessageAreNeverMarked(string typeName)
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        GameTask task = tree.New(tree.Root, types.Register<PlainTask>(typeName));

        Assert.True(BulletMarks.RefusesMarks(task, placement: null));
        Assert.Null(BulletMarks.SurfaceOf(task, placement: null));
    }

    /// <summary>
    /// <c>0x47D380</c> hands the decal its parent's live position and orientation, and <c>0x47D920</c>
    /// keeps the corners in the parent's frame, so a mark on a door swings and slides with the door.
    /// </summary>
    [Fact]
    public void AMarkMovesWithThePlacementItIsOn()
    {
        GameMaterialRegistry table = Table();
        (Vector3d Position, Matrix3f Orientation) door = (default, Matrix3f.Identity);
        WorldPlacement placement = new(default, default, default, null!, null!, "door", null!);
        bool Pose(WorldPlacement p, out Vector3d position, out Matrix3f orientation)
        {
            Assert.Same(placement, p);
            (position, orientation) = door;
            return true;
        }

        BulletMarks marks = new() { Poses = Pose };
        WorldRayHit hit = new(new Vector3d(100, 0, 0), new Vector3f(1f, 0f, 0f), 10.0, 1);
        Assert.True(marks.TryAdd(hit, placement, table));
        BulletMark mark = Assert.Single(marks.Marks);
        Assert.Same(placement, mark.Anchor);

        // The door turns a quarter about Z -- its local X now points along world Y -- and slides a
        // metre along X. Column-vector convention: this matrix takes (1,0,0) to (0,1,0).
        door = (new Vector3d(1, 0, 0), Matrix3f.FromRows(new(0f, -1f, 0f), new(1f, 0f, 0f), new(0f, 0f, 1f)));

        Assert.True(mark.TryResolve(Pose, out Vector3d position, out Vector3f normal, out _));
        Assert.Equal(WorldScene.UnitsPerMetre, position.X, 3);
        Assert.Equal(mark.Position.X, position.Y, 3);
        Assert.Equal(0.0, position.Z, 3);
        Assert.Equal(0f, normal.X, 5);
        Assert.Equal(1f, normal.Y, 5);

        // A second round on the same spot of the moved door lands where the first is in the door's own
        // frame, so the overlap rule refuses it even though the first mark was made a metre away.
        WorldRayHit again = new(new Vector3d(WorldScene.UnitsPerMetre, 100, 0), new Vector3f(0f, 1f, 0f), 10.0, 1);
        Assert.False(marks.TryAdd(again, placement, table));
    }

    /// <summary>A mark nothing owns, or one made with no pose to hand, stays where it was made.</summary>
    [Fact]
    public void AMarkWithNoPoseStaysWhereItWasMade()
    {
        GameMaterialRegistry table = Table();
        WorldPlacement placement = new(default, default, default, null!, null!, "crate", null!);
        BulletMarks marks = new();

        Assert.True(marks.TryAdd(Hit(1), placement, table));
        BulletMark mark = Assert.Single(marks.Marks);
        Assert.Null(mark.Anchor);
        Assert.False(mark.TryResolve(null, out Vector3d position, out _, out _));
        Assert.Equal(mark.Position, position);
    }

    private sealed class PlainTask : GameTask;
}
