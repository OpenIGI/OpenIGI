using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// What a round does to a body — the mist, the droplets, and the marks both leave
/// (<c>0x463AD0</c> and its droplet callback <c>0x4640B0</c>).
/// </summary>
/// <remarks>
/// <para>
/// The <c>Human</c> class binds this to its damage message (<c>0x45F94A</c>, the very first thing
/// <c>0x45F930</c> does), so <b>every</b> human takes it: guards, the player, and any body a round
/// reaches. It is gated only on the blood game option, which <c>0x463AD9</c> reads before anything
/// else.
/// </para>
/// <para>
/// Four things happen, and none of them depends on the surface the body is standing on:
/// </para>
/// <list type="number">
/// <item><description><b>The backstop mark.</b> A trace runs <see cref="BackstopRangeInUnits"/> on from
/// the wound along the round's own line. If it lands on static world geometry whose material marks at
/// all, one of <c>hole_blood1.tex</c>/<c>hole_blood2.tex</c> is stamped there at a radius of
/// <see cref="BackstopRadiusMinimumInUnits"/> to twice that. This is the splash behind the
/// target — which is why it appears on the wall a guard was standing in front of and nowhere else.</description></item>
/// <item><description><b>The mist.</b> One <c>blood1.spr</c> quad at the wound, motionless, a third of
/// a metre across, holding for <see cref="MistHoldTicks"/> ticks while its seven frames play, then
/// gone in one more.</description></item>
/// <item><description><b>The forward spray.</b> <see cref="ForwardDropletCount"/> <c>blood2.spr</c>
/// droplets into a <see cref="ForwardConeRadians">ten-degree</see> cone along the round's line, fast.</description></item>
/// <item><description><b>The back spray.</b> <see cref="BackDropletCount"/> droplets into a
/// <see cref="BackConeRadians">seventy-degree</see> cone <i>against</i> it, slow — the splashback
/// towards the shooter.</description></item>
/// </list>
/// <para>
/// The droplets fall under a third of gravity and are traced against the world every sixteen ticks;
/// where one lands facing the surface it stamps <c>hole_blood3.tex</c> or <c>hole_blood4.tex</c> and
/// dies. So the pattern on the walls really is a function of the angle of the shot and where the body
/// was standing — nothing about it is authored.
/// </para>
/// </remarks>
public static class BloodEffects
{
    /// <summary>The sprite the wound mist is drawn with.</summary>
    public const string MistSprite = "blood1.spr";

    /// <summary>The sprite one flying droplet is drawn with.</summary>
    public const string DropletSprite = "blood2.spr";

    /// <summary>The marks a backstop splash may leave, in the order <c>rand(2)</c> selects them.</summary>
    public static readonly string[] BackstopTextures = ["hole_blood1.tex", "hole_blood2.tex"];

    /// <summary>The marks a landing droplet may leave, in the order <c>rand(2)</c> selects them.</summary>
    public static readonly string[] DropletTextures = ["hole_blood3.tex", "hole_blood4.tex"];

    // ---- the backstop splash (0x463B9C - 0x463D1B) ------------------------------------------

    /// <summary>How far past the wound the backstop trace reaches, in world units.</summary>
    /// <remarks>Three metres. Past that the round's splash simply lands on nothing.</remarks>
    public const float BackstopRangeInUnits = 12288f;

    /// <summary>The smallest backstop splash, in world units.</summary>
    /// <remarks>
    /// <c>0x463C98</c> draws <c>(rand + 1) × 614.4</c>, so the radius runs from fifteen to thirty
    /// centimetres — five to ten times the size of the bullet hole beside it.
    /// </remarks>
    public const float BackstopRadiusMinimumInUnits = 614.4f;

    // ---- the mist (0x463D20 - 0x463E2F) ------------------------------------------------------

    /// <summary>How many mist quads a body may show at once.</summary>
    public const int MistCapacity = 2;

    /// <summary>The mist quad's size, in world units.</summary>
    /// <remarks><c>0x4499999A</c> — a third of a metre, and it does not grow.</remarks>
    public const float MistSizeInUnits = 1228.8f;

