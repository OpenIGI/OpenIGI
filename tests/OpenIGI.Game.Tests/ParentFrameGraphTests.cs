using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>AIGraphs authored inside a placed model, which the original reads in that model's frame.</summary>
/// <remarks>
/// <c>0x4F90B0</c> marks an AIGraph (<c>+100</c>) when its parent derives from <c>RigidDynCubeObj</c>, and
/// <c>0x4F9770</c> then turns node + graph position by the parent's 3x3 and adds the parent's position. Mission 9's
/// intro office is Building 472_01_1; its graph 18 carries Anya's node. Offset by the graph position alone the node
/// sat 42 km under the level, and so did Anya.
/// </remarks>
public sealed class ParentFrameGraphTests
{
    /// <summary>Building 472_01_1's placement in mission 9: position, orientation (0, 0, 0).</summary>
    private static readonly Vector3d OfficePosition = new(23073294d, -54104592d, 174258624d);

    /// <summary>Graph 18's own "Graph position", in the office's frame.</summary>
    private static readonly Vector3d GraphPosition = new(20109.203125d, -3438.180419921875d, 19802.546875d);

    /// <summary>Anya (HumanAI 1234) as placed, which is where the original keeps her origin through the scene.</summary>
    private static readonly Vector3d AnyaOrigin = new(23099130d, -54102716d, 174266624d);

    [RequiresGameDataFact]
    public void MissionNineOfficeGraphLandsUnderAnya()
    {
        string path = Path.Combine(GameData.RequireRoot(), "MISSIONS", "location0", "LEVEL9", "GRAPHS", "GRAPH18.DAT");
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));

        graph.ApplyWorldOffset(GraphPosition, OfficePosition, RetailOrientation.Euler(0f, 0f, 0f));

        Assert.True(graph.InParentFrame);
        GraphNode node = Assert.Single(graph.Nodes);
        Assert.InRange(Math.Abs(node.Position.X - AnyaOrigin.X), 0d, 5d * 4096d);
        Assert.InRange(Math.Abs(node.Position.Y - AnyaOrigin.Y), 0d, 5d * 4096d);

        // The floor under her: below the pelvis origin, within one metre.
        Assert.InRange(AnyaOrigin.Z - node.Position.Z, 0d, 4096d);
    }
}
