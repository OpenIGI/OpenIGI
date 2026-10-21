using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The engine's sine table, <c>Real32_avSin</c>: 4096 steps to a turn, each the sine at its step and the rise to the next, read between
/// steps by the rise.
/// </summary>
public static class Igi2SineTable
{
    /// <summary>The steps to a turn.</summary>
    public const int Steps = 4096;

    /// <summary>A step, in radians, as the original's single-precision constant.</summary>
    public const double Step = 0.0015339808305725455;

    private static readonly float[] Sines = Build();

    /// <summary>Reads the table at a step and a fraction of the next, <c>frac × rise + sine</c>.</summary>
    /// <param name="steps">The angle, in steps.</param>
    /// <returns>The sine.</returns>
    public static Fpu At(Fpu steps)
    {
        int whole = Fpu.ToInt32(steps);
        int index = whole & (Steps - 1);
        float rise = Sines[(index + 1) & (Steps - 1)] - Sines[index];
        return ((steps - whole) * rise) + Sines[index];
    }

    /// <summary>
    /// Reads the table at two angles and sums them as the original does: both fractions' rises first, then the two sines in the order
    /// given.
    /// </summary>
    /// <param name="first">The angle whose rise comes first, in steps.</param>
    /// <param name="second">The angle whose rise comes second, in steps.</param>
    /// <param name="secondSineFirst">Whether the second angle's sine is added before the first's.</param>
    /// <returns>The sum of the two sines.</returns>
    public static Fpu Sum(Fpu first, Fpu second, bool secondSineFirst = false)
    {
        int i = Fpu.ToInt32(first), j = Fpu.ToInt32(second);
        Fpu rises = ((first - i) * Rise(i)) + ((second - j) * Rise(j));
        return secondSineFirst ? rises + Sine(j) + Sine(i) : rises + Sine(i) + Sine(j);
    }

    /// <summary>Gets the sine at a step.</summary>
    /// <param name="index">The step, nought to 4095.</param>
    /// <returns>Its sine.</returns>
    public static float Sine(int index) => Sines[index & (Steps - 1)];

    /// <summary>Gets the rise from a step to the next.</summary>
    /// <param name="index">The step, nought to 4095.</param>
    /// <returns>The rise.</returns>
    public static float Rise(int index) => Sines[(index + 1) & (Steps - 1)] - Sines[index & (Steps - 1)];

    private static float[] Build()
    {
        float[] sines = new float[Steps];
        for (int i = 0; i < Steps; i++)
        {
            sines[i] = (float)Math.Sin(i * Step);
        }

        return sines;
    }
}

/// <summary>The stream the flocks draw from, <c>Flock_tRandomState</c>, seeded once as the game starts (<c>BirdFlock_Open</c>).</summary>
public sealed class Igi2FlockRandom
{
    /// <summary>Gets the stream.</summary>
    public IgiRandom Random { get; } = new(0xF276CD58u, 0x2599F756u, 0x64427ECDu);
}

/// <summary>One bird of a flock: where it is against the flock, the way it flies, and its three swings, each a count and a period.</summary>
public sealed class Igi2Bird
{
    /// <summary>Gets where it is against the flock's position.</summary>
    public Vector3f Position { get; internal set; }

    /// <summary>Gets the unit way it last moved.</summary>
    public Vector3f Direction { get; internal set; }

    /// <summary>Gets its swings' counts.</summary>
    public int[] Counts { get; } = new int[3];

    /// <summary>Gets its swings' periods, in ticks.</summary>
    public int[] Periods { get; } = new int[3];
}

