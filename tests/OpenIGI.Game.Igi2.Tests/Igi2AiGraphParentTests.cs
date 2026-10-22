using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A graph hung from a model object (a train, in the train mission) keeps its nodes in that object's frame and reads them through it,
/// <c>Graph_GetNodePos</c>, so they ride with it.
/// </summary>
public sealed class Igi2AiGraphParentTests
{
    [Fact]
    public void NodeRidesItsParent()
    {
        Parent train = new() { AttachPosition = new Vector3d(1000.0, 2000.0, 30.0) };
        Igi2AiGraphNode node = new(0, new Vector3d(100.0, 0.0, 10.0), 0f, 64f, -1, 0f, 0, string.Empty, 0, train);
        Assert.Equal(new Vector3d(1100.0, 2000.0, 40.0), node.Position);

        // A quarter turn: x now runs along y, and the node follows the train on.
        train.AttachOrientation = [0f, -1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];
        train.AttachPosition = new Vector3d(5000.0, 2000.0, 30.0);
        Assert.Equal(new Vector3d(5000.0, 2100.0, 40.0), node.Position);
        Assert.Equal(new Vector3d(100.0, 0.0, 10.0), node.LocalPosition);
    }

    [Fact]
    public void NodeWithoutParentStaysPut()
    {
        Igi2AiGraphNode node = new(0, new Vector3d(100.0, 0.0, 10.0), 0f, 64f, -1, 0f, 0, string.Empty, 0);
        Assert.Equal(node.LocalPosition, node.Position);
    }

    private sealed class Parent : IAttachParent
    {
        public Vector3d AttachPosition { get; set; }

        public IReadOnlyList<float> AttachOrientation { get; set; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
    }
}
