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
public sealed class Igi1TitlePlayer(Igi1HumanPlayer human, IGroundHeightField? ground)
    : ITitlePlayer, ITitleFirstPerson, ITitlePlayerLook, ITitlePlayerPlacement
{
    private TitleLookPreview? _look;


    /// <summary>Gets the player.</summary>
    public Igi1HumanPlayer Human { get; } = human ?? throw new ArgumentNullException(nameof(human));

    private readonly Igi1HumanCamera _death = new();
    private Vector3d _deathFeet;

    /// <summary>
    /// Gets what runs a tick of the rig where the player is the game's own mission's: the mission's steps around it
    /// (<c>Igi1MissionTick.StepPlayer</c>), which answer whether the player ticks at all. <see langword="null"/> to tick the rig
    /// alone, as in another game's level.
    /// </summary>
    public Func<Action<PlayerCamera>, bool>? Step { get; init; }

    /// <inheritdoc/>
    /// <remarks>Not while dead, nor on a mounted gun, whose look turns the gun rather than the head.</remarks>
    public bool CanPreviewLook => !Human.Life.IsDead && Human.Rig.MountedStationaryGun is null;

    /// <inheritdoc/>
    public Matrix3f LookOrientation => Look.Orientation;

    /// <summary>Gets the ticks the player has run.</summary>
    public long Ticks { get; private set; }

    /// <inheritdoc/>
    public Vector3d Position => Human.Rig.PositionUnits;

    /// <inheritdoc/>
    public Vector3d EyePosition => IsDeathViewActive ? _death.Camera.Position * WorldScene.UnitsPerMetre : Human.Rig.EyeUnits;

    /// <inheritdoc/>
    public Matrix3f ViewOrientation => IsDeathViewActive ? _death.Camera.Orientation : Human.Rig.Camera.Orientation;

    /// <inheritdoc/>
    /// <remarks>The rig's own placement: a teleport, onto the ground below.</remarks>
    public bool TryPlace(Vector3d cameraPosition, float yaw) => Human.Rig.Place(ground, cameraPosition / WorldScene.UnitsPerMetre, yaw);

    /// <inheritdoc/>
    /// <remarks>The body's yaw: its forward is (sin yaw, cos yaw).</remarks>
    public float? Heading => Human.Rig.Yaw;

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
        TitlePlayerInput given = input;

        // In the game's own mission the mission decides whether the player ticks at all, and its death is the mission's camera's.
        if (Step is { } step)
        {
            step(rig => TickRig(rig, given));
            Look.Reset();
            return;
        }

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

        TickRig(Human.Rig, given);

        // The look gathered since the last tick is the rig's now.
        Look.Reset();
    }

    /// <inheritdoc/>
    public void GatherLook(float mouseX, float mouseY, float sensitivity, bool invertMouse) => Look.Gather(mouseX, mouseY, sensitivity, invertMouse);

    /// <inheritdoc/>
    public void ResetLook() => Look.Reset();

    /// <inheritdoc/>
    public Matrix3f PreviewLook(float mouseX, float mouseY, float sensitivity, bool invertMouse) =>
        Look.Preview(mouseX, mouseY, sensitivity, invertMouse);

    // The tick's look, the mouse's counts since the last as the rig turns them, then HumanPlayer_Update over the actions held.
    private void TickRig(PlayerCamera rig, in TitlePlayerInput input)
    {
        rig.LookSettings = MouseLookSettings.FromSensitivity(input.MouseSensitivity, input.InvertMouse);
        rig.Look(input.MouseX, input.MouseY);
        rig.Tick(input.Held, ground);
        Ticks++;
    }

    // The look between ticks, from where the rig faced at its last: the shared Smooth Motion policy, turned as the rig turns.
    private TitleLookPreview Look => _look ??= new(() => CanPreviewLook, () => (Human.Rig.Yaw, Human.Rig.Pitch), Turn, LookAxes);

    /// <summary>The look a heading and a pitch make, as the shared presenter reads one: local to world, its columns the right, forward and up axes.</summary>
    /// <param name="yaw">The heading, in radians; zero looks along +Y.</param>
    /// <param name="pitch">The pitch, in radians; positive looks up.</param>
    /// <returns>The orientation.</returns>
    /// <remarks>
    /// The camera's own matrix (<see cref="Core.Graphics.Camera3D.LookRotation"/>) has those axes as its rows, up before
    /// forward. Handed over as it is, the presenter read its columns as axes: right only at each tick, and turned about
    /// the wrong axes between them.
    /// </remarks>
    public static Matrix3f LookAxes(float yaw, float pitch)
    {
        Matrix3f camera = Core.Graphics.Camera3D.LookRotation(yaw, pitch);
        return Matrix3f.FromRows(camera.Row1, camera.Row3, camera.Row2).Transposed();
    }

    // The rig's turn (PlayerFirstPersonView.Turn): right turns right, the profile's invert in the pitch, slowed with a zoom.
    private (float Gamma, float Pitch) Turn(float mouseX, float mouseY, float sensitivity, bool invertMouse, float yaw, float pitch)
    {
        MouseLookSettings settings = MouseLookSettings.FromSensitivity(sensitivity, invertMouse);
        float zoom = Human.Rig.View is { } view ? MouseLookSettings.ZoomSensitivityScale(view.TanX) : 1f;
        return (yaw + (settings.YawDelta(mouseX) * zoom),
            Math.Clamp(pitch + (settings.PitchDelta(mouseY) * zoom), -PlayerBody.PitchLimit, PlayerBody.PitchLimit));
    }

    // The eye of a body standing on these feet, which the death camera orbits and looks at.
    private static Vector3d EyeOver(Vector3d feet) => new(feet.X, feet.Y, feet.Z + PlayerBody.StandingEyeHeight);
}
