using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.TestSupport.QvmExpression;

namespace OpenIGI.Game.Tests;

public sealed class CarAiNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADisconnectedDestinationNeverBecomesADirectDrivingTarget(bool connected)
    {
        GameLoop loop = new();
        QvmProgram script = QvmProgram.Read(BuildProgram(
            Call("Task_DeclareParameters", Text("Car"), Text("Position"), Text("ObjectPos")),
            Call("Task_DeclareParameters", Text("CarAI"),
                Text("Car ID"), Text("Int32"), Text("Graph ID"), Text("Int32"), Text("Route ID"), Text("Int32")),
            Call("Task_DeclareParameters", Text("PatrolPath")),
            Call("Task_DeclareParameters", Text("PatrolPathCommand"),
                Text("Command"), Text("Int32"), Text("Command Parameter"), Text("Int32")),
            Call("Task_New", Int(10), Text("Car"), Text("car"), Real(0), Real(0), Real(0)),
            Call("Task_New", Int(11), Text("CarAI"), Text("controller"), Int(10), Int(20), Int(30)),
            Call("Task_New", Int(30), Text("PatrolPath"), Text("route"),
                Call("Task_New", Int(-1), Text("PatrolPathCommand"), Text("speed"), Int(8), Int(36)),
                Call("Task_New", Int(-1), Text("PatrolPathCommand"), Text("destination"), Int(2), Int(2)),
                Call("Task_New", Int(-1), Text("PatrolPathCommand"), Text("quit"), Int(7), Int(-1)))));
        Assert.Equal(QvmError.None, new QvmInterpreter(script, loop.Natives).Run());
        GameTask car = Assert.IsType<DeclaredTask>(loop.Tasks.FromId(10));
        CutsceneRuntime runtime = new(loop.Tasks.Root, loop.Variables);
        runtime.ConfigureCarAi(new Dictionary<int, GraphFile> { [20] = TwoNodeGraph(connected) });
        for (int tick = 0; tick < 20; tick++)
        {
            runtime.Tick();
        }
        Assert.True(runtime.TryGetDrawTransform(car, out Vector3d position, out _));
        // IGI1 0x425C10 returns the nearest node when no link exists. It never substitutes
        // the disconnected destination. The reachable control proves movement actually ticks.
        if (connected)
        {
            Assert.True(position.X > 1d);
        }
        else
        {
            Assert.Equal(Vector3d.Zero, position);
        }
    }

    private static GraphFile TwoNodeGraph(bool connected)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(GraphFile.Magic);
        Field("nMaxNodes", GraphValueType.Int32);
        writer.Write(3);
        // All entries are unreachable except the optional 1 -> 2 control case.
        FieldHeader("RouteTable", GraphValueType.Blob);
        writer.Write((byte)GraphValueType.Bool8);
        writer.Write((byte)GraphValueType.Bool8);
        writer.Write((byte)GraphValueType.Int32);
        writer.Write(3 * 3 * 8);
        for (int to = 0; to < 3; to++)
        {
            for (int from = 0; from < 3; from++)
            {
                bool reachable = connected && from == 1 && to == 2;
                writer.Write(reachable ? 2 : -1);
                writer.Write(reachable ? 409600f : -1f);
            }
        }
        for (int id = 1; id <= 2; id++)
        {
            Field("nNodeID", GraphValueType.Int32);
            writer.Write(id);
            Field("tNodePosition", GraphValueType.Real64x3);
            writer.Write((id - 1) * 409600d);
            writer.Write(0d);
            writer.Write(0d);
            Field("nNodeCriteria", GraphValueType.String8);
            writer.Write((byte)1);
            writer.Write((byte)0);
        }
        return GraphFile.Read(stream.ToArray());

        void Field(string name, GraphValueType type)
        {
            FieldHeader(name, type);
            writer.Write((byte)type);
        }
        void FieldHeader(string name, GraphValueType type)
        {
            writer.Write((byte)GraphValueType.UInt32);
            writer.Write(GraphFieldHash.Of(name));
            writer.Write((byte)GraphValueType.Bool8);
            writer.Write((byte)type);
        }
    }
}
