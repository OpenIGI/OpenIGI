using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The over-the-shoulder view, and the move between it and the player's own eyes.
/// </summary>
/// <remarks>
/// <para>
/// <b>A remaster addition, and it says so.</b> IGI1 has five camera modes (<see cref="OpenIGI.Game.Igi1.Player.HumanCameraMode"/>)
/// and none of them is a free third-person gameplay view; the two the port already draws from outside the
/// body — the death camera and the ladder's mode 3 — are transcriptions, and this is not. Nothing here
/// claims a retail address.
/// </para>
/// <para>
/// <b>It changes only where the frame is rasterised from.</b> The eye a round leaves, the ear the world is
/// heard through, the wall probe, the aim angles and every tick of the simulation stay exactly where the
/// first-person view put them: this takes the camera the player would have had and moves it back along
/// that camera's own axes. Which is also what makes the transition cheap to get right — the orientation
/// never changes, so there is no rotation to interpolate and no moment where the view is pointing
/// somewhere neither mode would have.
/// </para>
/// <para>The move itself is the feature. Three things make it read as a camera move rather than a cut:</para>
/// <list type="number">
/// <item>
/// The blend is <b>smootherstep</b> (<c>6t⁵ − 15t⁴ + 10t³</c>), whose first <em>and</em> second
/// derivatives vanish at both ends. A linear blend starts and stops abruptly however long it takes, and
/// plain smoothstep still steps in acceleration; this leaves and arrives at rest.
/// </item>
/// <item>
/// The camera travels an <b>arc</b>, not a line: the shoulder offset and the pull-back are separate legs
/// of the same eased parameter, so the eye swings out past the shoulder rather than reversing straight
/// out of the character's skull.
/// </item>
/// <item>
/// Reversing mid-move is continuous, because the blend is a <em>position</em> that is driven toward its
/// target rather than an animation that is restarted. Tapping the key twice quickly makes the camera
/// turn around where it is.
/// </item>
/// </list>
/// <para>
/// Collision is two rays and one asymmetry. The lateral leg and the pull-back leg are cast separately, so
/// a camera pressed sideways into a wall gives up its shoulder offset before it gives up its distance.
/// The limit <b>snaps in</b> and <b>eases out</b>: a wall arriving must take effect on the frame it
/// arrives or the near plane cuts through it, while a wall leaving must be released over a few tenths of
/// a second or every doorway fires the camera backwards.
/// </para>
/// </remarks>
public sealed class ThirdPersonCamera
{
    /// <summary>World units in a metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>
    /// How far short of blocking geometry the eye is held, in metres.
    /// </summary>
    /// <remarks>
    /// A ray finds the point a wall is at; the camera needs the point at which the wall is not yet inside
    /// its near plane. At the 90° horizontal field of view the world is drawn with, the near plane's own
    /// half-diagonal is about 0.16 m, so this is that with room for the rest of the frustum to clear a
    /// corner the single ray never tested.
    /// </remarks>
    public const double CollisionMarginInMetres = 0.35;

    /// <summary>How fast the eye is allowed to move back out once the way is clear, in metres a second.</summary>
    /// <remarks>
    /// Only the recovery is rate-limited. Coming in is instant — see the class remarks — so this number
    /// can be slow enough to look deliberate without ever being what puts a wall through the near plane.
    /// </remarks>
    public const double RecoveryMetresPerSecond = 3.5;

    /// <summary>How far from the eye the camera must be before the first-person arms are put away.</summary>
    private const double ArmsAwayInMetres = 0.2;

    /// <summary>How far back in it must be before they are brought out again.</summary>
    private const double ArmsBackInMetres = 0.14;

    /// <summary>How far from the eye the camera must be before the character is drawn.</summary>
    private const double BodyShownInMetres = 0.45;

    /// <summary>How far back in it must be before the character is taken away again.</summary>
    private const double BodyHiddenInMetres = 0.36;

    private float _blend;
    private double _limitInMetres = double.PositiveInfinity;
    private Camera3D _camera = Camera3D.Default;
    private bool _showsBody;
    private bool _showsViewModel = true;

    /// <summary>Gets or sets how far behind the player the eye settles, in metres.</summary>
    public double DistanceInMetres { get; set; } = 2.6;

    /// <summary>Gets or sets how far right of the player the eye sits, in metres.</summary>
    public double ShoulderInMetres { get; set; } = 0.55;

    /// <summary>Gets or sets how far above the player's eye the camera sits, in metres.</summary>
    public double HeightInMetres { get; set; } = 0.25;

