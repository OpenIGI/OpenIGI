using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One placed <c>AmbientArea</c> — a box that beds a sound in while the listener is inside it.</summary>
/// <param name="Task">The task instance.</param>
/// <param name="Position">The box centre, in engine units.</param>
/// <param name="Orientation">The box's orientation.</param>
/// <param name="HalfExtentsMetres">Half the box's size along each local axis, in metres.</param>
/// <param name="MinDelaySeconds">The shortest gap before the bed may start again.</param>
/// <param name="RandomWaitSeconds">How much random gap is added on top.</param>
/// <param name="Sounds">The sound definitions it may play, one drawn per start.</param>
public sealed record PlacedAmbientArea(
    GameTask Task,
    Vector3d Position,
    Matrix3f Orientation,
    Vector3d HalfExtentsMetres,
    double MinDelaySeconds,
    double RandomWaitSeconds,
    IReadOnlyList<string> Sounds)
{
    /// <summary>Gets how far the bed has faded in, 0 to 1.</summary>
    /// <remarks>
    /// Stepped by <see cref="AmbientAreaRegistry.FadeStepPerTick"/> each tick and clamped, so crossing
    /// the boundary takes twenty ticks either way rather than switching the bed on at the doorway.
    /// </remarks>
    public double Fade { get; internal set; }

    /// <summary>Gets whether the listener is inside the box this tick.</summary>
    public bool Inside { get; internal set; }

    /// <summary>Gets the ticks left before the bed may start again.</summary>
    public int Countdown { get; internal set; }

    /// <summary>Gets the sound the area wants started this tick, or an empty string.</summary>
    /// <remarks>Consumed by whoever plays it; cleared on the next tick.</remarks>
    public string PendingSound { get; internal set; } = string.Empty;

    /// <summary>Gets whether whatever is playing for this area should be stopped.</summary>
    public bool WantsStop { get; internal set; }

    /// <summary>Gets or sets whether a voice is currently running for this area.</summary>
    public bool IsSounding { get; set; }
}

/// <summary>
/// The level's authored ambience: the <c>AmbientArea</c> boxes that bed a sound in around the player.
/// </summary>
/// <remarks>
/// <para>
/// The task is 528 bytes, registered at <c>0x44C130</c> with six parameters, and its update
/// <c>0x44C370</c> is the whole behaviour. Mission 1 places beds like <c>machinery room 02</c> and
/// <c>wind behind</c>, each naming a sound definition.
/// </para>
/// <para>
/// <b>The bed plays at the LISTENER, not at the box.</b> The original sets the sound's position to the
/// listener's every tick, so an area is a non-directional wash rather than a point source — which is
/// what an ambience is. Its gain is the fade.
/// </para>
/// </remarks>
public sealed class AmbientAreaRegistry
{
    /// <summary>How much the fade moves each tick, in or out.</summary>
    /// <remarks>0.05, so a full fade takes twenty ticks — two thirds of a second at 30 Hz.</remarks>
    public const double FadeStepPerTick = 0.05;

    /// <summary>Engine units in a metre; the box's size is authored in metres and compared in units.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>The task type the level names.</summary>
    public const string TaskTypeName = "AmbientArea";

    private readonly List<PlacedAmbientArea> _areas = [];
    private readonly IgiRandom _random;

    /// <summary>Initializes a new instance of the <see cref="AmbientAreaRegistry"/> class.</summary>
    /// <param name="random">The generator the retrigger gap and the sound choice are drawn from.</param>
    public AmbientAreaRegistry(IgiRandom? random = null) => _random = random ?? new IgiRandom();

    /// <summary>Gets the level's ambient areas.</summary>
    public IReadOnlyList<PlacedAmbientArea> Areas => _areas;