    /// <summary>How many ticks the mist holds before it goes.</summary>
    /// <remarks>
    /// The animation's whole budget: <c>0x463D95</c> divides it by the sprite's frame count to get the
    /// ticks each frame is held, and the fade takes it out in a single tick afterwards.
    /// </remarks>
    public const int MistHoldTicks = 15;

    // ---- the droplets (0x463E3E - 0x464099) --------------------------------------------------

    /// <summary>How many droplets a body may have in the air at once.</summary>
    public const int DropletCapacity = 10;

    /// <summary>A droplet's size, in world units.</summary>
    /// <remarks><c>0x42CCCCCD</c> — two and a half centimetres.</remarks>
    public const float DropletSizeInUnits = 102.4f;

    /// <summary>What a droplet's velocity gains each tick, in world units per tick².</summary>
    /// <remarks>
    /// <c>0xC16DDF15</c> — a third of the engine's own 9.8 m/s². Blood does not fall like a body.
    /// </remarks>
    public const float DropletGravityPerTick = -14.866963f;

    /// <summary>How many ticks a droplet holds before it starts to fade.</summary>
    public const int DropletHoldTicks = 30;

    /// <summary>How many ticks a droplet's fade takes.</summary>
    public const int DropletFadeTicks = 7;

    /// <summary>How many droplets the forward spray throws, at fewest.</summary>
    public const int ForwardDropletMinimum = 3;

    /// <summary>How many whole counts the forward draw spans.</summary>
    /// <remarks>Three, so the forward spray is three, four or five droplets.</remarks>
    public const int ForwardDropletRange = 3;

    /// <summary>The forward spray's cone, in radians.</summary>
    /// <remarks><c>0x3E32B8C3</c> — ten degrees, so the droplets are drawn on ±five.</remarks>
    public const float ForwardConeRadians = 0.17453294f;

    /// <summary>The slowest a forward droplet leaves, in world units per tick.</summary>
    public const float ForwardSpeedMinimum = 379f;

    /// <summary>The fastest.</summary>
    public const float ForwardSpeedMaximum = 606f;

    /// <summary>How many droplets the back spray throws, at fewest.</summary>
    public const int BackDropletMinimum = 2;

    /// <summary>How many whole counts the back draw spans.</summary>
    /// <remarks>Three, so the back spray is two, three or four droplets.</remarks>
    public const int BackDropletRange = 3;

    /// <summary>The back spray's cone, in radians.</summary>
    /// <remarks><c>0x3F9C6C29</c> — seventy degrees, so the droplets are drawn on ±thirty-five.</remarks>
    public const float BackConeRadians = 1.2217305f;

    /// <summary>The slowest a back droplet leaves, in world units per tick.</summary>
    public const float BackSpeedMinimum = 37f;

    /// <summary>The fastest.</summary>
    public const float BackSpeedMaximum = 113f;

    // ---- the droplet's landing mark (0x4640B0) -----------------------------------------------

    /// <summary>The smallest mark a landing droplet leaves, in world units.</summary>
    /// <remarks><c>0x4641A5</c> draws <c>rand × 204.8 + 102.4</c> — two and a half to seven and a half
    /// centimetres.</remarks>
    public const float DropletMarkMinimumInUnits = 102.4f;

    /// <summary>How much the landing mark's draw spans, in world units.</summary>
    public const float DropletMarkRangeInUnits = 204.8f;

    /// <summary>Works out how large a backstop splash is.</summary>
    /// <param name="uniformSample">A uniform draw on <c>[0, 1)</c>.</param>
    /// <returns>The radius in world units.</returns>
    public static float BackstopRadius(float uniformSample) =>
        (uniformSample + 1f) * BackstopRadiusMinimumInUnits;

    /// <summary>Works out how large a landing droplet's mark is.</summary>
    /// <param name="uniformSample">A uniform draw on <c>[0, 1)</c>.</param>
    /// <returns>The radius in world units.</returns>
    public static float DropletMarkRadius(float uniformSample) =>
        (uniformSample * DropletMarkRangeInUnits) + DropletMarkMinimumInUnits;