    /// <summary>Gets or sets how long the move between the two views takes, in seconds.</summary>
    /// <remarks>Zero is a cut, which is a legitimate thing to ask for and the only way to get one.</remarks>
    public double BlendSeconds { get; set; } = 0.35;

    /// <summary>Gets how far along the move the camera is, before easing — 0 at the eye, 1 at the shoulder.</summary>
    public float Blend => _blend;

    /// <summary>Gets the eased share of the move, which is what the offsets are actually scaled by.</summary>
    public float Eased => Smootherstep(_blend);

    /// <summary>Gets whether the camera is anywhere other than exactly at the player's eye.</summary>
    /// <remarks>
    /// The gate for everything the rest of the frame has to do differently — drawing the body, standing
    /// the arms down, projecting the crosshair. It stays true for the whole of the move back, which is
    /// what keeps the body on screen until the camera has actually arrived at the eye.
    /// </remarks>
    public bool IsEngaged => _blend > 0f;

    /// <summary>Gets how far the camera ended up from the eye it was built off, in metres.</summary>
    /// <remarks>
    /// The measure the view model and the body are switched on by, rather than the blend: a camera whose
    /// pull-back a wall has taken away is <em>at</em> the eye however far through the move it is, and
    /// putting a character's head there would fill the screen with the inside of it.
    /// </remarks>
    public double OffsetInMetres { get; private set; }

    /// <summary>Gets the eye the scene is drawn from.</summary>
    public Camera3D Camera => _camera;

    /// <summary>Gets whether the character should be drawn this frame.</summary>
    /// <remarks>
    /// <para>
    /// A distance with hysteresis, not a share of the move. The distance because a camera whose pull-back
    /// a wall has taken away is at the eye however far through the move it is, and a head drawn there is
    /// the inside of a skull filling the screen. The hysteresis because that distance is decided partly
    /// by geometry: standing with your back a couple of feet from a wall puts the camera right at the
    /// threshold, and a bare comparison would flicker the character in and out as the player shuffled.
    /// </para>
    /// <para>
    /// Which also gives the view a graceful floor. Backed hard into a corner the camera has nowhere to go,
    /// <see cref="ShowsViewModel"/> comes back true and <see cref="ShowsBody"/> false, and the frame is
    /// simply the first-person one until there is room again — rather than a close-up of the inside of
    /// the player's own head.
    /// </para>
    /// </remarks>
    public bool ShowsBody => _showsBody;

    /// <summary>Gets whether the first-person arms and their weapon should be drawn this frame.</summary>
    /// <remarks>
    /// Its own threshold rather than the complement of <see cref="ShowsBody"/>: the arms are anchored to
    /// the eye and are unusable long before a head is far enough away to be worth drawing. Between the
    /// two the frame shows neither, which at the default blend lasts about a twentieth of a second.
    /// </remarks>
    public bool ShowsViewModel => _showsViewModel;

    /// <summary>Puts the camera back at the player's eye at once, with no move.</summary>
    /// <remarks>
    /// For the cuts the blend must not smooth over: a level change, a spawn, a cutscene taking the view.
    /// Interpolating across one of those draws the frame from a line between two unrelated places.
    /// </remarks>
    public void Reset()
    {
        _blend = 0f;
        _limitInMetres = double.PositiveInfinity;
        OffsetInMetres = 0.0;
        _showsBody = false;
        _showsViewModel = true;
        _camera = Camera3D.Default;
    }

