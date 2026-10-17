using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>One chip of surface a round knocked loose.</summary>
/// <remarks>
/// A <c>Fragment</c> (<c>0x480312</c>) is a real model, not a sprite: the impact effect names it and
/// <c>0x47F0D0</c> puts one instance of it into a <c>SimpointObjContainer</c> that owns its motion.
/// </remarks>
public struct ImpactFragment
{
    /// <summary>Where it is, in world units.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Where it was at the end of the previous tick, for interpolated drawing.</summary>
    public Vector3d PreviousPosition { get; set; }

    /// <summary>How far it moves each tick, in world units.</summary>
    public Vector3f Velocity { get; set; }

    /// <summary>Its orientation.</summary>
    public Matrix3f Orientation { get; set; }

    /// <summary>The orientation it had at the end of the previous tick.</summary>
    public Matrix3f PreviousOrientation { get; set; }

    /// <summary>How far it turns each tick, as X/Y/Z radians.</summary>
    public Vector3f AngularVelocity { get; set; }

    /// <summary>Ticks left before it is removed.</summary>
    public int LifeTicks { get; set; }

    /// <summary>The model it is drawn with, as the struck material's effect block names it.</summary>
    public string Model { get; set; }
}

/// <summary>
/// What the <c>GunShotEffect</c> class (<c>0x438440</c>) does to a surface a round struck.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bullet never chooses this.</b> The shot march (<c>0x47B750</c>) records each crossing with the
/// struck face's material row, and <c>0x4083C0</c> dispatches it to that row's own effect class. Every
/// row in the retail data names <c>TASKTYPE_GUNSHOTEFFECT</c>, so this is what every impact runs, but
/// the <i>parameters</i> are the material's: whether it puffs, whether it chips, which texture the mark
/// uses and which model the chips are.
/// </para>
/// <para>
/// Three things happen, in this order, and each has its own gate:
/// </para>
/// <list type="number">
/// <item><description><b>Chips</b>, when the block's second flag is set and the struck object is not a
/// human. An <c>Explosion0</c> is placed at the contact and fires once on its next tick
/// (<c>0x480F40</c>), throwing <see cref="FragmentCount"/> instances of the block's model.</description></item>
/// <item><description><b>The mark</b>, whenever the block resolved a texture — no flag guards it. This
/// is <see cref="BulletMarks"/>' business.</description></item>
/// <item><description><b>The puff</b>, when the block's first flag is set, the struck object is not a
/// human, <i>and a coin comes up heads</i>. Half of all wall hits therefore show no puff at
/// all.</description></item>
/// </list>
/// <para>
/// Retail's own table (<c>material.qvm</c>) sets both flags on every surface a round can mark except
/// <c>Glass</c>, <c>Fence</c>, <c>MetalLadder</c> and <c>MetalFence</c>, and clears both on every
/// second block — so the far side of a penetrated wall gets its mark and nothing else.
/// </para>
/// </remarks>
public static class GunShotImpactEffects
{
    /// <summary>World units in a metre.</summary>
    public const float UnitsPerMetre = 4096f;

    // ---- the puff (0x4385AD onwards) -------------------------------------------------------

    /// <summary>The sprite the puff is drawn with.</summary>
    public const string PuffSprite = "concrete1.spr";

    /// <summary>The puff system's pool size — two, because it emits exactly two.</summary>
    public const int PuffCapacity = 2;

    /// <summary>The puff quad's authored size, in world units.</summary>
    /// <remarks>Four centimetres, before the growth multiplier.</remarks>
    public const float PuffSizeInUnits = 163.84f;

    /// <summary>What the puff's size multiplier gains each tick.</summary>
    /// <remarks>
    /// <c>0x3DB60B61</c>. Over a puff's forty-five ticks the quad reaches five times its authored
    /// size — twenty centimetres — which is the whole of the effect's growth.
    /// </remarks>
    public const float PuffGrowthPerTick = 0.08888889f;

