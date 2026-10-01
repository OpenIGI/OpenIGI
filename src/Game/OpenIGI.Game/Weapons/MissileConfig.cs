namespace OpenIGI.Game.Weapons;

/// <summary>
/// The per-missile flight parameters the retail <c>Missile</c> task reads from its config struct
/// (<c>cfg = *(task+436)+724</c>, consumed by the integrator at <c>0x00428D00</c> and the smoke
/// emitter at <c>0x00428630</c>).
/// </summary>
/// <remarks>
/// <para>
/// A retail launcher does not carry these in its <c>DefineWeaponType</c> row — that call only hands the
/// missile projectile its model name (<see cref="WeaponTaskParameters"/>). The flight behaviour lives in
/// the task config, keyed by <see cref="MissileType"/>. The clean-room engine has no task-config parser,
/// so the values here are transcribed from the reversed integrator and grouped into the per-type presets
/// the retail rockets fly with.
/// </para>
/// <para>
/// The LAW 80 / RPG-18 is a <see cref="MissileType.MissileDirect"/> rocket: <c>0x00428910</c> clears its
/// steering inputs so it holds the launch facing, and <c>0x00428D00</c> ramps a throttle from launch to
/// full and drives the body forward under thrust with no grenade gravity.
/// </para>
/// </remarks>
public sealed record MissileConfig
{
    /// <summary>Gets the guidance regime this config flies under.</summary>
    public required MissileType Type { get; init; }

    /// <summary>Gets whether the missile lays a smoke trail (<c>cfg+280</c>, <see cref="MissileSmoke"/>).</summary>
    public bool Smoke { get; init; } = true;

    /// <summary>Gets the speed the rocket leaves the tube at, in metres a second.</summary>
    /// <remarks>
    /// Retail launches a rocket slow and lets thrust build it up, so the trail visibly accelerates away
    /// from the muzzle rather than snapping to cruise. A handheld LAW leaves around twenty metres a
    /// second.
    /// </remarks>
    public float LaunchMetresPerSecond { get; init; } = 20f;

    /// <summary>Gets the speed thrust drives the rocket up to and holds, in metres a second.</summary>
    /// <remarks>
    /// The cruise a <see cref="MissileType.MissileDirect"/> body settles at once its throttle is full and
    /// thrust balances drag. A LAW/RPG rocket crosses a hundred-metre courtyard in a couple of seconds.
    /// </remarks>
    public float CruiseMetresPerSecond { get; init; } = 75f;

    /// <summary>
    /// Gets the throttle ramp per tick while the throttle is below the fast threshold (<c>cfg+36</c>).
    /// </summary>
    public float ThrottleRampSlow { get; init; } = 0.06f;

    /// <summary>
    /// Gets the throttle ramp per tick once the throttle reaches the fast threshold (<c>cfg+32</c>).
    /// </summary>
    /// <remarks><c>0x00428D2C</c> switches to the fast rate at a throttle of <c>0.7</c>.</remarks>
    public float ThrottleRampFast { get; init; } = 0.12f;

    /// <summary>Gets the per-tick velocity retention, the linear drag from <c>0x00429494</c>.</summary>
    /// <remarks>The body integrator passes <c>0.95</c> along its facing and <c>0.92</c> across it.</remarks>
    public float Drag { get; init; } = 0.95f;

    /// <summary>Gets whether the body falls under gravity while it flies.</summary>
    /// <remarks>
    /// A <see cref="MissileType.MissileDirect"/> LAW/RPG holds its line — thrust dominates and the trail
    /// reads as flat over any range the player fires it at, so gravity stays off. A dropped
    /// <see cref="MissileType.Bomb"/> is the one body that falls.
    /// </remarks>
    public bool Gravity { get; init; }

    /// <summary>Gets how long, in ticks, the rocket motor burns — thrusting and laying smoke.</summary>
    /// <remarks>
    /// <c>a1+2148</c> is the burn countdown: <c>0x0042939A</c> subtracts <c>cfg+292</c> each tick, and
    /// while it is non-zero the missile thrusts and — with <c>cfg+280</c> set — smokes (the gate at
    /// <c>0x00428445</c>). Once it reaches zero the body coasts and the integrator adds its idle wobble.
    /// Three seconds of burn covers any range the player fires a LAW over.
    /// </remarks>
    public int MotorBurnTicks { get; init; } = 90;

    /// <summary>Gets how many ticks between smoke puffs (<c>a1+2176</c> in <c>0x00428630</c>).</summary>
    public int SmokeIntervalTicks { get; init; } = 1;

    /// <summary>Gets the world-unit radius a fresh smoke puff starts at.</summary>
    public float SmokeStartRadiusUnits { get; init; } = 1024f;

    /// <summary>Gets how many world units a smoke puff grows each tick as it drifts and thins.</summary>
    public float SmokeGrowthUnitsPerTick { get; init; } = 512f;

    /// <summary>Gets how many ticks a smoke puff lives.</summary>
    public int SmokeLifeTicks { get; init; } = 34;

    /// <summary>The LAW 80 / RPG-18 handheld rocket: flies straight under thrust, trails smoke.</summary>
    public static MissileConfig Direct { get; } = new() { Type = MissileType.MissileDirect };

    /// <summary>The keyboard-steered rocket: the same straight-thrust body, player-aimed.</summary>
    public static MissileConfig Keyboard { get; } = new() { Type = MissileType.MissileKeyboard };

    /// <summary>A lobbed rocket that arcs onto its target.</summary>
    public static MissileConfig Ballistic { get; } = new()
    {
        Type = MissileType.MissileBallistic,
        CruiseMetresPerSecond = 60f,
    };

    /// <summary>An anti-tank missile that lofts past thirty metres and dives onto armour.</summary>
    public static MissileConfig AntiTank { get; } = new()
    {
        Type = MissileType.MissileAntiTank,
        CruiseMetresPerSecond = 90f,
    };

    /// <summary>The Hind's own rocket, launched along the airframe axis.</summary>
    public static MissileConfig Hind { get; } = new()
    {
        Type = MissileType.HindHelicopter,
        CruiseMetresPerSecond = 120f,
    };

    /// <summary>A dropped bomb: no steering case, and the one body that falls.</summary>
    public static MissileConfig Bomb { get; } = new()
    {
        Type = MissileType.Bomb,
        Smoke = false,
        Gravity = true,
        LaunchMetresPerSecond = 0f,
        CruiseMetresPerSecond = 0f,
        MotorBurnTicks = 0,
    };

    /// <summary>Gets the retail preset a guidance type flies under.</summary>
    /// <param name="type">The missile guidance regime.</param>
    /// <returns>The matching preset; <see cref="Direct"/> for anything unrecognised.</returns>
    public static MissileConfig ForType(MissileType type) => type switch
    {
        MissileType.MissileDirect => Direct,
        MissileType.MissileKeyboard => Keyboard,
        MissileType.MissileBallistic => Ballistic,
        MissileType.MissileAntiTank => AntiTank,
        MissileType.HindHelicopter => Hind,
        MissileType.Bomb => Bomb,
        _ => Direct,
    };
}
