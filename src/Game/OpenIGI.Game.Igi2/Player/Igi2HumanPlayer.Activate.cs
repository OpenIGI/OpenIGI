using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;

namespace OpenIGI.Game.Igi2.Player;

/// <content>What the player finds to activate, and activating it.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets what the player's view has found to activate this tick, <c>Human_s + 0x748</c>, or <see langword="null"/>.</summary>
    public IIgi2Activatable? EvaluateTarget { get; private set; }

    /// <summary>Gets what the player is activating, <c>Human_s + 0x74C</c>, or <see langword="null"/>.</summary>
    public IIgi2Activatable? ActivatingTarget { get; private set; }

    /// <summary>Gets a value indicating whether the player is activating something, <c>Human_s + 0x750</c>; it looks for nothing meanwhile.</summary>
    public bool IsActivating { get; private set; }

    /// <summary>Gets or sets a value indicating whether an activation has ended since, <c>isCheckActivateButton</c> (<c>Human_s + 0x751</c>).</summary>
    public bool IsCheckActivateButton { get; set; }

    /// <summary>Gets the ticks the current activation has run, <c>Human_s + 0x754</c>.</summary>
    public int ActivateTime { get; private set; }

    /// <summary>
    /// Looks for something to activate, <c>Human_UpdateActivate</c> (<c>igi2 0x500D10</c>), once a tick in the update after the
    /// death check.
    /// </summary>
    /// <remarks>
    /// Standing on the ground and activating nothing, the player offers its view to everything whose sphere meets its own —
    /// the length of its root bone's translation plus its model's radius — and keeps the best that resolves itself
    /// (<see cref="Igi2EvaluateActivate"/>). What it is activating counts the tick.
    /// </remarks>
    public void UpdateActivate()
    {
        EvaluateTarget = null;
        if ((Signal & Igi2PlayerSignal.Grounded) != 0 && !IsActivating)
        {
            Matrix3f v = View.Orientation;
            Igi2EvaluateActivate context = new(
                View.Position,
                [v.M11, v.M12, v.M13, v.M21, v.M22, v.M23, v.M31, v.M32, v.M33],
                isPlayer: true);
            Vector3f r = RootTranslation;
            float radius = (Fpu.Sqrt((((Fpu)r.X * r.X) + ((Fpu)r.Y * r.Y)) + ((Fpu)r.Z * r.Z)) + ModelRadius).F;
            foreach (IIgi2Activatable candidate in Surroundings.FindActivatables(Position, radius))
            {
                candidate.EvaluateActivate(context);
            }

            EvaluateTarget = context.Best;
        }

        if (ActivatingTarget is not null)
        {
            ActivateTime++;
        }
    }

    /// <summary>
    /// The idle state's activate branch, <c>HumanPlayer_RunActionIdle</c> (<c>igi2 0x4B439A</c>…<c>0x4B441D</c>): with something
    /// found and the activate key's channel set, and the found thing first on the action menu, the zoom comes off and the
    /// activation starts.
    /// </summary>
    /// <remarks>
    /// The action menu (<c>ActionMenuScreen_IsFirstActionItem</c>, <c>0x524E60</c>) is the status screen's, not ported yet; the
    /// found thing stands as its first item.
    /// </remarks>
    private void TryActivateFromIdle()
    {
        if (EvaluateTarget is not { } target || Channels[Igi2HumanChannel.Activate] == 0f)
        {
            return;
        }

        View.DisableZoomMode();
        InitActivate(target);
    }

    /// <summary>
    /// Starts activating, <c>HumanPlayer_InitActivate</c> (<c>igi2 0x4A06A0</c>): a switch is pressed at once and the activation
    /// ends there. What else a human activates is not ported; retail leaves an activation of an unknown kind running.
    /// </summary>
    private void InitActivate(IIgi2Activatable target)
    {
        ActivatingTarget = EvaluateTarget;
        IsActivating = true;
        ActivateTime = 0;
        switch (target.Kind)
        {
            case Igi2ActivateKind.Switch:
                (ActivatingTarget as IIgi2Pressable)?.Press(0);
                EndActivate();
                break;
        }
    }

    /// <summary>The activation's end: the player looks again, <c>Human_UnlockActivate</c> (<c>igi2 0x502770</c>).</summary>
    private void EndActivate()
    {
        IsActivating = false;
        IsCheckActivateButton = true;
        ActivatingTarget = null;
    }
}