    /// <summary>How far off the surface the first puff sits, in world units.</summary>
    /// <remarks><c>0x438624</c>: three times the authored size, along the struck face's normal.</remarks>
    public const float PuffLiftInUnits = PuffSizeInUnits * 3f;

    /// <summary>How much higher the second puff sits than the first, in world units.</summary>
    /// <remarks><c>0x4386B8</c>: a quarter of the authored size, straight up in world space.</remarks>
    public const float PuffSecondRiseInUnits = PuffSizeInUnits * 0.25f;

    /// <summary>How many ticks a puff holds full alpha.</summary>
    public const int PuffHoldTicks = 15;

    /// <summary>How many ticks a puff's fade takes.</summary>
    public const int PuffFadeTicks = 30;

    /// <summary>How much faster the second puff rises than the first.</summary>
    public const float PuffSecondSpeedScale = 1.25f;

    /// <summary>The slowest a puff rises, in kilometres per hour.</summary>
    /// <remarks>
    /// <c>0x4386E4</c> draws <c>rand × 0.75 + 0.15</c> and runs it through the same
    /// <c>4096000 / 108000</c> kilometres-per-hour conversion the spent casings use — in floating point
    /// here, so nothing is truncated.
    /// </remarks>
    public const float PuffSpeedMinimumKph = 0.15f;

    /// <summary>How much speed the puff's draw spans, in kilometres per hour.</summary>
    public const float PuffSpeedRangeKph = 0.75f;

    /// <summary>Units per tick in one kilometre per hour.</summary>
    /// <remarks>A kilometre is 4096000 units and an hour is 108000 ticks.</remarks>
    public const float UnitsPerTickPerKph = 4096000f / 108000f;

    /// <summary>The smallest scale a puff quad is drawn at.</summary>
    public const float PuffAlphaMinimum = 0.75f;

    /// <summary>How much the puff's starting alpha draw spans.</summary>
    public const float PuffAlphaRange = 0.25f;

    /// <summary>The middle of the spin a puff quad is given, in radians per tick.</summary>
    /// <remarks><c>0x3A51B717</c> — about three quarters of a degree a tick.</remarks>
    public const float SpinCentreRadians = 0.012799081f;

    /// <summary>How far either side of <see cref="SpinCentreRadians"/> the spin is drawn.</summary>
    /// <remarks><c>0x3D566CF3</c> — three degrees, so the spin runs from −2.25° to +3.75° a tick.</remarks>
    public const float SpinSpreadRadians = 0.052359879f;

    // ---- the chips (0x4384C9, then 0x480F40 -> 0x47F200 -> 0x47F0D0) ------------------------

    /// <summary>How many chips a hit throws, at fewest.</summary>
    public const int FragmentCountMinimum = 2;

    /// <summary>How many whole counts the chip draw spans.</summary>
    /// <remarks>Three, so a hit throws two, three or four chips.</remarks>
    public const int FragmentCountRange = 3;

    /// <summary>The widest a chip may leave the bullet's line, in radians.</summary>
    /// <remarks><c>0x3F060A92</c> — thirty degrees. The draw is one-sided: <c>rand × cone</c>.</remarks>
    public const float FragmentConeRadians = 0.5235988f;

    /// <summary>The slowest a chip leaves the surface, in world units per tick.</summary>
    public const float FragmentSpeedMinimum = 340f;

    /// <summary>The fastest.</summary>
    public const float FragmentSpeedMaximum = 680f;

    /// <summary>How many ticks a chip lives.</summary>
    /// <remarks><c>0x47F0D0</c> passes forty-five to the container, which stamps it on every child.</remarks>
    public const int FragmentLifeTicks = 45;

    /// <summary>What a chip's velocity is multiplied by each tick.</summary>
    public const float FragmentDrag = 0.98f;

    /// <summary>The engine's gravity, in world units per tick².</summary>
    /// <remarks>The same 9.8 m/s² everything else in the world falls at.</remarks>
    public const float GravityPerTick = 44.600887f;