    /// <summary>Decides whether a landed droplet marks the surface it met.</summary>
    /// <param name="from">Where the droplet's segment started, in world units.</param>
    /// <param name="to">Where it ended.</param>
    /// <param name="hit">The surface it met.</param>
    /// <returns><see langword="true"/> when the droplet marks.</returns>
    /// <remarks>
    /// <para>
    /// <c>0x464178</c> takes <c>start − end</c> — the reverse of the droplet's travel — dots it with the
    /// struck face's normal and marks only when the result is <b>negative</b>. Equivalently: the droplet
    /// marks when it is travelling <i>along</i> the face's own normal.
    /// </para>
    /// <para>
    /// <b>Which way that face's normal points is the whole of the question, and it is settled by
    /// measurement rather than by convention.</b> The original's normal is
    /// <c>cross(v1 − v0, v2 − v0)</c> (<c>0x4CD300</c> through <c>0x4B3190</c>) — bit for bit what
    /// <see cref="RayTriangle.Normal"/> computes from the same three vertices in the same order — and
    /// fanning rays into level one's geometry showed every first crossing has a <i>positive</i> facing.
    /// These meshes wind their normals INTO the solid, so travelling along the normal is travelling
    /// <b>into</b> the surface. A droplet that reaches the front of a wall splashes on it; one that
    /// caught the back of a face on its way out does not.
    /// </para>
    /// <para>
    /// <see cref="WorldRayHit.Normal"/> has already been turned to oppose the ray so that a decal sits
    /// proud of the surface, which is the opposite vector on an entry. The raw normal is recovered from
    /// <see cref="WorldRayHit.IsExit"/>, which is the sign the flip was taken from, and which the rich
    /// trace's <see cref="WorldTraceHit.RayHit"/> carries too. Every droplet that reaches a surface from
    /// the outside is an entry and marks — what the shipped <c>hole_blood3</c>/<c>4</c> textures are for.
    /// </para>
    /// </remarks>
    public static bool MarksSurface(Vector3d from, Vector3d to, in WorldRayHit hit)
    {
        // The face's own normal, before the reported one was turned to face the ray.
        Vector3f face = hit.IsExit ? hit.Normal : -hit.Normal;
        double dot = ((from.X - to.X) * face.X)
            + ((from.Y - to.Y) * face.Y)
            + ((from.Z - to.Z) * face.Z);
        return dot < 0.0;
    }

    /// <summary>How many droplets the forward spray throws.</summary>
    /// <param name="uniformSample">A uniform integer on 0 to <see cref="ForwardDropletRange"/> minus one.</param>
    /// <returns>Three, four or five.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample is outside its range.</exception>
    public static int ForwardDropletCount(int uniformSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uniformSample);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(uniformSample, ForwardDropletRange);

