using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Particles;

namespace OpenIGI.Game.Igi1.World;

/// <summary>One of Project I.G.I.'s <c>Smoke</c> tasks: a plume of particles off a disc, a chimney's or a burning wreck's.</summary>
public sealed class Igi1SmokePlume
{
    private readonly SmokePlumeEmitter<X87> _emitter = new();
    private Vector3d _lastPosition;
    private bool _moved;

    internal Igi1SmokePlume(DeclaredTask task)
    {
        Task = task;
        Position = Vector(task, "Position");
        float pitch = Real(task, "Alpha", 0f);
        float yaw = Real(task, "Gamma", 0f);

        // 0x438000: the axis is the third column of a turn about Z by the yaw, then about X by the pitch.
        Matrix3f turned = Matrix3f.RotationZ(yaw) * Matrix3f.RotationX(pitch);
        Direction = new Vector3f(turned.M13, turned.M23, turned.M33);
        MaxParticles = (int)Real(task, "Number of Particles", 100f);
        Radius = Real(task, "Radius", 0f);
        MaxRandomAngle = Real(task, "Maximum Random Angle", 0.785398185f);
        MinVelocity = Real(task, "Minimum Velocity", 4.5f);
        MaxVelocity = Real(task, "Maximum Velocity", 6f);
        Colour = task.TryGetValue("Colour", out object? colour) && colour is Vector3f c ? c : new Vector3f(2f, 2f, 2f);
        LifeTime = Real(task, "Life Time", 0.5f);
        FadeTime = Real(task, "Fade Time", 1f);
        FadeMode = (int)Real(task, "Fade Mode", 0f);
        Sprite = (int)Real(task, "Sprite index", 3f);
        Size = Real(task, "Particle Size", 0.5f);
        SizeDelta = Real(task, "Particle Size Delta", 2.6666667f);
        MinRotation = Real(task, "Minimum Rotation Speed", 0f);
        MaxRotation = Real(task, "Maximum Rotation Speed", 0f);
        Intensity = Real(task, "Intensity", 1f);
        GenerateFactor = Real(task, "Initial generate factor value", 30f);
        GenerateExpression = task.TryGetValue("Generate factor", out object? expression) && expression is string text ? text : string.Empty;
        MovesParticles = task.TryGetValue("Move Particles", out object? move) && move is true;
    }

    /// <summary>Gets the task.</summary>
    public DeclaredTask Task { get; }

    /// <summary>Gets the disc's centre, world units.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets the plume's axis.</summary>
    public Vector3f Direction { get; }

    /// <summary>Gets the most particles it holds.</summary>
    public int MaxParticles { get; }

    /// <summary>Gets the disc's radius, metres.</summary>
    public float Radius { get; }

    /// <summary>Gets the cone's angle about the axis.</summary>
    public float MaxRandomAngle { get; }

    /// <summary>Gets the slowest a particle leaves.</summary>
    public float MinVelocity { get; }

    /// <summary>Gets the fastest.</summary>
    public float MaxVelocity { get; }

    /// <summary>Gets its colour, before <see cref="SmokePlume.ColourScale"/>.</summary>
    public Vector3f Colour { get; }

    /// <summary>Gets the seconds a particle lives before it fades.</summary>
    public float LifeTime { get; }

    /// <summary>Gets the seconds it fades over, at intensity one.</summary>
    public float FadeTime { get; }

    /// <summary>Gets its fade mode, which picks its transparency.</summary>
    public int FadeMode { get; }

    /// <summary>Gets its sprite index.</summary>
    public int Sprite { get; }

    /// <summary>Gets a particle's size, metres.</summary>
    public float Size { get; }

    /// <summary>Gets how much a particle grows a second.</summary>
    public float SizeDelta { get; }

    /// <summary>Gets the slowest spin.</summary>
    public float MinRotation { get; }

    /// <summary>Gets the fastest spin.</summary>
    public float MaxRotation { get; }

    /// <summary>Gets a particle's intensity.</summary>
    public float Intensity { get; }

    /// <summary>Gets or sets its generate factor, <c>vGenerateFactor</c>: thirty a tick makes a particle a tick.</summary>
    public double GenerateFactor { get; set; }

    /// <summary>Gets the expression its generate factor follows, when it has one.</summary>
    public string GenerateExpression { get; }

    /// <summary>Gets whether its particles move with it, "Move Particles".</summary>
    public bool MovesParticles { get; }

    /// <summary>Gets its particle system, once made.</summary>
    public Particle2DSystem? System { get; private set; }

