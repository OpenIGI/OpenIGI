using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// Project I.G.I.'s player as the host's title path runs it (<see cref="ITitlePlayer"/>): where it stands and looks, and its
/// tick from the title input -- the look first, then <c>HumanPlayer_Update</c> over the actions held.
/// </summary>
/// <remarks>
/// As I.G.I. 2's own (<c>Igi2TitlePlayer</c>): the player the level's walk runs where it reaches the player's task, whichever
/// level it stands in. A dead player takes no input, as the original's death state takes none.
/// </remarks>
/// <param name="human">The player.</param>
/// <param name="ground">The ground it walks on.</param>
public sealed class Igi1TitlePlayer(Igi1HumanPlayer human, IGroundHeightField? ground) : ITitlePlayer
{
    /// <summary>Gets the player.</summary>
    public Igi1HumanPlayer Human { get; } = human ?? throw new ArgumentNullException(nameof(human));

    /// <inheritdoc/>
    public Vector3d Position => Human.Rig.PositionUnits;

    /// <inheritdoc/>
    public Vector3d EyePosition => Human.Rig.EyeUnits;

    /// <inheritdoc/>
    public Matrix3f ViewOrientation => Human.Rig.Camera.Orientation;

    /// <inheritdoc/>
    public void Tick(in TitlePlayerInput input)
    {
        if (Human.Life.IsDead)
        {
            return;
        }

        Human.Rig.LookSettings = MouseLookSettings.FromSensitivity(input.MouseSensitivity, input.InvertMouse);
        Human.Rig.Look(input.MouseX, input.MouseY);
        Human.Rig.Tick(input.Held, ground);
    }
}