        return uniformSample + ForwardDropletMinimum;
    }

    /// <summary>How many droplets the back spray throws.</summary>
    /// <param name="uniformSample">A uniform integer on 0 to <see cref="BackDropletRange"/> minus one.</param>
    /// <returns>Two, three or four.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample is outside its range.</exception>
    public static int BackDropletCount(int uniformSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uniformSample);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(uniformSample, BackDropletRange);

        return uniformSample + BackDropletMinimum;
    }

    /// <summary>Builds the mist's particle system, sized for one body.</summary>
    /// <param name="frameCount">How many frames <c>blood1.spr</c> has; retail ships seven.</param>
    /// <returns>The system.</returns>
    /// <remarks>
    /// <b>The frame rate is integer division.</b> <c>0x463D95</c> computes <c>15 / frameCount</c> as
    /// whole ticks, so seven frames run at two ticks each — fourteen of the fifteen the mist holds, and
    /// the last frame is held one tick longer than the rest.
    /// </remarks>
    public static RetailParticleSystem CreateMistSystem(int frameCount) => new(
        acceleration: Vector3f.Zero,
        sizeInUnits: MistSizeInUnits,
        growthPerTick: 0f,
        frameCount: Math.Max(1, frameCount),
        ticksPerFrame: MistHoldTicks / Math.Max(1, frameCount),
        capacity: MistCapacity);

    /// <summary>Builds the droplet system, sized for one body.</summary>
    /// <returns>The system, which collides.</returns>
    public static RetailParticleSystem CreateDropletSystem() => new(
        acceleration: new Vector3f(0f, 0f, DropletGravityPerTick),
        sizeInUnits: DropletSizeInUnits,
        growthPerTick: 0f,
        frameCount: 1,
        ticksPerFrame: 0,
        capacity: DropletCapacity,
        collides: true);

    /// <summary>Draws one quad's spin, which the blood spray shares with the wall puff.</summary>
    /// <param name="uniformSample">A uniform draw on <c>[0, 1)</c>.</param>
    /// <returns>The spin, in radians per tick.</returns>
    public static float Spin(float uniformSample) => GunShotImpactEffects.Spin(uniformSample);

    /// <summary>Fills the two systems for one wound, in the original's own order.</summary>
    /// <param name="mist">The body's mist system.</param>
    /// <param name="droplets">The body's droplet system.</param>
    /// <param name="position">Where the round entered, in world units.</param>
    /// <param name="direction">The round's travel direction; need not be unit length.</param>
    /// <param name="random">The draw source.</param>
    /// <exception cref="ArgumentNullException">A system or the draw source is <see langword="null"/>.</exception>
    /// <remarks>
    /// The mist comes first and takes no draws at all; then the forward spray, whose count is drawn
    /// before any of its particles; then the back spray, whose direction is the wound's reversed. Each
    /// droplet draws its own spin before it is emitted, exactly as <c>0x463F10</c> and
    /// <c>0x464044</c> do.
    /// </remarks>
    public static void Spray(
        RetailParticleSystem mist,
        RetailParticleSystem droplets,
        Vector3d position,
        Vector3f direction,
        IgiRandom random)
        => Spray(mist, droplets, position, direction, random, random);

    /// <summary>Throws one wound's mist and droplets, the emitter drawing from its own stream as <c>0x4FE210</c> does.</summary>
    /// <param name="mist">The body's mist system.</param>
    /// <param name="droplets">The body's droplet system.</param>
    /// <param name="position">Where the round entered.</param>
    /// <param name="direction">The round's travel direction.</param>
    /// <param name="random">The level stream: the two counts and each droplet's spin.</param>
    /// <param name="emitter">The particle emitter's own stream (<c>dword_A7A4C0</c>): each droplet's direction and speed.</param>
    public static void Spray(
        RetailParticleSystem mist,
        RetailParticleSystem droplets,
        Vector3d position,
        Vector3f direction,
        IgiRandom random,
        IgiRandom emitter)
    {
        ArgumentNullException.ThrowIfNull(mist);
        ArgumentNullException.ThrowIfNull(droplets);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(emitter);

        mist.Emit(
            position,
            Vector3f.Zero,
            alpha: 1f,
            spin: 0f,
            angle: 0f,
            growth: 1f,
            holdTicks: MistHoldTicks,
            fadeTicks: 0);

        int forward = ForwardDropletCount((int)random.NextBounded(ForwardDropletRange));
        for (int i = 0; i < forward; i++)
        {
            droplets.EmitCone(
                position,
                direction,
                ForwardConeRadians,
                ForwardSpeedMinimum,
                ForwardSpeedMaximum,
                alpha: 1f,
                spin: Spin(random.NextSingle()),
                angle: 0f,
                growth: 1f,
                holdTicks: DropletHoldTicks,
                fadeTicks: DropletFadeTicks,
                emitter);
        }

        int back = BackDropletCount((int)random.NextBounded(BackDropletRange));
        for (int i = 0; i < back; i++)
        {
            droplets.EmitCone(
                position,
                -direction,
                BackConeRadians,
                BackSpeedMinimum,
                BackSpeedMaximum,
                alpha: 1f,
                spin: Spin(random.NextSingle()),
                angle: 0f,
                growth: 1f,
                holdTicks: DropletHoldTicks,
                fadeTicks: DropletFadeTicks,
                emitter);
        }
    }
}