/// <summary>
/// A flock of birds, I.G.I. 2's <c>BirdFlock</c> (birdflock.c): up to a hundred birds wheeling about a point, each drawn as two flapping
/// wings, while the camera is within its cutoff distance.
/// </summary>
/// <remarks>
/// <para>
/// <c>BirdFlock_UpdateInternalDataHandler</c>: at most a hundred; each bird's swings start at nought, each period forty to 1240 ticks.
/// </para>
/// <para>
/// <c>BirdFlock_RunHandler</c>, while seen: each swing counts on, and past its period starts again with a period of twenty to sixty
/// seconds times a fiftieth of its radius. A bird stands at the sum of the sines of its first two swings times the radius across, of its
/// last two up the other way, and of its first and last times ten metres up, each swing a turn a period, half a turn on; it flies the
/// way it moved.
/// </para>
/// <para>
/// <c>BirdFlock_EnterHandler</c>, each frame: within its cutoff distance of the camera it is seen, and each bird is drawn as four
/// triangles, wings a metre to either side of a body 0.4 m long, its wing tips flapping 0.8 m fifteen times a period of its second swing;
/// beyond, it is not seen and stands still.
/// </para>
/// </remarks>
public sealed partial class Igi2BirdFlockTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "BirdFlock";

    /// <summary>The most birds a flock has.</summary>
    public const int MaximumBirds = 100;

    private Igi2Bird[] _birds = [];
    private bool _isSeen = true;

    /// <summary>Gets or sets where it wheels about.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets how far its birds wheel, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Radius (meter)")]
    public float Radius { get; set; } = 100f;

    /// <summary>Gets or sets how far the camera may be and see it, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Cutoff Distance (meter)")]
    public float CutoffDistance { get; set; } = 1000f;

    /// <summary>Gets or sets how many birds it has.</summary>
    [ScriptParam(ScriptParamType.Int32, "Individuals (max 100)")]
    public int Individuals { get; set; } = 10;

    /// <summary>Gets its birds.</summary>
    public IReadOnlyList<Igi2Bird> Birds => _birds;

    /// <summary>Gets a value indicating whether the camera was within its cutoff distance at the last frame, so that it flies.</summary>
    public bool IsSeen => _isSeen;

    /// <summary>Gets or sets the stream its birds draw from.</summary>
    public Igi2FlockRandom? Random { get; set; }

    /// <summary>Holds its birds to a hundred and starts their swings, <c>BirdFlock_UpdateInternalDataHandler</c>.</summary>
    /// <param name="random">The stream the flocks draw from.</param>
    public void Prepare(Igi2FlockRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Random = random;
        Individuals = Math.Min(Individuals, MaximumBirds);
        _birds = new Igi2Bird[Math.Max(Individuals, 0)];
        for (int i = 0; i < _birds.Length; i++)
        {
            Igi2Bird bird = new();
            for (int swing = 0; swing < 3; swing++)
            {
                bird.Periods[swing] = Fpu.ToInt32(((Fpu)(double)(ushort)random.Random.NextRaw() * IgiRandom.Scale * 1200.0) + 40.0);
            }

            _birds[i] = bird;
        }
    }

    /// <summary>Tells it how far it projects from the camera this frame, <c>BirdFlock_EnterHandler</c>.</summary>
    /// <param name="depth">Its depth, in world units.</param>
    public void Project(float depth) => _isSeen = ((Fpu)CutoffDistance * 4096.0).F >= depth;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!_isSeen || Random is not { } random)
        {
            return;
        }

        Fpu radius = (Fpu)Radius * 4096.0;
        Fpu radiusScale = (Fpu)Radius * 0.019999999552965164;
        foreach (Igi2Bird bird in _birds)
        {
            for (int swing = 0; swing < 3; swing++)
            {
                if (++bird.Counts[swing] > bird.Periods[swing])
                {
                    bird.Counts[swing] = 0;
                    Fpu seconds = ((Fpu)(double)(ushort)random.Random.NextRaw() * IgiRandom.Scale * 40.0) + 20.0;
                    bird.Periods[swing] = Fpu.ToInt32(seconds * radiusScale * 30.0);
                }
            }

            Fpu a = Steps(bird, 0), b = Steps(bird, 1), c = Steps(bird, 2);
            Vector3f at = new(
                (Igi2SineTable.Sum(a, b) * radius).F,
                (Igi2SineTable.Sum(b, c) * radius).F,
                (Igi2SineTable.Sum(a, c, secondSineFirst: true) * 40960.0).F);
            Vector3f moved = new(at.X - bird.Position.X, at.Y - bird.Position.Y, at.Z - bird.Position.Z);
            if (moved.X != 0f || moved.Y != 0f || moved.Z != 0f)
            {
                Fpu scale = (Fpu)1.0 / Fpu.Sqrt(((Fpu)moved.X * moved.X) + ((Fpu)moved.Y * moved.Y) + ((Fpu)moved.Z * moved.Z));
                moved = new Vector3f((scale * moved.X).F, (scale * moved.Y).F, (scale * moved.Z).F);
            }

            bird.Direction = moved;
            bird.Position = at;
        }
    }

    /// <summary>Builds its birds' wings, <c>BirdFlock_EnterHandler</c>: four triangles a bird, against its position.</summary>
    /// <returns>The triangles' corners, three to a triangle.</returns>
    public IReadOnlyList<Vector3f> Wings()
    {
        List<Vector3f> corners = new(_birds.Length * 12);
        foreach (Igi2Bird bird in _birds)
        {
            Vector3f d = bird.Direction, p = bird.Position;
            Vector3f back = new(((Fpu)d.X * -819.2000122070312).F, ((Fpu)d.Y * -819.2000122070312).F, ((Fpu)d.Z * -819.2000122070312).F);
            float sideX = d.Y, sideY = -d.X;
            if (sideX != 0f || sideY != 0f)
            {
                Fpu scale = (Fpu)1.0 / Fpu.Sqrt(((Fpu)sideX * sideX) + ((Fpu)sideY * sideY));
                (sideX, sideY) = ((scale * d.Y).F, (scale * sideY).F);
            }

            Vector3f side = new(((Fpu)sideX * 4096.0).F, ((Fpu)sideY * 4096.0).F, 0f);
            Vector3f nose = Add(p, back), tail = Sub(p, back);
            Vector3f right = Add(nose, side), left = Sub(nose, side);
            Vector3f rightTip = Add(right, back), leftTip = Add(left, back);
            float flap = (Igi2SineTable.At((Fpu)bird.Counts[1] / bird.Periods[1] * 61440.0) * 3276.800048828125).F;
            right = Flapped(right, flap);
            left = Flapped(left, flap);
            rightTip = Flapped(rightTip, flap);
            leftTip = Flapped(leftTip, flap);
            corners.AddRange([nose, tail, left, tail, nose, right, nose, rightTip, right, nose, left, leftTip]);
        }

        return corners;
    }

    // A swing as an angle in table steps: a turn a period, half a turn on.
    private static Fpu Steps(Igi2Bird bird, int swing) =>
        (((Fpu)bird.Counts[swing] / bird.Periods[swing]) + 3.1415927410125732) * 4096.0;

    private static Vector3f Flapped(Vector3f tip, float flap) => new(tip.X, tip.Y, ((Fpu)flap + tip.Z).F);

    private static Vector3f Add(Vector3f a, Vector3f b) => new(((Fpu)a.X + b.X).F, ((Fpu)a.Y + b.Y).F, ((Fpu)a.Z + b.Z).F);

    private static Vector3f Sub(Vector3f a, Vector3f b) => new(((Fpu)a.X - b.X).F, ((Fpu)a.Y - b.Y).F, ((Fpu)a.Z - b.Z).F);
}
