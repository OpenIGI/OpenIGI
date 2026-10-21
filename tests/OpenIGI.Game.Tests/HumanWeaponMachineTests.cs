using System.Collections.ObjectModel;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Checks the shape of the weapon-action machine at <c>Human+3320</c> and the join that names every
/// input slot.
/// </summary>
/// <remarks>
/// These are invariants of the extraction rather than of any one transition: if a state were mis-read,
/// the star shape or the slot join would stop holding.
/// </remarks>
public sealed class HumanWeaponMachineTests
{
    [Fact]
    public void TheMachineHasThirteenStatesAndNoTriggers()
    {
        Assert.Equal(13, HumanWeaponGraph.StateCount);
        Assert.Equal(13, HumanWeaponGraph.States.Count());
        Assert.Equal(HumanWeaponState.Ready, HumanWeaponGraph.InitialState);

        // The locomotion machine registers two four-step double-taps; this one registers none, which
        // is the whole reason no weapon action is a gesture.
        Assert.Equal(0, HumanWeaponGraph.TriggerCount);
    }

    [Fact]
    public void EveryStateIsInTheTableExactlyOnce()
    {
        HumanWeaponState[] declared = Enum.GetValues<HumanWeaponState>();
        Assert.Equal(HumanWeaponGraph.StateCount, declared.Length);
        Assert.Equal(declared.Order(), HumanWeaponGraph.States.Order());
    }

    [Fact]
    public void TheGraphIsAStarAroundReady()
    {
        // Only Ready branches. That is the opposite of the locomotion machine, where every motion
        // state can reach every other one -- and it is the strongest single check that the extraction
        // read real branch targets rather than incidental immediates.
        foreach (HumanWeaponState state in HumanWeaponGraph.States)
        {
            ReadOnlyCollection<HumanWeaponState> targets = HumanWeaponGraph.TargetsOf(state);
            Assert.NotEmpty(targets);

            if (state == HumanWeaponState.Ready)
            {
                Assert.Equal(13, targets.Count);
                continue;
            }

            // Nothing else names more than four, and Firing is the only one that does -- it can be
            // suspended, go dry, reload, or finish.
            Assert.Contains(HumanWeaponState.Ready, targets);
            Assert.InRange(targets.Count, 1, 4);
            Assert.True(
                targets.Count <= 2 || state == HumanWeaponState.Firing,
                $"{state} branches {targets.Count} ways; only Ready and Firing may.");
        }
    }

    [Fact]
    public void ReadyReachesEveryOtherState()
    {
        ReadOnlyCollection<HumanWeaponState> targets = HumanWeaponGraph.TargetsOf(HumanWeaponState.Ready);

        foreach (HumanWeaponState state in HumanWeaponGraph.States)
        {
            if (state != HumanWeaponState.Ready)
            {
                Assert.Contains(state, targets);
            }
        }

        // Placing appears twice -- once from the fire branch's category-7 arm and once from the
        // alternate-fire branch's proximity-mine arm. Order is branch order, so the duplicate is real.
        Assert.Equal(2, targets.Count(t => t == HumanWeaponState.Placing));
    }

    [Fact]
    public void FireIsSlotSixAndFlagTwoHundred()
    {
        // The answer the whole pass was after, stated as a test so it cannot drift: the binding table
        // gives Fire the mask 0x100, the sampler puts that bit in slot 6, and the weapon prologue maps
        // slot 6 to flag 0x200.
        Assert.True(PlayerInputBindings.TryGetSlot(PlayerAction.Fire, out PlayerInputSlot slot));
        Assert.Equal(PlayerInputSlot.Fire, slot);
        Assert.Equal(6, (int)slot);
        Assert.Equal(0x100, (int)PlayerAction.Fire);

        (PlayerInputSlot Slot, HumanWeaponFlagWord Flag) mapped =
            HumanWeaponFlagBuilder.InputBits.Single(b => b.Slot == PlayerInputSlot.Fire);
        Assert.Equal(HumanWeaponFlagWord.Fire, mapped.Flag);
        Assert.Equal(0x200u, (uint)mapped.Flag);
    }

    [Fact]
    public void TheMovementBitsMatchTheDirectionsTheAnimationsMeasure()
    {
        // Locomotion state 0 tests 0x80, 0x100, 0x40, 0x20 in that order and targets states 1, 4, 7
        // and 8, which were MEASURED off the clips as forward, backward, right and
        // left. If the slot join were wrong, these four would not line up.
        Assert.Equal(HumanWeaponFlagWord.MoveUp, FlagOf(PlayerInputSlot.MoveUp));
        Assert.Equal(0x80u, (uint)FlagOf(PlayerInputSlot.MoveUp));
        Assert.Equal(0x100u, (uint)FlagOf(PlayerInputSlot.MoveDown));
        Assert.Equal(0x40u, (uint)FlagOf(PlayerInputSlot.MoveRight));
        Assert.Equal(0x20u, (uint)FlagOf(PlayerInputSlot.MoveLeft));
    }

