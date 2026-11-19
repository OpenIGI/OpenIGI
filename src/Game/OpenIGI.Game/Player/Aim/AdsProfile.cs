using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player.Aim;

/// <summary>How a weapon is raised when its player aims.</summary>
public enum AdsKind
{
    /// <summary>Brought to the eye with its sights on the line of sight.</summary>
    Sights = 0,

    /// <summary>Held tighter and higher without sights to look through: a pair of pistols, one in each hand.</summary>
    Focus = 1,
}

/// <summary>The kick a shot gives the weapon in the hands, on top of what the original puts in the view.</summary>
/// <param name="BackMetres">How far the weapon is driven back towards the eye.</param>
/// <param name="UpMetres">How far it is driven up.</param>
/// <param name="PitchDegrees">How far the muzzle climbs.</param>
/// <param name="YawDegrees">How far the muzzle may wander to either side, at random.</param>
/// <param name="RollDegrees">How far the weapon may roll to either side, at random.</param>
public readonly record struct AdsKick(float BackMetres, float UpMetres, float PitchDegrees, float YawDegrees, float RollDegrees);

/// <summary>How one weapon aims down its sights: where they are, how fast it comes up, and what aiming changes.</summary>
/// <remarks>
/// <para>
/// The sights are two points in the frame of the bone the weapon hangs on (<see cref="HumanAttachPoints.FirstPersonWeapon"/>),
/// in metres: the rear sight's aperture or notch and the tip of the front post. Aiming lays the line through them on the eye's
/// line of sight with the rear sight <see cref="EyeReliefMetres"/> ahead of the eye. They were measured off each shipped
/// model's mesh, not tuned by eye.
/// </para>
/// <para>
/// Every scale is what the full aim makes of the hip's value; between the two the aim's progress blends them.
/// </para>
/// </remarks>
public sealed record AdsProfile
{
    /// <summary>Gets the weapon's script id, as <see cref="Weapons.WeaponType.ScriptId"/>.</summary>
    public required string ScriptId { get; init; }

    /// <summary>Gets how the weapon is raised.</summary>
    public AdsKind Kind { get; init; } = AdsKind.Sights;

    /// <summary>Gets the rear sight, in metres in the weapon bone's frame.</summary>
    public Vector3f RearSight { get; init; }

    /// <summary>Gets the front sight, in metres in the weapon bone's frame.</summary>
    public Vector3f FrontSight { get; init; } = new(0f, 0.3f, 0f);

    /// <summary>Gets how far ahead of the eye the rear sight sits when aimed, in metres.</summary>
    public float EyeReliefMetres { get; init; } = 0.18f;

    /// <summary>Gets where a <see cref="AdsKind.Focus"/> weapon moves to, in metres in the eye's frame (right, ahead, up).</summary>
    public Vector3f FocusOffsetMetres { get; init; }

    /// <summary>Gets how long the weapon takes to come up to the eye, in seconds.</summary>
    public float InSeconds { get; init; } = 0.22f;

    /// <summary>Gets how long it takes to go back down, in seconds.</summary>
    public float OutSeconds { get; init; } = 0.16f;

    /// <summary>Gets how much the view is magnified through the sights; one is not at all.</summary>
    public float Magnification { get; init; } = 1.25f;

    /// <summary>Gets the view model's horizontal half-angle tangent through the sights; the hip's is <c>tan 30°</c>.</summary>
    public float ViewModelTanX { get; init; } = HumanView.DefaultViewModelTanX;

    /// <summary>Gets what aiming makes of the cone a round strays in.</summary>
    public float SpreadScale { get; init; } = 0.3f;

    /// <summary>Gets what aiming makes of the kick a round puts in the view.</summary>
    public float RecoilScale { get; init; } = 0.7f;

    /// <summary>Gets what aiming makes of the speed the player moves at.</summary>
    public float MoveScale { get; init; } = 0.7f;

    /// <summary>Gets what aiming makes of the weapon's sway as the view turns and the body moves.</summary>
    public float SwayScale { get; init; } = 0.25f;

    /// <summary>Gets how much of the weapon clip's own motion the aim holds still, nought (none) to one (all of it).</summary>
    /// <remarks>
    /// The original's clips sway a weapon held at the hip and throw it about on every shot. Through the sights that motion
    /// would take the sight picture with it, so this much of it is held, and the kick is put back as <see cref="Kick"/>.
    /// </remarks>
    public float Steadiness { get; init; } = 0.7f;

    /// <summary>Gets the kick a shot gives the weapon at the hip; through the sights it is <see cref="AimedKickScale"/> of it.</summary>
    public AdsKick Kick { get; init; } = new(0.012f, 0.004f, 2f, 0.6f, 1.2f);

    /// <summary>Gets what aiming makes of <see cref="Kick"/>.</summary>
    public float AimedKickScale { get; init; } = 0.6f;

    /// <summary>Gets whether the weapon can be braced on a ledge, table or edge.</summary>
    public bool CanMount { get; init; } = true;
}
