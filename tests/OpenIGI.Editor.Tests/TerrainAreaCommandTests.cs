using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Terrain;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A sculptable area added to a mission: its task, written as the retail ones are, and taken out again on undo.</summary>
public sealed class TerrainAreaCommandTests
{
    private const string Sample = """
        Task_DeclareParameters("EditRigidObj", "Position", "ObjectPos", "Orientation", "Real32x9", "Model", "String16");
        Task_New(-1, "Static", "",
        	Task_New(-1, "Container", "TerrainModifiers",
        		Task_New(-1, "Container", "TerrainTexturing")));
        """;

    private static readonly TerrainArea Area = new(
        3,
        "HeightMap",
        new Vector3d(6000.0, -13636.0, 42582.0),
        [new("Position", "ObjectPos"), new("Level", "Int32"), new("Size", "Int32"), new("isEdit", "bool8"), new("Bitmap ID", "Int32"), new("NORMALSMOOTH", "Int32"), new("BLURSMOOTH", "Int32")],
        [new("Level", 11), new("Size", 128), new("Bitmap ID", 3), new("NORMALSMOOTH", 8), new("BLURSMOOTH", 7)],
        "heightmap 3");

    /// <summary>The task lands in the terrain container with level 1's settings, its type declared beside the others; undo restores the script.</summary>
    [Fact]
    public void Adds_a_retail_shaped_task_and_its_declaration()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript(Sample, QvmProgram.IgiVersion));
        string before = QscPrinter.Print(document.ToScript());
        FakeTerrain terrain = new();

        AddTerrainAreaCommand command = AddTerrainAreaCommand.For(document, terrain, Area);
        command.Apply();
        string after = QscPrinter.Print(document.ToScript());
        Assert.Contains("Task_DeclareParameters(\"HeightMap\", \"Position\", \"ObjectPos\", \"Level\", \"Int32\", \"Size\", \"Int32\", \"isEdit\", \"bool8\", \"Bitmap ID\", \"Int32\", \"NORMALSMOOTH\", \"Int32\", \"BLURSMOOTH\", \"Int32\")", after, StringComparison.Ordinal);
        Assert.True(after.Contains("Task_New(-1, \"HeightMap\", \"\", 24576000.0, -55853056.0, 174415870.0, 11, 128, 0, 3, 8, 7)", StringComparison.Ordinal), after);
        Assert.Equal("TerrainModifiers", command.Task.Parent?.Label);
        Assert.Equal(0, terrain.Added);

        command.Undo();
        Assert.Equal(before, QscPrinter.Print(document.ToScript()));
        Assert.Equal(1, terrain.Removed);

        command.Apply();
        Assert.Equal(1, terrain.Added);
        Assert.Equal(after, QscPrinter.Print(document.ToScript()));
    }

    private sealed class FakeTerrain : IEditableTerrain
    {
        public int Added { get; private set; }

        public int Removed { get; private set; }

        public IReadOnlyList<ITerrainHeightGrid> Grids => [];

        public bool IsPartial => true;

        public bool TryGetHeight(double xMetres, double yMetres, out double zMetres)
        {
            zMetres = 0.0;
            return false;
        }

        public void Refresh(ITerrainHeightGrid grid, int left, int top, int right, int bottom)
        {
        }

        public IReadOnlyList<(string MissionPath, byte[] Bytes)> ChangedFiles(Func<string, ReadOnlyMemory<byte>?> read) => [];

        public void MarkSaved()
        {
        }

        public TerrainArea? AddArea(Vector3d pointMetres, int? id = null)
        {
            Added++;
            return Area;
        }

        public void RemoveArea(TerrainArea area) => Removed++;
    }
}