    /// <summary>Advances the move and places the eye for one frame.</summary>
    /// <param name="seconds">Real elapsed time since the last frame — the move is not a tick.</param>
    /// <param name="wanted">Whether the third-person view is being asked for.</param>
    /// <param name="firstPerson">The camera the frame would have been drawn from.</param>
    /// <param name="walls">A world ray test for the pull-in, or <see langword="null"/> where none exists.</param>
    /// <returns>
    /// The camera to draw from. At <see cref="Blend"/> zero this is <paramref name="firstPerson"/>
    /// unchanged, component for component, so nothing downstream can tell the feature is compiled in.
    /// </returns>
    public Camera3D Update(
        float seconds,
        bool wanted,
        in Camera3D firstPerson,
        WorldWalls.Blocked? walls = null)
    {
        double step = BlendSeconds > 0.0 ? Math.Clamp(seconds, 0f, 0.1f) / BlendSeconds : 1.0;
        _blend = wanted
            ? (float)Math.Min(1.0, _blend + step)
            : (float)Math.Max(0.0, _blend - step);

        if (_blend <= 0f)
        {
            // Exactly the camera handed in, and the limit forgotten: the next engage starts from
            // whatever the world is then rather than from the wall this one ended against.
            _limitInMetres = double.PositiveInfinity;
            OffsetInMetres = 0.0;
            _showsBody = false;
            _showsViewModel = true;
            _camera = firstPerson;
            return firstPerson;
        }

        Matrix3f orientation = firstPerson.Orientation;
        Vector3d right = new(orientation.M11, orientation.M12, orientation.M13);
        Vector3d up = new(orientation.M21, orientation.M22, orientation.M23);
        Vector3d forward = new(orientation.M31, orientation.M32, orientation.M33);

        Vector3d pivot = firstPerson.Position;

        // The lateral leg, at FULL extension. Casting the ray the camera is heading for rather than the
        // one it has reached keeps the ray origin still: a ray that starts where the camera currently is
        // moves every frame, and a moving ray origin against a corner is a camera that shivers.
        Vector3d lateral = new(
            (right.X * ShoulderInMetres) + (up.X * HeightInMetres),
            (right.Y * ShoulderInMetres) + (up.Y * HeightInMetres),
            (right.Z * ShoulderInMetres) + (up.Z * HeightInMetres));
        double lateralLength = lateral.Length;
        double lateralShare = 1.0;
        if (walls is { } lateralWalls && lateralLength > 0.0)
        {
            Vector3d target = Add(pivot, lateral, 1.0);
            if (lateralWalls(ToUnits(pivot), ToUnits(target), out Vector3d hit))
            {
                double reach = (Subtract(FromUnits(hit), pivot)).Length - CollisionMarginInMetres;
                lateralShare = Math.Clamp(reach / lateralLength, 0.0, 1.0);
            }
        }

        Vector3d shoulder = Add(pivot, lateral, lateralShare);

        // The pull-back leg, also at full extension, for the same reason and so the eased distance below
        // is the ONLY thing the blend drives. Were the ray shortened by the blend as well, the limit
        // would grow as the move progressed and the recovery rate would throttle the move itself.
        double allowed = DistanceInMetres;
        if (walls is { } backWalls && DistanceInMetres > 0.0)
        {
            Vector3d target = Add(shoulder, forward, -DistanceInMetres);
            if (backWalls(ToUnits(shoulder), ToUnits(target), out Vector3d hit))
            {
                allowed = Math.Clamp(
                    (Subtract(FromUnits(hit), shoulder)).Length - CollisionMarginInMetres,
                    0.0,
                    DistanceInMetres);
            }
        }

        // In at once, out over time. See the class remarks: the asymmetry is the whole behaviour.
        _limitInMetres = allowed < _limitInMetres
            ? allowed
            : Math.Min(allowed, _limitInMetres + (RecoveryMetresPerSecond * Math.Clamp(seconds, 0f, 0.1f)));

        double eased = Smootherstep(_blend);
        double back = _limitInMetres * eased;
        Vector3d eye = Add(Add(pivot, lateral, lateralShare * eased), forward, -back);

        OffsetInMetres = (Subtract(eye, pivot)).Length;
        _showsBody = OffsetInMetres >= (_showsBody ? BodyHiddenInMetres : BodyShownInMetres);
        _showsViewModel = OffsetInMetres < (_showsViewModel ? ArmsAwayInMetres : ArmsBackInMetres);

        // The orientation is the first-person one, untouched. That is not a shortcut: it is what keeps
        // the crosshair's ray, the world's motion vectors and the player's own aim in one frame during
        // the move, and what makes the move a dolly rather than a swing.
        _camera = new Camera3D(eye, orientation);
        return _camera;
    }

    /// <summary>The blend's easing — zero velocity <b>and</b> zero acceleration at both ends.</summary>
    /// <param name="t">The raw share of the move, 0 to 1.</param>
    /// <returns>The eased share.</returns>
    public static float Smootherstep(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return x * x * x * ((x * ((x * 6f) - 15f)) + 10f);
    }

    private static Vector3d Add(Vector3d origin, Vector3d direction, double scale) => new(
        origin.X + (direction.X * scale),
        origin.Y + (direction.Y * scale),
        origin.Z + (direction.Z * scale));

    private static Vector3d Subtract(Vector3d left, Vector3d right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static Vector3d ToUnits(Vector3d metres) =>
        new(metres.X * UnitsPerMetre, metres.Y * UnitsPerMetre, metres.Z * UnitsPerMetre);

    private static Vector3d FromUnits(Vector3d units) =>
        new(units.X / UnitsPerMetre, units.Y / UnitsPerMetre, units.Z / UnitsPerMetre);
}
