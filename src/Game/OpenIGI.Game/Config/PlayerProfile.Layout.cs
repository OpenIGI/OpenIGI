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

    /// <summary>A title's own row that today's layout moved: the key it had by default, and the one it has now.</summary>
    /// <param name="Action">The row's action, a <see cref="TitleRowPrefix"/> name.</param>
    /// <param name="From">The key the row had by default before.</param>
    /// <param name="To">Its key now.</param>
    public readonly record struct TitleRowMove(string Action, string From, string To);

    /// <summary>The title rows today's layout moved, so the title's keys sit on the same standard as the shared ones.</summary>
    /// <remarks>
    /// I.G.I. 2's crouch and walk/run on the keys the shared ones use, prone on Z, the thermal goggles on N rather than the
    /// co-op mark's T, the inventory on I, and the stance steps on Insert and Delete only, clear of the leans' Q and prone's Z.
    /// Kept as names only: the titles own their rows, and this assembly knows none of them.
    /// </remarks>
    public static IReadOnlyList<TitleRowMove> TitleRowMoves { get; } =
    [
        new("Title_igi2_Crouch", "KEY_LEFT_SHIFT", "KEY_LEFT_CTRL"),
        new("Title_igi2_ToggleWalkRun", "KEY_CAPITAL", "KEY_LEFT_SHIFT"),
        new("Title_igi2_ToggleWalkRunAlt", "KEY_END", "KEY_CAPITAL"),
        new("Title_igi2_ToggleStandLie", UnboundKey, "KEY_Z"),
        new("Title_igi2_LowerStance", "KEY_Z", "KEY_DELETE"),
        new("Title_igi2_LowerStanceAlt", "KEY_DELETE", UnboundKey),
        new("Title_igi2_RaiseStance", "KEY_Q", "KEY_INSERT"),
        new("Title_igi2_RaiseStanceAlt", "KEY_INSERT", UnboundKey),
        new("Title_igi2_Thermal", "KEY_T", "KEY_N"),
        new("Title_igi2_Inventory", "KEY_0", "KEY_I"),
    ];

    // The shared actions I.G.I. 2 never reads, or reads only through a row of its own: a title row may share their keys.
    private static readonly string[] SharedActionsTitlesSkip =
    [
        "Crouch", "ToggleWalkRun", "Flashlight", "WeaponCategory8",
        nameof(Player.PlayerAction.Underbarrel), nameof(Player.PlayerAction.LeanLeft),
        nameof(Player.PlayerAction.LeanRight), nameof(Player.PlayerAction.Mount),
    ];

    /// <summary>Whether a shared action is one a title with rows of its own (I.G.I. 2) never reads, or reads through its own row.</summary>
    /// <param name="action">The shared action's config name.</param>
    /// <returns><see langword="true"/> for such an action.</returns>
    public static bool TitlesSkip(string action) => SharedActionsTitlesSkip.Contains(action, StringComparer.OrdinalIgnoreCase);

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
    /// from use to the right lean while use goes to F and the flashlight to L -- all happen together. A title's own rows are
    /// live only while their title plays, so they hold no key from a shared action; the ones the layout moved
    /// (<see cref="TitleRowMoves"/>) come along afterwards by the same rule among their own title's rows.
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

        bool shared = Settle(moves, (other, _) => !IsTitleRow(other));
        return AdoptTitleLayout() | shared;
    }

    // The title rows still on the key the layout before gave them, onto today's (TitleRowMoves). A title row cannot take a key
    // another of its own title's rows keeps, nor one a shared action the titles read keeps.
    private bool AdoptTitleLayout()
    {
        List<(int Index, InputBinding Now)> moves = [];
        foreach (TitleRowMove move in TitleRowMoves)
        {
            int index = Bindings.FindIndex(binding => string.Equals(binding.Action, move.Action, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && string.Equals(Bindings[index].Key, move.From, StringComparison.OrdinalIgnoreCase))
            {
                moves.Add((index, Bindings[index] with { Key = move.To }));
            }
        }

        return Settle(moves, static (other, now) => IsTitleRow(other)
            ? string.Equals(TitleOf(other.Action), TitleOf(now.Action), StringComparison.OrdinalIgnoreCase)
            : !SharedActionsTitlesSkip.Contains(other.Action, StringComparer.OrdinalIgnoreCase));
    }

    // Drops every move onto a control a row that stays keeps -- among the rows the rule says count -- and every move that waited
    // on one dropped, then makes the rest. A move to no key at all is never held up.
    private bool Settle(List<(int Index, InputBinding Now)> moves, Func<InputBinding, InputBinding, bool> counts)
    {
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
                    held = !moving.Contains(row) && counts(other, now) && SameControl(other, now);
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

    // The title a title row belongs to: "Title_igi2_Crouch" is igi2's.
    private static string TitleOf(string action)
    {
        int end = action.IndexOf('_', TitleRowPrefix.Length);
        return end < 0 ? action : action[..end];
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
