using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="LampSwitchboard"/>.
/// </summary>
/// <remarks>
/// <para>
/// The thing worth pinning is not that a lamp can be switched — a boolean does that. It is that the
/// switch is not a boolean: a fitting takes time to go out and rather longer to come back, and the
/// failure everything here guards against is that shape quietly collapsing into a step. An instant
/// change would pass any test that only asked what state the lamp ended in, and would look exactly like
/// a texture being swapped.
/// </para>
/// <para>
/// The rest is the two ways a fade goes wrong without looking wrong: never finishing, so a switched-off
/// lamp sits at a thousandth of its output occupying one of the shader's sixty-four slots forever; and
/// depending on the frame rate, so the same key press produces a different fade on a different machine.
/// </para>
/// </remarks>
public sealed class LampSwitchboardTests
{
    /// <summary>Advances a board in small steps, as the frame loop does.</summary>
    private static void Run(LampSwitchboard board, float seconds, float step = 1f / 60f)
    {
        for (float t = 0f; t < seconds; t += step)
        {
            board.Update(step);
        }
    }

    private static LightFixture Fixture(double x, double y, double z, float extent = 0.2f) =>
        new("lamp", new Vector3d(x, y, z), 8f, Vector3f.One, 1f, extent, 4);

    private static LampSwitchboard Board(params LightFixture[] fixtures) => new(fixtures, seed: 7);