    [Fact]
    public void EveryBoundActionIsSampledIntoExactlyOneSlot()
    {
        PlayerAction[] declared = Enum.GetValues<PlayerAction>();

        // Twenty-seven rows in the binding table and the remaster's flashlight: twenty-eight bound slots.
        Assert.Equal(28, declared.Length);
        Assert.Equal(28, PlayerInputBindings.All.Count);

        foreach (PlayerAction action in declared)
        {
            Assert.True(PlayerInputBindings.TryGetSlot(action, out _), $"{action} is sampled into no slot.");
        }

        Assert.Equal(28, PlayerInputBindings.All.Select(b => b.Slot).Distinct().Count());
    }

    [Fact]
    public void TheFlashlightTakesABitAndASlotTheOriginalLeavesFree()
    {
        // Not retail: the binding table jumps from the movement nibble to 0x100, and the sampler never
        // writes slot 35, so nothing the original reads can collide with the remaster's one extension.
        Assert.Equal(0x10, (int)PlayerAction.Flashlight);
        Assert.True(PlayerInputBindings.TryGetSlot(PlayerAction.Flashlight, out PlayerInputSlot slot));
        Assert.Equal(PlayerInputSlot.Flashlight, slot);
        Assert.Equal(35, (int)slot);

        foreach (PlayerAction action in Enum.GetValues<PlayerAction>())
        {
            if (action != PlayerAction.Flashlight)
            {
                Assert.Equal(0, (int)action & 0x10);
            }
        }
    }

    [Fact]
    public void TheScratchSlotBindsNoAction()
    {
        // Slot 13 carries the mask's sign bit and the table binds nothing to 0x80000000; the weapon
        // category selector uses it as a one-tick register instead.
        Assert.False(PlayerInputBindings.TryGetAction(PlayerInputSlot.SelectedWeaponScratch, out _));
        Assert.DoesNotContain(PlayerInputBindings.All, b => b.Slot == PlayerInputSlot.SelectedWeaponScratch);
    }

    [Fact]
    public void ThePrologueKeepsGroundAndDeathAndRebuildsTheRest()
    {
        HumanWeaponFlagWord previous =
            HumanWeaponFlagWord.OnGround | HumanWeaponFlagWord.Dying | HumanWeaponFlagWord.Fire;

        PlayerInputFrame frame = default;
        HumanWeaponFlagWord next = HumanWeaponFlagBuilder.Build(previous, frame, clipEnded: false, blending: false);

        Assert.True(next.HasFlag(HumanWeaponFlagWord.OnGround));
        Assert.True(next.HasFlag(HumanWeaponFlagWord.Dying));

        // Fire came from the input and the input is now clear, so it must not survive.
        Assert.False(next.HasFlag(HumanWeaponFlagWord.Fire));
    }

    [Fact]
    public void AnActionSlotIsTestedAgainstZeroAndNotAgainstOne()
    {
        // Slot 21 holds 0x2000 when NextWeapon is pressed, never 1. A prologue that compared against
        // one would read almost every action as released.
        PlayerInputFrame frame = default;
        frame[(int)PlayerInputSlot.NextWeapon] = (uint)PlayerAction.NextWeapon;

        HumanWeaponFlagWord flags = HumanWeaponFlagBuilder.Build(
            HumanWeaponFlagWord.None, frame, clipEnded: false, blending: false);

        Assert.True(flags.HasFlag(HumanWeaponFlagWord.NextWeapon));
    }

    [Fact]
    public void TheClipEndFlagIsWhatTheStatesWaitOn()
    {
        PlayerInputFrame frame = default;

        Assert.False(HumanWeaponFlagBuilder
            .Build(HumanWeaponFlagWord.None, frame, clipEnded: false, blending: false)
            .HasFlag(HumanWeaponFlagWord.ClipEnded));

        Assert.True(HumanWeaponFlagBuilder
            .Build(HumanWeaponFlagWord.None, frame, clipEnded: true, blending: false)
            .HasFlag(HumanWeaponFlagWord.ClipEnded));
    }

    [Fact]
    public void TheFireDispatchPlaysNothingPastTheThirdVariant()
    {
        WeaponType weapon = new()
        {
            FireAnimation1 = 28,
            FireAnimation2 = 29,
            FireAnimation3 = 30,
            UpperBodyFireAnimation = 147,
        };

        Assert.Equal(28, WeaponAnimationTable.Select(weapon, WeaponAction.Fire, 0).FullBody);
        Assert.Equal(29, WeaponAnimationTable.Select(weapon, WeaponAction.Fire, 1).FullBody);
        Assert.Equal(30, WeaponAnimationTable.Select(weapon, WeaponAction.Fire, 2).FullBody);

        // Not a wrap. The original's chain of equality tests leaves BOTH ids at -1 past two, and -1
        // means play nothing -- a port that took the remainder would play a fourth swing.
        WeaponAnimations fourth = WeaponAnimationTable.Select(weapon, WeaponAction.Fire, 3);
        Assert.Equal(-1, fourth.FullBody);
        Assert.Equal(-1, fourth.UpperBody);
    }

    private static HumanWeaponFlagWord FlagOf(PlayerInputSlot slot) =>
        HumanWeaponFlagBuilder.InputBits.Single(b => b.Slot == slot).Flag;
}