    /// <summary>How much of gravity a chip feels.</summary>
    /// <remarks>
    /// The impact passes <c>2048</c> and <c>0x4812AC</c> scales it by <c>0.000219726557</c>, giving
    /// <c>0.45</c>. The container then applies <c>scale × gravity × drag</c> every tick
    /// (<c>0x5035FA</c>), so a chip falls at a little under half the rate a body does.
    /// </remarks>
    public const float FragmentGravityScale = 0.45f;

    /// <summary>The chip's downward acceleration, in world units per tick².</summary>
    public const float FragmentGravityPerTick = FragmentGravityScale * GravityPerTick * FragmentDrag;

    /// <summary>What a chip's angular velocity is multiplied by each tick.</summary>
    public const float FragmentAngularDrag = 0.99f;

    /// <summary>The widest tumble a chip is given, in radians per tick.</summary>
    /// <remarks><c>0x3F20D97C</c> — thirty-six degrees a tick on each axis, drawn one-sided.</remarks>
    public const float FragmentTumbleRadians = 0.62831855f;

    /// <summary>The widest the chip's model is turned when it is placed, in radians.</summary>
    /// <remarks><c>0x3D009BC9</c> — 1.8°, drawn one-sided on each axis.</remarks>
    public const float FragmentPlacementRadians = 0.031415928f;

    /// <summary>How large a chip's model is drawn.</summary>
    /// <remarks><c>0x47F146</c>: <c>204.8 × 0.00048828125</c>, which is exactly a tenth.</remarks>
    public const float FragmentModelScale = 0.1f;

    /// <summary>Decides whether a hit shows its puff.</summary>
    /// <param name="effect">The struck material's effect block.</param>
    /// <param name="struckHuman">Whether the object hit was a human.</param>
    /// <param name="coin">A draw of <c>rand() &amp; 1</c>.</param>
    /// <returns><see langword="true"/> when two puff quads should be emitted.</returns>
    /// <remarks>
    /// <b>A body never puffs.</b> <c>0x438458</c> asks the struck object whether it is a Human before
    /// anything else, and both the puff and the chips are skipped when it is — which is why shooting a
    /// guard throws blood and not concrete dust, even though a guard's collision names a material like
    /// any other surface.
    /// </remarks>
    public static bool SpawnsPuff(in GunShotEffect effect, bool struckHuman, int coin) =>
        effect.SpawnsParticles && !struckHuman && (coin & 1) != 0;

    /// <summary>Decides whether a hit throws chips.</summary>
    /// <param name="effect">The struck material's effect block.</param>
    /// <param name="struckHuman">Whether the object hit was a human.</param>
    /// <returns><see langword="true"/> when an <c>Explosion0</c> should be placed.</returns>
    /// <remarks>There is no coin here — the chips are unconditional where the flag is set.</remarks>
    public static bool ThrowsFragments(in GunShotEffect effect, bool struckHuman) =>
        effect.LeavesMark && !struckHuman && effect.Model.Length > 0;

    /// <summary>How many chips one hit throws.</summary>
    /// <param name="uniformSample">A uniform integer on 0 to <see cref="FragmentCountRange"/> minus one.</param>
    /// <returns>Two, three or four.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample is outside its range.</exception>
    public static int FragmentCount(int uniformSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uniformSample);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(uniformSample, FragmentCountRange);

