using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// Reads the human player's input frame from the window's input: the retail action slots
/// (<c>0x45E060</c>) and the movement axes built from them.
/// </summary>
internal static class PlayerInputSampler
{
    // 0x45E060, for the slots this port can supply. Every action slot carries the MASKED BIT VALUE and
    // never 1, because the original stores `mask & bit` -- a consumer that compared against 1 would
    // read almost every action as released.
    internal static PlayerInputFrame SampleFrame(InputState input, PlayerProfile? profile)
    {
        PlayerInputFrame frame = default;
        foreach ((PlayerInputSlot slot, PlayerAction action) in PlayerInputBindings.All)
        {
            Set(
                ref frame,
                slot,
                PlayerProfileInputBindings.IsDown(profile, input, PlayerInputBindings.ConfigName(action)));
        }

        return frame;
    }

    private static void Set(ref PlayerInputFrame frame, PlayerInputSlot slot, bool down)
    {
        if (down && PlayerInputBindings.TryGetAction(slot, out PlayerAction action))
        {
            frame[(int)slot] = (uint)action;
        }
    }

    /// <summary>Builds this tick's movement command from the player's input.</summary>
    /// <param name="input">The player's input state this tick.</param>
    /// <param name="running">The walk/run latch.</param>
    /// <param name="canJump">Whether the player may jump now, or <see langword="null"/> when nothing forbids it.</param>
    internal static PlayerCommand Command(PlayerInputState input, bool running, Func<bool>? canJump)
    {
        // The action names are the game's own, from the binding table at 0x536558: MoveUp and
        // MoveDown are forward and back - named for the screen, as the era's binding dialogs were.
        float forward = Axis(input, PlayerInputSlot.MoveUp, PlayerInputSlot.MoveDown);
        float strafe = Axis(input, PlayerInputSlot.MoveRight, PlayerInputSlot.MoveLeft);
        bool crouch = input.IsSet(PlayerInputSlot.Crouch);

        // Jump is the rising edge, not the level, so holding it does not bounce.
        // The player cannot jump while inside an elevator cab — the lift floor is not a surface
        // the jump integrator expects, and retail does not allow it.
        bool jump = input.IsRisingEdge(PlayerInputSlot.Jump) && (canJump?.Invoke() ?? true);

        return new PlayerCommand(forward, strafe, 0f, 0f, running, crouch, jump);
    }

    internal static float Axis(PlayerInputState input, PlayerInputSlot positive, PlayerInputSlot negative)
    {
        float value = 0f;
        if (input.IsSet(positive))
        {
            value += 1f;
        }

        if (input.IsSet(negative))
        {
            value -= 1f;
        }

        return value;
    }
}