    /// <summary>A level as it ships has every lamp at full output.</summary>
    [Fact]
    public void EveryLampStartsOn()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0), Fixture(4, 0, 0));

        Assert.Equal(2, board.Count);
        Assert.True(board.IsUntouched);
        Assert.Equal(LampMode.On, board.ModeOf(0));
        Assert.Equal(1f, board.LevelOf(0));
        Assert.Equal(1f, board.LevelOf(1));
    }

    /// <summary>An index naming no lamp answers as a lamp that is simply on, rather than throwing.</summary>
    /// <remarks>
    /// Everything downstream multiplies by <see cref="LampSwitchboard.LevelOf"/> on the draw path, and a
    /// placement bound to a fixture the list no longer has must dim nothing rather than take the frame
    /// down.
    /// </remarks>
    [Fact]
    public void AnIndexThatNamesNoLampIsSimplyOn()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        Assert.Equal(1f, board.LevelOf(-1));
        Assert.Equal(1f, board.LevelOf(9));
        Assert.Equal(LampMode.On, board.ModeOf(9));
        Assert.Equal(Vector3f.One, board.TintOf(9));
        Assert.False(board.IsFaulty(9));
    }

    /// <summary>Switching a lamp off does not put it out on the frame the key was pressed.</summary>
    /// <remarks>
    /// The whole point. A light that reaches zero in one frame is a texture swap, and no amount of curve
    /// documentation stops somebody replacing this with an assignment later.
    /// </remarks>
    [Fact]
    public void SwitchingOffTakesTime()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.Switch(0);
        Assert.Equal(LampMode.Off, board.ModeOf(0));
        Assert.Equal(1f, board.LevelOf(0));

        // One frame in it is dimmer, and still lit: the fast term has taken a chunk out of it and the
        // slow one has not finished.
        board.Update(1f / 60f);
        Assert.InRange(board.LevelOf(0), 0.05f, 0.95f);
    }

    /// <summary>And it reaches zero, rather than approaching it for the rest of the level.</summary>
    /// <remarks>
    /// A pair of exponentials never arrives. Left like that, a switched-off lamp keeps a slot in the
    /// shader's sixty-four-light array forever and the light it displaces is a real one somebody can see.
    /// </remarks>
    [Fact]
    public void SwitchingOffFinishes()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.Switch(0);
        Run(board, 1.5f);

        Assert.Equal(0f, board.LevelOf(0));
    }

    /// <summary>Coming back on is slower than going out, and passes through the middle.</summary>
    /// <remarks>
    /// A tube strikes and warms; a filament heats. Either way the lamp spends a visible stretch neither
    /// dark nor full, which is what the sample here catches — and it is not there at all if somebody
    /// replaces the warm-up with an assignment to one.
    /// </remarks>
    [Fact]
    public void ComingBackOnTakesLongerThanGoingOut()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.Switch(0);
        Run(board, 1.5f);

        board.Switch(0);
        Assert.Equal(LampMode.On, board.ModeOf(0));

        bool partway = false;

        for (int frame = 0; frame < 30; frame++)
        {
            board.Update(1f / 60f);
            float level = board.LevelOf(0);

            Assert.InRange(level, 0f, 1f);
            partway |= level is > 0.02f and < 0.98f;
        }

        Assert.True(partway, "The lamp went from dark to full without ever being partly lit.");

        Run(board, 2f);
        Assert.Equal(1f, board.LevelOf(0));
        Assert.True(board.IsUntouched);
    }

    /// <summary>A fade advanced in coarse steps ends up where a fade advanced in fine ones does.</summary>
    /// <remarks>
    /// The curves are functions of accumulated seconds rather than of frames, and this is what says so.
    /// Get it wrong and a lamp fades at half speed on a machine running at half the frame rate — which is
    /// the sort of thing that is never noticed until two captures of the same scene disagree.
    /// </remarks>
    [Fact]
    public void TheFadeDoesNotDependOnTheFrameRate()
    {
        LampSwitchboard fast = Board(Fixture(0, 0, 0));
        LampSwitchboard slow = Board(Fixture(0, 0, 0));

        fast.Switch(0);
        slow.Switch(0);

        Run(fast, 0.2f, 1f / 240f);
        Run(slow, 0.2f, 1f / 30f);

        Assert.Equal(fast.LevelOf(0), slow.LevelOf(0), 2);
    }

    /// <summary>A stalled frame does not step a lamp clean over its fade.</summary>
    /// <remarks>
    /// A load hitch hands the loop most of a second. Taken literally it would land past the far end of
    /// both curves, and the switch would look instant on exactly the frames a person is most likely to be
    /// pressing it — during a hitch.
    /// </remarks>
    [Fact]
    public void AStalledFrameDoesNotSkipTheFade()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.Switch(0);
        board.Update(2f);

        Assert.True(board.LevelOf(0) > 0f, "A single long frame put the lamp straight out.");
    }

    /// <summary>A broken lamp goes dark, comes back, and does not settle.</summary>
    [Fact]
    public void ABrokenLampFlickers()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.Break(0);
        Assert.Equal(LampMode.Broken, board.ModeOf(0));
        Assert.True(board.IsFaulty(0));

        float lowest = 1f;
        float highest = 0f;
        int changes = 0;
        float previous = board.LevelOf(0);

        for (int frame = 0; frame < 900; frame++)
        {
            board.Update(1f / 60f);
            float level = board.LevelOf(0);

            Assert.InRange(level, 0f, 1f);
            lowest = MathF.Min(lowest, level);
            highest = MathF.Max(highest, level);

            if (MathF.Abs(level - previous) > 0.2f)
            {
                changes++;
            }

            previous = level;
        }

        Assert.Equal(0f, lowest);
        Assert.True(highest > 0.7f, "A broken lamp never came back near full.");
        Assert.True(changes > 5, $"A broken lamp only jumped {changes} time(s) in fifteen seconds.");
    }

    /// <summary>Two lamps broken at the same moment do not flicker in step.</summary>
    /// <remarks>
    /// The single artefact that gives the whole effect away. Identical flicker across a row of fittings
    /// reads as one effect being played on all of them rather than as several failing tubes, and the only
    /// thing preventing it is that each lamp draws from its own stream.
    /// </remarks>
    [Fact]
    public void BrokenLampsDoNotFlickerInStep()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0), Fixture(4, 0, 0));

        board.Break(0);
        board.Break(1);

        bool differed = false;

        for (int frame = 0; frame < 600 && !differed; frame++)
        {
            board.Update(1f / 60f);
            differed = MathF.Abs(board.LevelOf(0) - board.LevelOf(1)) > 0.25f;
        }

        Assert.True(differed, "Two broken lamps flickered identically for ten seconds.");
    }

    /// <summary>Repairing a broken lamp puts it back on, and switching it does not repair it.</summary>
    /// <remarks>
    /// The switch and the fault are separate facts about a fitting. Pulling the breaker on a dying tube
    /// does not fix the tube, and it has to come back faulty when it is switched on again.
    /// </remarks>
    [Fact]
    public void TheSwitchAndTheFaultAreSeparate()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.Break(0);
        board.Switch(0);
        Assert.Equal(LampMode.Off, board.ModeOf(0));
        Assert.True(board.IsFaulty(0));

        Run(board, 1.5f);
        Assert.Equal(0f, board.LevelOf(0));

        board.Switch(0);
        Assert.Equal(LampMode.Broken, board.ModeOf(0));

        board.Break(0);
        Assert.Equal(LampMode.On, board.ModeOf(0));
        Assert.False(board.IsFaulty(0));

        Run(board, 2f);
        Assert.Equal(1f, board.LevelOf(0));
    }

    /// <summary>Breaking a lamp that is switched off arms the fault without lighting it.</summary>
    [Fact]
    public void BreakingASwitchedOffLampArmsTheFault()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.Switch(0);
        Run(board, 1.5f);

        board.Break(0);
        Assert.Equal(LampMode.Off, board.ModeOf(0));
        Assert.True(board.IsFaulty(0));
        Assert.Equal(0f, board.LevelOf(0));
    }

    /// <summary>Reset puts a switched-off and a broken lamp back the way the level ships them.</summary>
    [Fact]
    public void ResetPutsEveryLampBackOn()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0), Fixture(4, 0, 0));

        board.Switch(0);
        board.Break(1);
        Run(board, 1f);
        Assert.False(board.IsUntouched);

        board.Reset();

        Assert.True(board.IsUntouched);
        Assert.Equal(1f, board.LevelOf(0));
        Assert.Equal(1f, board.LevelOf(1));
    }

    /// <summary>A lamp on its way out reddens, and one that is simply on does not.</summary>
    [Fact]
    public void OnlyACoolingLampIsTinted()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        Assert.Equal(Vector3f.One, board.TintOf(0));

        board.Switch(0);
        Run(board, 0.1f);

        Vector3f tint = board.TintOf(0);

        Assert.Equal(1f, tint.X);
        Assert.True(tint.Z < tint.Y, "A cooling lamp lost as much blue as green.");
        Assert.InRange(tint.Z, 0f, 1f);
    }

    /// <summary>The revision moves while a lamp is fading, and holds still once nothing is happening.</summary>
    /// <remarks>
    /// It is what the surface cache's emitter upload is throttled on, and an upload every frame of every
    /// level would be the whole point of having it thrown away.
    /// </remarks>
    [Fact]
    public void TheRevisionOnlyMovesWhenSomethingDoes()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        int settled = board.Revision;
        Run(board, 0.5f);
        Assert.Equal(settled, board.Revision);

        board.Switch(0);
        Run(board, 0.1f);
        Assert.True(board.Revision > settled, "A lamp faded without the bounce being told.");

        Run(board, 2f);
        int afterwards = board.Revision;
        Run(board, 0.5f);
        Assert.Equal(afterwards, board.Revision);
    }

    /// <summary>The crosshair finds the lamp in front of it, and nothing behind the viewer.</summary>
    [Fact]
    public void TheNearestLampAlongTheRayIsPicked()
    {
        LampSwitchboard board = Board(
            Fixture(0, 10, 0),
            Fixture(0, 4, 0),
            Fixture(0, -4, 0));

        Assert.True(board.TryPick(Vector3d.Zero, Vector3d.UnitY, 60.0, out int index, out double distance));
        Assert.Equal(1, index);
        Assert.Equal(4.0, distance, 3);

        // Behind the viewer is not under the crosshair, however close it is.
        Assert.True(board.TryPick(Vector3d.Zero, -Vector3d.UnitY, 60.0, out int behind, out _));
        Assert.Equal(2, behind);
    }

    /// <summary>A lamp well off the crosshair is not picked, and one just off it is.</summary>
    /// <remarks>
    /// The tolerance is an angle, so it has to widen with range: a fixture half a metre off axis is a
    /// miss at two metres and a hit at thirty. Both directions are asserted, because a tolerance that
    /// only ever grows would make the whole ceiling one target.
    /// </remarks>
    [Fact]
    public void TheAimToleranceIsAnAngle()
    {
        LampSwitchboard near = Board(Fixture(0.5, 2, 0));
        Assert.False(near.TryPick(Vector3d.Zero, Vector3d.UnitY, 60.0, out _, out _));

        LampSwitchboard far = Board(Fixture(0.5, 30, 0));
        Assert.True(far.TryPick(Vector3d.Zero, Vector3d.UnitY, 60.0, out _, out _));
    }

    /// <summary>Nothing within reach means nothing picked, rather than the nearest lamp anywhere.</summary>
    [Fact]
    public void ALampBeyondReachIsNotPicked()
    {
        LampSwitchboard board = Board(Fixture(0, 90, 0));

        Assert.False(board.TryPick(Vector3d.Zero, Vector3d.UnitY, 60.0, out _, out _));
    }

    /// <summary>A placement is bound to the lamp beside it, and to none when there is none beside it.</summary>
    [Fact]
    public void PlacementsBindToTheNearestFixture()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0), Fixture(10, 0, 0));

        Assert.Equal(0, board.NearestFixture(new Vector3d(1, 0, 0), 4.0));
        Assert.Equal(1, board.NearestFixture(new Vector3d(9, 0, 0), 4.0));
        Assert.Equal(-1, board.NearestFixture(new Vector3d(5, 0, 0), 4.0));
    }

    /// <summary>A level with no lamps at all is a board nothing throws on.</summary>
    [Fact]
    public void AnEmptyLevelIsHarmless()
    {
        LampSwitchboard board = Board();

        board.Update(1f / 60f);
        board.Reset();

        Assert.Equal(0, board.Count);
        Assert.True(board.IsUntouched);
        Assert.False(board.TryPick(Vector3d.Zero, Vector3d.UnitY, 60.0, out _, out _));
        Assert.Equal(-1, board.NearestFixture(Vector3d.Zero, 4.0));
    }
}