    /// <summary>Collects every <c>AmbientArea</c> task in a level.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <param name="random">The generator to draw with.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static AmbientAreaRegistry Build(GameTask root, IgiRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        AmbientAreaRegistry registry = new(random);
        Collect(root, registry);
        return registry;
    }

    /// <summary>Advances every area one tick against where the listener is.</summary>
    /// <param name="listenerInUnits">Where the ear is, in engine units.</param>
    public void Tick(Vector3d listenerInUnits)
    {
        foreach (PlacedAmbientArea area in _areas)
        {
            area.PendingSound = string.Empty;
            area.WantsStop = false;

            bool inside = Contains(area, listenerInUnits);
            area.Inside = inside;

            // The fade is a ramp, not a switch: 0.05 a tick each way, clamped.
            area.Fade = Math.Clamp(area.Fade + (inside ? FadeStepPerTick : -FadeStepPerTick), 0.0, 1.0);

            if (area.Fade == 0.0)
            {
                // Silent: rearm the gap and drop whatever was playing. The gap is drawn fresh every
                // time the bed goes quiet, which is what stops several areas falling into step.
                area.Countdown = (int)((_random.NextDouble() * area.RandomWaitSeconds) + area.MinDelaySeconds) * 30;
                area.WantsStop = area.IsSounding;
            }
            else if (!area.IsSounding && inside && area.Countdown == 0 && area.Sounds.Count > 0)
            {
                area.PendingSound = area.Sounds[(int)(_random.NextDouble() * area.Sounds.Count) % area.Sounds.Count];
            }

            if (area.Countdown > 0)
            {
                area.Countdown--;
            }
        }
    }

    /// <summary>Whether a point is inside an area's box.</summary>
    /// <param name="area">The area.</param>
    /// <param name="pointInUnits">The point, in engine units.</param>
    /// <returns><see langword="true"/> when the point is within the box on all three local axes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="area"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The offset is taken into the box's own frame with the engine's <c>v · M</c> convention — columns
    /// dotted with the vector, which is what the original's three dot products do — and compared against
    /// the half-extents scaled to engine units. The bounds are strict at both ends.
    /// </remarks>
    public static bool Contains(PlacedAmbientArea area, Vector3d pointInUnits)
    {
        ArgumentNullException.ThrowIfNull(area);

        Vector3d offset = new(
            pointInUnits.X - area.Position.X,
            pointInUnits.Y - area.Position.Y,
            pointInUnits.Z - area.Position.Z);

        Vector3d local = area.Orientation.Transform(offset);

        return Within(local.X, area.HalfExtentsMetres.X)
            && Within(local.Y, area.HalfExtentsMetres.Y)
            && Within(local.Z, area.HalfExtentsMetres.Z);

        static bool Within(double value, double halfExtentMetres)
        {
            double limit = halfExtentMetres * UnitsPerMetre;
            return value > -limit && value < limit;
        }
    }

    private static void Collect(GameTask task, AmbientAreaRegistry registry)
    {
        if (task is DeclaredTask declared
            && string.Equals(task.Type.Name, TaskTypeName, StringComparison.Ordinal))
        {
            string sound = declared.TryGetValue("SoundDef", out object? raw) && raw is string name
                ? name
                : string.Empty;

            registry._areas.Add(new PlacedAmbientArea(
                declared,
                ReadPosition(declared),
                ReadOrientation(declared),
                ReadSize(declared),
                ReadReal(declared, "Min Delay"),
                ReadReal(declared, "Random wait"),
                sound.Length > 0 ? [sound] : []));
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry);
        }
    }

    private static Vector3d ReadPosition(DeclaredTask declared) =>
        declared.TryGetValue("Position", out object? value) && value is Vector3d position ? position : default;

    private static Matrix3f ReadOrientation(DeclaredTask declared) =>
        declared.TryGetValue("Orientation", out object? value)
            ? value switch
            {
                Vector3f angles => WorldScene.ToOrientation(angles),
                float gamma => Matrix3f.RotationZ(gamma),
                _ => Matrix3f.Identity,
            }
            : Matrix3f.Identity;

    private static Vector3d ReadSize(DeclaredTask declared) =>
        declared.TryGetValue("Size", out object? value) && value is Vector3d size ? size : default;

    private static double ReadReal(DeclaredTask declared, string parameter) =>
        declared.TryGetValue(parameter, out object? value)
            ? value switch
            {
                double real => real,
                float single => single,
                _ => 0.0,
            }
            : 0.0;
}
