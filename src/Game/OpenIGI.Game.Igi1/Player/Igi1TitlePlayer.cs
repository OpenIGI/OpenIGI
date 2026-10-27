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
/// level it stands in. A dead player takes no input, as the original's death state takes none, but the mouse still turns the death
/// camera about the body (<see cref="BeginDeathView"/>), whose eye the view is then.
/// </remarks>
/// <param name="human">The player.</param>
/// <param name="ground">The ground it walks on.</param>
public sealed class Igi1TitlePlayer(Igi1HumanPlayer human, IGroundHeightField? ground) : ITitlePlayer, ITitleFirstPerson
{

    /// <summary>Gets the player.</summary>
    public Igi1HumanPlayer Human { get; } = human ?? throw new ArgumentNullException(nameof(human));

    private readonly Igi1HumanCamera _death = new();
    private Vector3d _deathFeet;

    /// <summary>Gets the ticks the player has run.</summary>
    public long Ticks { get; private set; }

    /// <inheritdoc/>
    public Vector3d Position => Human.Rig.PositionUnits;

    /// <inheritdoc/>
    public Vector3d EyePosition => IsDeathViewActive ? _death.Camera.Position * WorldScene.UnitsPerMetre : Human.Rig.EyeUnits;

    /// <inheritdoc/>
    public Matrix3f ViewOrientation => IsDeathViewActive ? _death.Camera.Orientation : Human.Rig.Camera.Orientation;

    /// <summary>Gets a value indicating whether the death camera has the view: the player is dead and its camera pulled back.</summary>
    public bool IsDeathViewActive => Human.Life.IsDead && _death.IsActive;

    /// <summary>Pulls the camera back from the player's body as it falls (the human camera's mode 5).</summary>
    /// <param name="feet">Where the body lies, in world units.</param>
    /// <param name="facing">The facing the body falls along; the orbit starts behind it.</param>
    /// <param name="walls">What the camera keeps off, or <see langword="null"/> for nothing.</param>
    public void BeginDeathView(Vector3d feet, float facing, WorldRayCast? walls)
    {
        _deathFeet = feet;
        _death.Begin(EyeOver(feet), feet, facing, walls, death: true);
    }

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
            if (_death.IsActive)
            {
                _death.LookSettings = MouseLookSettings.FromSensitivity(input.MouseSensitivity, input.InvertMouse);
                _death.Control(input.MouseX, input.MouseY);
                _death.Tick(EyeOver(_deathFeet), _deathFeet);
                _death.Present(1f);
            }

            return;
        }

        Human.Rig.LookSettings = MouseLookSettings.FromSensitivity(input.MouseSensitivity, input.InvertMouse);
        Human.Rig.Look(input.MouseX, input.MouseY);
        Human.Rig.Tick(input.Held, ground);
        Ticks++;
    }

    // The eye of a body standing on these feet, which the death camera orbits and looks at.
    private static Vector3d EyeOver(Vector3d feet) => new(feet.X, feet.Y, feet.Z + PlayerBody.StandingEyeHeight);
}