    /// <summary>Makes its system in a particle module, as <c>0x438000</c> does when it is set up.</summary>
    /// <param name="module">The module.</param>
    public void Bind(Particle2DModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        string sprite = SmokePlume.SpriteFor(Sprite);
        float size = ((X87)Size * 4096.0).F;
        Particle2DLook look = new(
            sprite,
            IsAnimated: false,
            Frames: module.FramesOf(sprite),
            AnimationDelta: 0,
            new Vector3f(
                ((X87)Colour.X * SmokePlume.ColourScale).F,
                ((X87)Colour.Y * SmokePlume.ColourScale).F,
                ((X87)Colour.Z * SmokePlume.ColourScale).F),
            size,
            size,
            SmokePlume.Range,
            SmokePlume.TransparencyFor(FadeMode) | Particle2DFlags.DepthTested);
        System = module.New(look, Vector3f.Zero, ((X87)SizeDelta * 0.033333335f).F, MaxParticles, collider: null);

        // Particle2D_SetAutoDelete(ps, 0): the plume keeps its system while it has nothing in the air.
        System.IsSelfDestruct = false;
        _moved = false;
    }

    /// <summary>Runs it a tick, <c>Smoke_Run</c> (<c>0x437DB0</c>): its expression's generate factor, and the particles it makes.</summary>
    /// <param name="random">The plumes' stream.</param>
    /// <param name="expressions">What its expression is read through.</param>
    public void Run(IgiRandom random, LevelExpressionRuntime? expressions)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (System is not { } system)
        {
            return;
        }

        if (_moved && MovesParticles)
        {
            system.MoveParticles(Position - _lastPosition);
        }

        _moved = true;
        _lastPosition = Position;
        if (GenerateExpression.Length != 0 && expressions?.TryEvaluateReal(GenerateExpression, Task, out double factor) == true)
        {
            GenerateFactor = factor;
        }

        // 0x437DB0 rounds its ticks toward zero, the game's FPU chopping.
        _emitter.Emit(
            system,
            random,
            GenerateFactor,
            Position,
            Radius,
            Direction,
            MaxRandomAngle,
            MinVelocity,
            MaxVelocity,
            Intensity,
            MinRotation,
            MaxRotation,
            (int)((X87)LifeTime * 30.0).Value,
            (int)((X87)FadeTime * 30.0 / Intensity).Value);
    }

    private static Vector3d Vector(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3d v ? v : default;

    private static float Real(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value)
            ? value switch
            {
                float f => f,
                double d => (float)d,
                int i => i,
                short s => s,
                _ => fallback,
            }
            : fallback;
}

/// <summary>A level's <c>Smoke</c> tasks (<c>0x437510</c>), which every tick add their particles to the level's own.</summary>
public sealed class Igi1SmokePlumes
{
    private readonly List<Igi1SmokePlume> _plumes = [];
    private Particle2DModule? _module;

    /// <summary>Gets the plumes, in the level's order.</summary>
    public IReadOnlyList<Igi1SmokePlume> Plumes => _plumes;

    /// <summary>Gets the plumes' stream, <c>gSmokeRandom</c>, seeded as each plume is made (<c>0x4B4720</c>).</summary>
    public IgiRandom Random { get; } = IgiRandom.Seeded();

    /// <summary>Gets or sets what their expressions are read through.</summary>
    public LevelExpressionRuntime? Expressions { get; set; }

    /// <summary>Reads a level's plumes and exports each one's generate factor to its scripts.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <param name="variables">The level's script variables, or <see langword="null"/>.</param>
    /// <returns>The plumes.</returns>
    public static Igi1SmokePlumes Build(GameTask root, ScriptVarRegistry? variables)
    {
        ArgumentNullException.ThrowIfNull(root);
        Igi1SmokePlumes plumes = new();
        foreach (GameTask task in root.Descendants())
        {
            if (task is DeclaredTask declared && string.Equals(declared.Type.Name, "Smoke", StringComparison.Ordinal))
            {
                Igi1SmokePlume plume = new(declared);
                plumes._plumes.Add(plume);

                // 0x437CA0: Smoke_N.vGenerateFactor is the plume's own rate, which a level's script may turn up or down.
                variables?.BindReal(declared, "vGenerateFactor", () => plume.GenerateFactor, value => plume.GenerateFactor = value);
            }
        }

        return plumes;
    }

    /// <summary>Gives the plumes the level's particle module, which their systems are made in.</summary>
    /// <param name="module">The module.</param>
    /// <remarks>
    /// The systems are made on the first tick, and made again whenever the module has been cleared under them -- the
    /// mission's effects are reset after the level is set up, and the sprites' frames are known only then.
    /// </remarks>
    public void Bind(Particle2DModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _module = module;
        Random.Seed(unchecked((uint)-923067897), 971667286u, 2146516173u);
    }

    /// <summary>Runs every plume in the world a tick.</summary>
    public void Tick()
    {
        if (_module is not { } module)
        {
            return;
        }

        foreach (Igi1SmokePlume plume in _plumes)
        {
            if (plume.System is not { } system || !module.Systems.Contains(system))
            {
                plume.Bind(module);
            }

            if (ConditionalContainerGate.IsTaskActive(plume.Task))
            {
                plume.Run(Random, Expressions);
            }
        }
    }
}
