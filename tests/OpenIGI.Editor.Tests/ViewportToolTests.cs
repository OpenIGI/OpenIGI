using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The viewport's geometry: volume boxes in both games' storage, and AI graph frames in both games' rules.</summary>
public sealed class ViewportToolTests
{
    private const string Sample = """
        Task_DeclareParameters("AreaActivate", "Position", "ObjectPos", "Orientation", "Real32x9", "Dimensions", "Real32x3");
        Task_DeclareParameters("AmbientArea", "Position", "ObjectPos", "Orientation", "Real32x9", "Size", "Real64x3");
        Task_DeclareParameters("Building", "Position", "ObjectPos", "Orientation", "Real32x9", "Model", "String16");
        Task_DeclareParameters("AIGraph", "Graph position", "ObjectPos");
        Task_DeclareParameters("Container", "Unused", "Int32");
        Task_New(-1, "Container", "Root", 0,
        	Task_New(1, "AreaActivate", "Trigger", 4096, 0, 0, 0, 0, 0, 4096, 8192, 16384),
        	Task_New(2, "AmbientArea", "Birds", 0, 0, 0, 0, 0, 0, 1.5, 2.5, 3.5),
        	Task_New(3, "Building", "Hut", 40960, 0, 0, 0, 0, 1.5707963, "hut",
        		Task_New(4, "AIGraph", "", 4096, 0, 0)),
        	Task_New(5, "AIGraph", "", 0, 4096, 0));
        """;

    private const string Igi2Sample = """
        Task_DeclareParameters("Building", "Position", "ObjectPos", "Orientation", "Real32x9", "Model", "String16");
        Task_DeclareParameters("AIGraph", "Graph position", "ObjectPos", "Relative", "bool8");
        Task_New(-1, "Building", "Hut", 40960, 0, 0, 0, 0, 1.5707963, "hut",
        	Task_New(4, "AIGraph", "", 4096, 0, 0, TRUE),
        	Task_New(5, "AIGraph", "", 4096, 0, 0, FALSE));
        """;

    /// <summary>A trigger area stores its full size in world units, an ambient area half its size in metres.</summary>
    [Fact]
    public void Volumes_read_and_write_their_own_storage()
    {
        MissionDocument document = Read(Sample);
        TaskVolume trigger = TaskVolume.Of(Task(document, "Trigger"))!.Value;
        Assert.Equal(new Vector3d(0.5, 1.0, 2.0) * (4096.0 / WorldUnits.PerMetre), trigger.HalfMetres());
        Assert.Equal([4096.0, 8192.0, 16384.0], trigger.Stored(trigger.HalfMetres()!.Value), Close);

        TaskVolume birds = TaskVolume.Of(Task(document, "Birds"))!.Value;
        Assert.Equal(new Vector3d(1.5, 2.5, 3.5), birds.HalfMetres());
        Assert.Equal([1.5, 2.5, 3.5], birds.Stored(new Vector3d(1.5, 2.5, 3.5)), Close);

        Assert.Null(TaskVolume.Of(Task(document, "Hut")));
    }

    /// <summary>A box's corners are its local corners turned by the transposed orientation, as AreaActivation builds them.</summary>
    [Fact]
    public void A_box_stands_in_its_task_frame()
    {
        Matrix3f turn = Matrix3f.FromEngineEulerAngles(0f, 0f, MathF.PI / 2f);
        BoxGizmo.Box box = new(new Vector3d(10, 0, 0), turn, new Vector3d(1, 2, 3));

        Vector3d corner = box.Corner(0);
        Vector3d expected = new Vector3d(10, 0, 0) + turn.Transposed().Transform(new Vector3d(1, 2, 3));
        Assert.True((corner - expected).Length < 1e-5, $"{corner} is not {expected}");

        // Opposite faces sit either side of the centre, each half the size away.
        Assert.True((box.FaceCentre(4) - new Vector3d(10, 0, 3)).Length < 1e-5);
        Assert.True(((box.FaceCentre(0) + box.FaceCentre(1)) * 0.5 - box.Centre).Length < 1e-5);
        Assert.True(Math.Abs((box.FaceCentre(2) - box.Centre).Length - 2.0) < 1e-5);
    }

    /// <summary>
    /// I.G.I.: a graph under a placed model is turned by the model and moved to it; one in the open is its position plus
    /// the node. Each frame's inverse gives back what it was given.
    /// </summary>
    [Fact]
    public void Igi1_graph_frames()
    {
        MissionDocument document = Read(Sample);
        MissionTask inHut = document.EnumerateTasks().Single(static t => t.Id == 4);
        MissionTask open = document.EnumerateTasks().Single(static t => t.Id == 5);
        Vector3d node = new(0, 4096, 0);

        // The hut stands at 10 m and is turned a quarter about Z; the graph sits 1 m along the hut's own X.
        Matrix3f hut = Matrix3f.FromEngineEulerAngles(0f, 0f, 1.5707963f);
        Vector3d expected = (new Vector3d(40960, 0, 0) + hut.Transform(new Vector3d(4096, 4096, 0))) / WorldUnits.PerMetre;
        AssertClose(expected, MissionGraphs.ToWorldMetres(inHut, node));
        AssertClose(node, MissionGraphs.ToLocalUnits(inHut, MissionGraphs.ToWorldMetres(inHut, node)));

        AssertClose(new Vector3d(0, 2, 0), MissionGraphs.ToWorldMetres(open, node));
        AssertClose(node, MissionGraphs.ToLocalUnits(open, new Vector3d(0, 2, 0)));

        // Moving a graph's origin goes back through the same frame to the value its parameter holds.
        AssertClose(new Vector3d(4096, 0, 0), MissionGraphs.OffsetForOrigin(inHut, MissionGraphs.OriginMetres(inHut)));
    }

    /// <summary>I.G.I. 2: a relative graph rides its parent and ignores its position; any other is position plus node, in the world.</summary>
    [Fact]
    public void Igi2_graph_frames()
    {
        MissionDocument document = Read(Igi2Sample, QvmProgram.Igi2Version);
        MissionTask relative = document.EnumerateTasks().Single(static t => t.Id == 4);
        MissionTask world = document.EnumerateTasks().Single(static t => t.Id == 5);
        Vector3d node = new(0, 4096, 0);

        Matrix3f hut = Matrix3f.FromEngineEulerAngles(0f, 0f, 1.5707963f);
        AssertClose((new Vector3d(40960, 0, 0) + hut.Transform(node)) / WorldUnits.PerMetre, MissionGraphs.ToWorldMetres(relative, node));
        AssertClose(new Vector3d(1, 1, 0), MissionGraphs.ToWorldMetres(world, node));
        AssertClose(node, MissionGraphs.ToLocalUnits(relative, MissionGraphs.ToWorldMetres(relative, node)));
    }

    private static readonly IEqualityComparer<double> Close = EqualityComparer<double>.Create(static (a, b) => Math.Abs(a - b) < 1e-3, static _ => 0);

    private static void AssertClose(Vector3d expected, Vector3d actual) =>
        Assert.True((expected - actual).Length < 1e-3, $"Expected {expected}, got {actual}.");

    private static MissionTask Task(MissionDocument document, string label) => document.EnumerateTasks().Single(t => t.Label == label);

    private static MissionDocument Read(string script, int version = QvmProgram.IgiVersion) =>
        MissionDocument.Read(QscParser.ParseScript(script, version));
}
