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

    private static LightFixture Coloured(double x, Vector3f colour) =>
        new("lamp", new Vector3d(x, 0, 0), 8f, colour, 1f, 0.2f, 4);

    /// <summary>A board of many lamps, so every character is on it, with the supply already off.</summary>
    private static LampSwitchboard DarkYard(int count, Vector3f colour)
    {
        LightFixture[] fixtures = new LightFixture[count];
        for (int i = 0; i < count; i++)
        {
            fixtures[i] = Coloured(i * 5.0, colour);
        }

        LampSwitchboard board = new(fixtures, seed: 11);
        for (int i = 0; i < count; i++)
        {
            board.SetSupplyImmediately(i, false);
        }

        return board;
    }

    /// <summary>Cutting the supply puts a lamp out the way the switch does, and restoring it brings it back.</summary>
    [Fact]
    public void TheSupplyPutsALampOutAndBringsItBack()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.SetSupply(0, false);
        Assert.False(board.IsSupplied(0));
        Assert.Equal(LampMode.On, board.ModeOf(0));
        Assert.False(board.IsUntouched);

        board.Update(1f / 60f);
        Assert.InRange(board.LevelOf(0), 0.05f, 0.95f);

        Run(board, 1.5f);
        Assert.Equal(0f, board.LevelOf(0));

        board.SetSupply(0, true);
        Run(board, 25f);
        Assert.Equal(1f, board.LevelOf(0));
        Assert.True(board.IsUntouched);
    }

    /// <summary>The wall switch and the supply are in series: either one open is a dark lamp.</summary>
    /// <remarks>
    /// A lamp somebody switched off must not come on by itself at dusk; one they switched back on during
    /// the day must not light until dusk, and then must.
    /// </remarks>
    [Fact]
    public void TheSwitchAndTheSupplyAreInSeries()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        board.Switch(0);
        Run(board, 1.5f);
        board.SetSupply(0, false);
        board.SetSupply(0, true);
        Run(board, 25f);
        Assert.Equal(0f, board.LevelOf(0));

        board.SetSupply(0, false);
        board.Switch(0);
        Run(board, 25f);
        Assert.Equal(LampMode.On, board.ModeOf(0));
        Assert.Equal(0f, board.LevelOf(0));

        board.SetSupply(0, true);
        Run(board, 25f);
        Assert.Equal(1f, board.LevelOf(0));
    }

    /// <summary>A level opening snaps its lamps to their state silently: no fade, and nothing heard.</summary>
    [Fact]
    public void SettingTheSupplyImmediatelyIsSilentAndAtOnce()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0), Fixture(4, 0, 0));
        board.Break(1);
        Run(board, 1f);
        board.DrainEvents([]);

        board.SetSupplyImmediately(0, false);
        board.SetSupplyImmediately(1, false);
        Assert.Equal(0f, board.LevelOf(0));
        Assert.Equal(0f, board.LevelOf(1));

        // Dark, and staying dark: a broken lamp without supply does not flicker.
        for (int frame = 0; frame < 300; frame++)
        {
            board.Update(1f / 60f);
            Assert.Equal(0f, board.LevelOf(1));
        }

        board.SetSupplyImmediately(0, true);
        Assert.Equal(1f, board.LevelOf(0));

        var events = new List<LampEvent>();
        board.DrainEvents(events);
        Assert.Empty(events);
    }

    /// <summary>Every lamp brought up cold makes the sounds of its own sort of start, in the order it does them.</summary>
    /// <remarks>
    /// The sounds are raised by the state machine that moves the light, which is the whole guarantee that
    /// they match it: a tube's ticks come before its hum, a lantern's crackle before its arc.
    /// </remarks>
    [Fact]
    public void ColdStartsSoundLikeTheirOwnSortOfLamp()
    {
        LampSwitchboard board = DarkYard(48, Vector3f.One);
        var events = new List<LampEvent>();

        for (int i = 0; i < board.Count; i++)
        {
            board.SetSupply(i, true);
        }

        for (int frame = 0; frame < 60 * 25; frame++)
        {
            board.Update(1f / 60f);
            board.DrainEvents(events);
        }

        var characters = new HashSet<LampCharacter>();

        for (int i = 0; i < board.Count; i++)
        {
            Assert.Equal(1f, board.LevelOf(i));

            LampSound[] heard = [.. events.Where(e => e.Index == i).Select(e => e.Sound)];
            LampCharacter character = board.CharacterOf(i);
            characters.Add(character);

            switch (character)
            {
                case LampCharacter.Tube:
                    Assert.True(heard.Length >= 2, $"Tube {i} made {heard.Length} sound(s).");
                    Assert.All(heard[..^1], sound => Assert.Equal(LampSound.Starter, sound));
                    Assert.Equal(LampSound.Arc, heard[^1]);
                    break;

                case LampCharacter.Discharge:
                    Assert.True(heard.Length >= 2, $"Lantern {i} made {heard.Length} sound(s).");
                    Assert.All(heard[..^1], sound => Assert.Equal(LampSound.Ignitor, sound));
                    Assert.Equal(LampSound.Arc, heard[^1]);
                    break;

                default:
                    Assert.Equal(new[] { LampSound.Filament }, heard);
                    break;
            }
        }

        Assert.Equal(3, characters.Count);
    }

    /// <summary>A yard brought up at one instant does not finish at one instant.</summary>
    /// <remarks>
    /// Even with every supply closed on the same frame -- one contactor -- the lamps strike and warm each in
    /// their own time. A row of lamps reaching full together is one effect played on all of them.
    /// </remarks>
    [Fact]
    public void AColdYardDoesNotComeUpInStep()
    {
        LampSwitchboard board = DarkYard(24, Vector3f.One);
        float[] lit = new float[board.Count];
        Array.Fill(lit, -1f);

        for (int i = 0; i < board.Count; i++)
        {
            board.SetSupply(i, true);
        }

        for (int frame = 0; frame < 60 * 25; frame++)
        {
            board.Update(1f / 60f);

            for (int i = 0; i < board.Count; i++)
            {
                if (lit[i] < 0f && board.LevelOf(i) >= 1f)
                {
                    lit[i] = frame / 60f;
                }
            }
        }

        Assert.All(lit, seconds => Assert.True(seconds > 0f, "A lamp never reached full."));
        Assert.True(lit.Max() - lit.Min() > 2f, $"The yard came up within {lit.Max() - lit.Min():0.00} s.");
    }

    /// <summary>A sodium lantern starts in its starting gas's colour and ends in its own.</summary>
    [Fact]
    public void AnAmberLanternWarmsUpThroughPink()
    {
        LampSwitchboard board = DarkYard(32, new Vector3f(1f, 0.55f, 0.2f));
        int lantern = Enumerable.Range(0, board.Count).First(i => board.CharacterOf(i) == LampCharacter.Discharge);

        board.SetSupply(lantern, true);
        bool pink = false;

        for (int frame = 0; frame < 60 * 25; frame++)
        {
            board.Update(1f / 60f);
            Vector3f tint = board.TintOf(lantern);
            pink |= board.LevelOf(lantern) > 0.1f && tint.Y < 0.7f && tint.Z > tint.Y;
        }

        Assert.True(pink, "The lantern never showed its starting gas's colour.");
        Assert.Equal(1f, board.LevelOf(lantern));
        Assert.Equal(Vector3f.One, board.TintOf(lantern));
    }

    /// <summary>A board nobody drains does not grow its sound list without bound.</summary>
    [Fact]
    public void UndrainedSoundsAreBounded()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));

        for (int i = 0; i < 5000; i++)
        {
            board.Switch(0);
        }

        var events = new List<LampEvent>();
        board.DrainEvents(events);
        Assert.InRange(events.Count, 1, 256);

        events.Clear();
        board.DrainEvents(events);
        Assert.Empty(events);
    }

    /// <summary>Reset puts the switches back and leaves the daylight's supply alone.</summary>
    [Fact]
    public void ResetLeavesTheSupplyAlone()
    {
        LampSwitchboard board = Board(Fixture(0, 0, 0));
        board.SetSupplyImmediately(0, false);
        board.Switch(0);

        board.Reset();

        Assert.Equal(LampMode.On, board.ModeOf(0));
        Assert.False(board.IsSupplied(0));
        Assert.Equal(0f, board.LevelOf(0));
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
