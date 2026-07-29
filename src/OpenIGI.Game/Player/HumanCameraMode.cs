namespace OpenIGI.Game.Player;

/// <summary>
/// The five modes <c>HumanCamera</c> dispatches on.
/// </summary>
/// <remarks>
/// <para>
/// The camera does not own its mode. Its update sends the human a message carrying <c>camera+112</c> as
/// the payload, and <c>Human</c>'s handler (<c>0x45FCD0</c>) fills that payload in — position,
/// orientation, both field-of-view tangents, and at <c>payload+112</c> — which is <c>camera+224</c> —
/// the mode. So the mode is <b>pushed by the human</b>, and nothing ever writes the camera's field
/// directly, which is why searching for a writer of that offset finds nothing.
/// </para>
/// <para>
/// Its default source is <c>Human+1264</c>, set to <see cref="FirstPerson"/> at construction
/// (<c>0x45ECD0</c>) and changed through one setter (<c>0x463760</c>). Every name below comes from
/// either that setter's callers or from the handler's own overrides.
/// </para>
/// </remarks>
public enum HumanCameraMode
{
    /// <summary>Nothing selected; the camera's update ignores it.</summary>
    None = 0,

    /// <summary>
    /// The ordinary first-person view — the mode a human is constructed in and the one every other
    /// mode restores when it ends.
    /// </summary>
    /// <remarks>
    /// It is the only mode that raises the flag passed last to the view submit (<c>0x4D9870</c>), which
    /// is what distinguishes it from <see cref="Scripted"/> even though the two share an apply.
    /// </remarks>
    FirstPerson = 1,

    /// <summary>
    /// The developer camera, toggled by input slot 19's rising edge — the Ctrl+Shift+numpad-<c>*</c>
    /// chord.
    /// </summary>
    /// <remarks>
    /// The handler flips a byte at <c>Human+1268</c> on that edge and forces this mode while it is set,
    /// overriding whatever <c>Human+1264</c> holds. Unreachable in a retail profile, since the chord
    /// needs the developer level of 14 (<c>player.md</c> §2.2).
    /// </remarks>
    Developer = 2,

    /// <summary>
    /// The interaction camera, used while the player is doing something with both hands.
    /// </summary>
    /// <remarks>
    /// Set by the entry of every self-driving locomotion state — picking a lock, typing on a computer,
    /// cutting a wire — and restored to <see cref="FirstPerson"/> when the interaction ends. It is by
    /// far the most common value the setter is called with.
    /// </remarks>
    Interaction = 3,

    /// <summary>A scripted camera, whose position and orientation are supplied rather than derived.</summary>
    /// <remarks>
    /// Forced by the handler when the aim/view object's <c>+314</c> is set: it copies a stored position
    /// and orientation over the ones it had just computed and sets this mode. That is why it shares
    /// <see cref="FirstPerson"/>'s apply — the transform is already final and only needs submitting.
    /// </remarks>
    Scripted = 4,

    /// <summary>The death camera.</summary>
    /// <remarks>
    /// Set by exactly one caller: the entry of locomotion state 47, the state whose own entry names
    /// <c>"player_death_"</c>. Its apply clears a bit of the camera's flags afterwards, which none of
    /// the others do.
    /// </remarks>
    Death = 5,
}
