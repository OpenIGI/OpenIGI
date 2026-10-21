using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Drives a <see cref="PlayerWeapons"/> machine tick by tick the way the human does: input frames in, the
/// sway stepped after the machine has read it, and the fire resolved down a fixed barrel.
/// </summary>
internal static class PlayerWeaponsDriver
{
    // Runs until the cycle has put a weapon in hand and the machine is idle again. The swap is a
    // TWO-PHASE lowering and raising of the rig -- 11 ticks each way -- so it is not instant.
    internal static void Equip(PlayerWeapons weapons, bool clipEnded = true)
    {
        PlayerInputState input = new();
        for (int i = 0; i < 4 * HumanViewSway.TicksToTravel; i++)
        {
            Tick(weapons, input, Frame(), clipEnded);
            if (weapons.State == HumanWeaponState.Ready && weapons.InHand is not null)
            {
                return;
            }
        }

        Assert.Fail("the weapon cycle never put anything in hand.");
    }

    internal static PlayerInputFrame Frame(params PlayerInputSlot[] pressed)
    {
        PlayerInputFrame frame = default;
        foreach (PlayerInputSlot slot in pressed)
        {
            if (PlayerInputBindings.TryGetAction(slot, out PlayerAction action))
            {
                frame[(int)slot] = (uint)action;
            }
        }

        return frame;
    }

    internal static void Tick(
        PlayerWeapons weapons,
        PlayerInputState input,
        PlayerInputFrame frame,
        bool clipEnded = false)
    {
        input.Accept(frame);
        weapons.Tick(input, clipEnded, onGround: true);

        // Where 0x464BD0 steps the weapon-change lowering: after the machine has read it. Without this
        // a swap never completes, because the state that swaps waits for the rig to arrive.
        weapons.Sway.Advance();
        weapons.ResolveFire(default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f));
    }

    // A tap, then long enough for the swap's lowering and raising to run out.
    internal static void Press(PlayerWeapons weapons, PlayerInputState input, PlayerInputSlot slot)
    {
        Tick(weapons, input, Frame(slot));
        for (int i = 0; i < 4 * HumanViewSway.TicksToTravel; i++)
        {
            Tick(weapons, input, Frame());
            if (weapons.State == HumanWeaponState.Ready)
            {
                return;
            }
        }
    }

    // The rig is stood in for by reporting the clip as finished every tick. That is what a LOOPING
    // stand clip does on its wrap and what a one-shot clip does once it is clamped at its duration, and
    // it is the conservative choice: a state that waits on the flag gets to leave, so the cadence these
    // measure comes from the gun's clock alone and never from an animation this test invented.
    internal static int Run(PlayerWeapons weapons, bool fire, int ticks, bool clipEnded = true)
    {
        PlayerInputState input = new();
        PlayerInputFrame frame = fire ? Frame(PlayerInputSlot.Fire) : Frame();

        int rounds = 0;
        for (int i = 0; i < ticks; i++)
        {
            Tick(weapons, input, frame, clipEnded);
            if (weapons.FiredThisTick)
            {
                rounds++;
            }
        }

        return rounds;
    }

    // Taps the trigger every `tapEvery` ticks and reports the gaps between the rounds that went off.
    internal static List<int> Spacing(PlayerWeapons weapons, int tapEvery, int ticks)
    {
        PlayerInputState input = new();
        List<int> gaps = [];
        int last = -1;

        for (int i = 0; i < ticks; i++)
        {
            bool down = i % (tapEvery * 2) < tapEvery;
            Tick(weapons, input, down ? Frame(PlayerInputSlot.Fire) : Frame(), clipEnded: true);

            if (!weapons.FiredThisTick)
            {
                continue;
            }

            if (last >= 0)
            {
                gaps.Add(i - last);
            }

            last = i;
        }

        return gaps;
    }
}
