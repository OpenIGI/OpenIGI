using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class WorldInteractionTests
{
    [Fact]
    public void LevelConditionsUseRetailBooleanConstantsAndEmptyDefaults()
    {
        NativeRegistry natives = new();
        ScriptVarRegistry variables = new(natives);
        LevelExpressionRuntime expressions = new(variables);

        Assert.True(expressions.EvaluateCondition("TRUE && !FALSE", emptyValue: false));
        Assert.True(expressions.EvaluateCondition(string.Empty, emptyValue: true));
        Assert.False(expressions.EvaluateCondition(string.Empty, emptyValue: false));

        natives.RegisterVariable("Lift.vFloor", new QvmFloatVariable(() => 0.00025, _ => { }));
        Assert.False(expressions.EvaluateCondition("Lift.vFloor == 0", emptyValue: true));
    }

    [Fact]
    public void DoorDrawTransformFollowsBothMotionChannels()
    {
        DoorState state = new(maxAngleDegrees: 90f, openTimeSeconds: 1f, slides: true);
        PlacedDoor door = new(
            new LevelTask(),
            new Vector3d(4096.0, 8192.0, 12288.0),
            Matrix3f.Identity,
            1f,
            0f,
            0f,
            state);

        state.CommandOpen();
        for (int i = 0; i < 30; i++)
        {
            state.Tick();
        }

        Assert.Equal(new Vector3d(2.0, 2.0, 3.0), door.DrawPosition);
        Assert.Equal(90f, state.AngleDegrees);
    }

    [Theory]
    [InlineData(4, 31)]
    [InlineData(5, 32)]
    [InlineData(6, 33)]
    [InlineData(7, 34)]
    [InlineData(12, 35)]
    [InlineData(13, 36)]
    public void RetailSelfDrivingTypesEnterTheirExactState(int type, int state)
    {
        Assert.Equal(state, OpenIGI.Game.Player.HumanInteractionDispatch.Dispatch(type).LocomotionState);
    }
}
