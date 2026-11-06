using System.Buffers.Binary;
using OpenIGI.Formats.World;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>A <c>terrain.hmp</c> changed in place and written back.</summary>
public sealed class TerrainHeightmapWriteTests
{
    [Fact]
    public void AnUnchangedHeightmapWritesBackAsItWasRead()
    {
        byte[] file = Make();
        TerrainHeightmap map = TerrainHeightmap.Read(file);
        Assert.Equal(file, map.Write());
        Assert.Equal(0, map.Revision);
    }

    [Fact]
    public void ASetCornerChangesThatSampleAloneAndIsSeenByTheGrid()
    {
        byte[] file = Make();
        TerrainHeightmap map = TerrainHeightmap.Read(file);
        Assert.True(map.TryGetGrid(1, out TerrainHeightGrid grid));

        map.SetCorner(1, 2, 3, 70);
        Assert.Equal(70, grid.Corner(2, 3));
        Assert.Equal(1, map.Revision);

        map.SetCorner(1, 2, 3, 70);
        Assert.Equal(1, map.Revision);

        map.SetCorner(1, 0, 0, -500);
        Assert.Equal(sbyte.MinValue, grid.Corner(0, 0));

        byte[] written = map.Write();
        int block = TerrainGridTable.TableSize;
        Assert.Equal(70, (sbyte)written[block + (3 * 9) + 2]);
        Assert.Equal(2, file.Zip(written).Count(static pair => pair.First != pair.Second));
        Assert.Equal(64, file[block + (3 * 9) + 2]);
    }

    [Fact]
    public void ACornerOfAGridThatIsNotThereIsRefused()
    {
        TerrainHeightmap map = TerrainHeightmap.Read(Make());
        Assert.Throws<ArgumentOutOfRangeException>(() => map.SetCorner(2, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.SetCorner(1, 9, 0, 1));
    }

    [Fact]
    public void AGridAddedAndTakenAwayLeavesTheFileAsItWas()
    {
        byte[] file = Make();
        TerrainHeightmap map = TerrainHeightmap.Read(file);
        map.SetCorner(1, 4, 4, 90);
        byte[] edited = map.Write();
        Assert.Equal(0, map.FirstFreeId());

        map.AddGrid(0, 16);
        Assert.Equal(file.Length + (17 * 17), map.Write().Length);
        Assert.True(map.TryGetGrid(0, out TerrainHeightGrid added));
        Assert.Equal(16, added.Size);
        Assert.Equal(64, added.Corner(16, 16));
        Assert.True(map.TryGetGrid(1, out TerrainHeightGrid kept));
        Assert.Equal(90, kept.Corner(4, 4));
        Assert.True(map.IsExact);

        map.RemoveGrid(0);
        Assert.Equal(edited, map.Write());
        Assert.Throws<ArgumentOutOfRangeException>(() => map.RemoveGrid(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.AddGrid(1, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.AddGrid(5, 12));
    }

    [Fact]
    public void ANewHeightmapIsAnEmptyTableTheOriginalReadsAsNoGrids()
    {
        TerrainHeightmap map = TerrainHeightmap.Create();
        Assert.Equal(0, map.GridCount);
        Assert.True(map.IsExact);
        map.AddGrid(map.FirstFreeId(), 128);
        Assert.Equal(TerrainGridTable.TableSize + (129 * 129), map.Write().Length);
        Assert.Equal(128u, BinaryPrimitives.ReadUInt32LittleEndian(map.Write().AsSpan(8)));
    }

    // One 8-cell grid at id 1, flat.
    private static byte[] Make()
    {
        byte[] file = new byte[TerrainGridTable.TableSize + 81];
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(TerrainGridTable.RecordStride + 8), 8);
        file.AsSpan(TerrainGridTable.TableSize).Fill(64);
        return file;
    }
}
