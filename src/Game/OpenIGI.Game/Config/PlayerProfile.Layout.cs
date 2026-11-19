namespace OpenIGI.Game.Config;

/// <content>
/// The move from an older default layout onto today's (<see cref="DefaultBindings"/>), action by action.
/// </content>
public sealed partial class PlayerProfile
{
    /// <summary>The prefix of a title's own binding rows (<c>Title_igi2_Crouch</c>), which only that title reads.</summary>
    public const string TitleRowPrefix = "Title_";

    /// <summary>What a fresh profile's bindings were at <see cref="GameConfig.CurrentControlsVersion"/> two, where they differ from today's.</summary>
    /// <remarks>
    /// Reload on Return and crouch on the right Ctrl, the original's own keys a hand on WASD cannot reach; use on E and the
    /// flashlight on F, which the leans and use pushed along; and modern aim's first leans and mount, before they settled on Q
    /// and E and Z.
    /// </remarks>
    public static IReadOnlyList<InputBinding> Version2DefaultBindings { get; } =
    [
        new("Reload", "INPUTPORT_DEVICE_KEYBOARD", "KEY_RETURN"),
        new("Activate", "INPUTPORT_DEVICE_KEYBOARD", "KEY_E"),
        new("Crouch", "INPUTPORT_DEVICE_KEYBOARD", "KEY_RIGHT_CTRL"),
        new("Flashlight", "INPUTPORT_DEVICE_KEYBOARD", "KEY_F"),
        new(nameof(Player.PlayerAction.LeanRight), "INPUTPORT_DEVICE_KEYBOARD", "KEY_R"),
    ];

    // The actions modern aim brought in, which an older profile may have been handed unbound for want of a free key.
    private static readonly string[] ModernAimActions =
    [
        nameof(Player.PlayerAction.Underbarrel),
        nameof(Player.PlayerAction.LeanLeft),
        nameof(Player.PlayerAction.LeanRight),
        nameof(Player.PlayerAction.Mount),
    ];

    /// <summary>Moves every action still on a key some older default layout gave it onto today's key for it.</summary>
    /// <returns><see langword="true"/> when the profile changed.</returns>
    /// <remarks>
    /// <para>
    /// Action by action, not all or none: an action the player put on a key of their own stays there, and the rest of the
    /// profile still comes along. An action counts as never moved when it is on its key in the original's layout, in either of
    /// the port's earlier ones, or -- for modern aim's own actions -- unbound, as they arrived when their key was taken.
    /// </para>
    /// <para>
    /// A control must not drive two actions, so a move onto a control the player has given another action is dropped, and so
    /// is any move that waited on that one to free its control. Moves that only swap controls among themselves -- E going
    /// from use to the right lean while use goes to F and the flashlight to T -- all happen together. A title's own rows take
    /// part in none of it: they are live only while their title plays.
    /// </para>
    /// </remarks>
    public bool AdoptCurrentLayout()
    {
        // A player who walks on anything but WASD -- the original's arrows, a left-handed layout -- has laid the keys out round
        // that hand: the layout's other keys only make sense beside WASD, so such a profile is left as its player has it.
        string[] walk = ["MoveUp", "MoveLeft", "MoveDown", "MoveRight"];
        if (!walk.All(action => Bindings.Exists(binding =>
                string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase)
                && DefaultBindings.Any(now => SameAction(now, binding) && SameControl(now, binding)))))
        {
            return false;
        }

        List<(int Index, InputBinding Now)> moves = [];
        foreach (InputBinding now in DefaultBindings)
        {
            int index = Bindings.FindIndex(binding => SameAction(binding, now));
            if (index >= 0 && !SameControl(Bindings[index], now) && WasDefault(Bindings[index]))
            {
                moves.Add((index, now));
            }
        }

        bool dropped;
        do
        {
            dropped = false;
            HashSet<int> moving = [.. moves.Select(move => move.Index)];
            for (int i = moves.Count - 1; i >= 0; i--)
            {
                InputBinding now = moves[i].Now;
                bool held = false;
                for (int row = 0; row < Bindings.Count && !held; row++)
                {
                    InputBinding other = Bindings[row];
                    held = !moving.Contains(row) && !IsTitleRow(other) && SameControl(other, now);
                }

                if (held)
                {
                    moves.RemoveAt(i);
                    dropped = true;
                }
            }
        }
        while (dropped);

        foreach ((int index, InputBinding now) in moves)
        {
            Bindings[index] = now;
        }

        return moves.Count > 0;
    }

    /// <summary>Whether a binding row is a title's own, which only that title reads.</summary>
    /// <param name="binding">The row.</param>
    /// <returns><see langword="true"/> for a <see cref="TitleRowPrefix"/> row.</returns>
    public static bool IsTitleRow(InputBinding binding) =>
        binding.Action?.StartsWith(TitleRowPrefix, StringComparison.Ordinal) == true;

    // Whether a row sits where some default layout put its action: the original's, either earlier one of the port's, or today's.
    private static bool WasDefault(InputBinding binding)
    {
        if (string.Equals(binding.Key, UnboundKey, StringComparison.OrdinalIgnoreCase))
        {
            return ModernAimActions.Contains(binding.Action, StringComparer.OrdinalIgnoreCase);
        }

        return RetailDefaultBindings.Concat(PreviousDefaultBindings).Concat(Version2DefaultBindings)
            .Any(old => SameAction(old, binding) && SameControl(old, binding));
    }

    private static bool SameAction(InputBinding a, InputBinding b) =>
        string.Equals(a.Action, b.Action, StringComparison.OrdinalIgnoreCase);

    private static bool SameControl(InputBinding a, InputBinding b) =>
        string.Equals(a.Device, b.Device, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(a.Key, UnboundKey, StringComparison.OrdinalIgnoreCase);
}