        return uniformSample + FragmentCountMinimum;
    }

    /// <summary>How fast a puff quad rises, in world units per tick.</summary>
    /// <param name="uniformSample">A uniform draw on <c>[0, 1)</c>.</param>
    /// <returns>The rise, straight up in world space.</returns>
    public static float PuffRiseSpeed(float uniformSample) =>
        ((uniformSample * PuffSpeedRangeKph) + PuffSpeedMinimumKph) * UnitsPerTickPerKph;

    /// <summary>How far a quad turns each tick.</summary>
    /// <param name="uniformSample">A uniform draw on <c>[0, 1)</c>.</param>
    /// <returns>The spin, in radians per tick.</returns>
    /// <remarks>
    /// The original writes <c>(r - 0.5 + r - 0.5) × spread + centre</c>, which is the same draw the
    /// blood spray uses for its own quads.
    /// </remarks>
    public static float Spin(float uniformSample) =>
        (((uniformSample - 0.5f) * 2f) * SpinSpreadRadians) + SpinCentreRadians;

    /// <summary>Draws one chip's launch velocity, as <c>0x47F4A5</c>–<c>0x47FF1B</c> composes it.</summary>
    /// <param name="direction">The round's own travel direction.</param>
    /// <param name="random">The draw source.</param>
    /// <returns>The velocity, in world units per tick.</returns>
    /// <remarks>
    /// <para>
    /// The original draws the <b>speed first</b>, then a roll on <c>[0, π)</c>, then a pitch on
    /// <c>[0, cone)</c>, and that order is kept so every later draw in the tick lands on the same value.
    /// </para>
    /// <para>
    /// <c>0x4B4010</c> builds an orthonormal frame whose <b>rows</b> are <c>u</c>, <c>w</c> and the
    /// direction, where <c>u = normalize(cross(s, d))</c> and <c>w = normalize(cross(d, u))</c> for the
    /// horizontal helper <c>s = normalize(d.y, −d.x, 0)</c> — <c>(0, 1, 0)</c> when the direction is
    /// straight up or down. <c>0x47F515</c> then transposes it, so the direction becomes the third
    /// <i>column</i>, and the launch is that column after the frame is turned by
    /// <c>Rz(roll)·Rx(pitch)</c> on the right:
    /// <c>cos p · d + sin p · (sin r · u − cos r · w)</c>.
    /// </para>
    /// <para>
    /// The roll spans only half a turn, so the chips leave in a <b>half</b>-cone rather than a full one.
    /// That is the original's draw and it is kept.
    /// </para>
    /// </remarks>
    public static Vector3f FragmentLaunch(Vector3f direction, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        float speed = FragmentSpeed(random.NextSingle());

        Vector3f axis = direction.ReciprocalNormalizedOr(Vector3f.UnitZ);
        Vector3f helper = axis.X == 0f && axis.Y == 0f
            ? new Vector3f(0f, 1f, 0f)
            : (new Vector3f(axis.Y, -axis.X, 0f)).ReciprocalNormalizedOr(Vector3f.UnitZ);
        Vector3f u = (Vector3f.Cross(helper, axis)).ReciprocalNormalizedOr(Vector3f.UnitZ);
        Vector3f w = (Vector3f.Cross(axis, u)).ReciprocalNormalizedOr(Vector3f.UnitZ);

        float roll = random.NextSingle() * MathF.PI;
        float pitch = random.NextSingle() * FragmentConeRadians;
        float rollSin = MathF.Sin(roll);
        float rollCos = MathF.Cos(roll);
        float pitchSin = MathF.Sin(pitch);
        float pitchCos = MathF.Cos(pitch);

        Vector3f launch = (new Vector3f(
            (axis.X * pitchCos) + (pitchSin * ((rollSin * u.X) - (rollCos * w.X))),
            (axis.Y * pitchCos) + (pitchSin * ((rollSin * u.Y) - (rollCos * w.Y))),
            (axis.Z * pitchCos) + (pitchSin * ((rollSin * u.Z) - (rollCos * w.Z))))).ReciprocalNormalizedOr(Vector3f.UnitZ);

        return launch * speed;
    }

    /// <summary>Draws one chip's launch speed.</summary>
    /// <param name="uniformSample">A uniform draw on <c>[0, 1)</c>.</param>
    /// <returns>The speed in world units per tick.</returns>
    public static float FragmentSpeed(float uniformSample) =>
        (uniformSample * (FragmentSpeedMaximum - FragmentSpeedMinimum)) + FragmentSpeedMinimum;

}
