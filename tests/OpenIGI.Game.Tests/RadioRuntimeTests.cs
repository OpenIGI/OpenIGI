using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A <c>Radio</c>'s own update, <c>0x43AF90</c>.</summary>
public sealed class RadioRuntimeTests
{
    [Fact]
    public void ARadioTalksOnlyWhenSilentAndWaitsBetweenSounds()
    {
        (RadioRuntime radio, _, IgiRandom expected) = CreateRadio("1");
        FakeSounds sounds = new();

        // Its first update finds the countdown at 0 and talks at once: one rand(4) for the slot, one rand(150).
        radio.Tick(sounds);
        Assert.True(radio.IsOn);
        string first = $"chatter{expected.NextBounded(RadioRuntime.SoundSlots)}";
        int gap = (int)expected.NextBounded(RadioRuntime.SoundGapTicks) + RadioRuntime.SoundGapTicks;
        Assert.Equal([first], sounds.Played);
        Assert.Equal(gap, radio.Countdown);

        // While that sound plays the countdown stands still.
        sounds.Playing = true;
        for (int tick = 0; tick < 50; tick++)
        {
            radio.Tick(sounds);
        }

        Assert.Equal(gap, radio.Countdown);

        // Once it ends, the countdown runs, and only at 0 does the radio talk again.
        sounds.Playing = false;
        for (int tick = 0; tick < gap; tick++)
        {
            radio.Tick(sounds);
        }

        Assert.Equal(0, radio.Countdown);
        Assert.Single(sounds.Played);
        radio.Tick(sounds);
        Assert.Equal(2, sounds.Played.Count);
    }

    [Fact]
    public void AnExplodedRadioCutsItsSoundAndShutsDownOnce()
    {
        (RadioRuntime radio, Action explode, _) = CreateRadio("1");
        FakeSounds sounds = new();
        radio.Tick(sounds);
        sounds.Playing = true;

        explode();
        radio.Tick(sounds);
        radio.Tick(sounds);

        Assert.False(radio.IsOn);
        Assert.Equal(1, sounds.Silenced);
        Assert.Equal("shutdown", sounds.Played[^1]);
        Assert.Equal(2, sounds.Played.Count);
    }

    [Fact]
    public void ARadioWithNoOnExpressionStaysOff()
    {
        (RadioRuntime radio, _, _) = CreateRadio(string.Empty);
        FakeSounds sounds = new();

        radio.Tick(sounds);

        Assert.False(radio.IsOn);
        Assert.Empty(sounds.Played);
    }

    private static (RadioRuntime Radio, Action Explode, IgiRandom Expected) CreateRadio(string onExpression)
    {
        GameLoop loop = new();
        QvmProgram script = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("Radio"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Radio sound 0"), QvmExpression.Text("String16"),
                QvmExpression.Text("Radio sound 1"), QvmExpression.Text("String16"),
                QvmExpression.Text("Radio sound 2"), QvmExpression.Text("String16"),
                QvmExpression.Text("Radio sound 3"), QvmExpression.Text("String16"),
                QvmExpression.Text("Shutdown sound"), QvmExpression.Text("String16"),
                QvmExpression.Text("On expression"), QvmExpression.Text("VarString")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(3366),
                QvmExpression.Text("Radio"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
                QvmExpression.Text("chatter0"),
                QvmExpression.Text("chatter1"),
                QvmExpression.Text("chatter2"),
                QvmExpression.Text("chatter3"),
                QvmExpression.Text("shutdown"),
                QvmExpression.Text(onExpression))));
        Assert.Equal(QvmError.None, new QvmInterpreter(script, loop.Natives).Run());

        DeclaredTask task = Assert.IsType<DeclaredTask>(loop.Tasks.FromId(3366));
        bool exploded = false;
        loop.Variables.BindBool(task, "isExploded", () => exploded, _ => { });
        RadioRuntime radio = RadioRuntime.Create(
            task, new LevelExpressionRuntime(loop.Variables), loop.Variables, new IgiRandom(7u));
        return (radio, () => exploded = true, new IgiRandom(7u));
    }

    private sealed class FakeSounds : IRadioSoundPlayer
    {
        public List<string> Played { get; } = [];

        public bool Playing { get; set; }

        public int Silenced { get; private set; }

        public object? Play(string sound, Vector3d positionUnits)
        {
            Played.Add(sound);
            return sound;
        }

        public bool IsPlaying(object voice) => Playing;

        public void Silence(object voice) => Silenced++;
    }
}
