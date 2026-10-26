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
public sealed class Igi1TitlePlayer(Igi1HumanPlayer human, IGroundHeightField? ground) : ITitlePlayer, ITitleFirstPerson
{

    /// <summary>Gets the player.</summary>
    public Igi1HumanPlayer Human { get; } = human ?? throw new ArgumentNullException(nameof(human));

    /// <summary>Gets the ticks the player has run.</summary>
    public long Ticks { get; private set; }

    /// <inheritdoc/>
    public Vector3d Position => Human.Rig.PositionUnits;

    /// <inheritdoc/>
    public Vector3d EyePosition => Human.Rig.EyeUnits;

    /// <inheritdoc/>
    public Matrix3f ViewOrientation => Human.Rig.Camera.Orientation;

    /// <inheritdoc/>
    /// <remarks>
    /// The rig holds what the game's own view model draws from -- the arms' pose, the weapon's bones, its magazine and moving part,
    /// the shot it fired -- and the game's presenter draws from the rig itself: this says when there is anything to draw, from
    /// where, and at the width the rig's view holds it (a scope's zoom narrows it). Nothing while dead, nor before the rig has a
    /// clip, nor while the binoculars or a scope replace the view.
    /// </remarks>
    public TitleFirstPersonView? FirstPerson =>
        Human.Life.IsDead || Human.Rig is { IsBinocularsOpen: true } or { IsSniperScoped: true } || Human.Rig.View is not { IsPosed: true } view
            ? null
            : new TitleFirstPersonView(Human.Rig.EyeUnits, Human.Rig.Camera.Orientation, view.ViewModelTanX, Ticks, []);

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
        Ticks++;
    }
}
