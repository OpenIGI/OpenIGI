using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The random stream every smoke draws from, <c>Smoke_tRandomState</c>: one for the module, set back to its seed each time a smoke is
/// made (<c>Smoke_CreateHandler</c>).
/// </summary>
public sealed class Igi2SmokeRandom
{
    /// <summary>Gets the stream.</summary>
    public IgiRandom Random { get; private set; } = Seeded();

    /// <summary>Sets the stream back to its seed, as a smoke is made.</summary>
    public void Reseed() => Random = Seeded();

    private static IgiRandom Seeded() => new(unchecked((uint)-923067897), 971667286u, 2146516173u);
}

/// <summary>
/// A plume of smoke, steam or fire, I.G.I. 2's <c>Smoke</c> (smoke.c): a particle system that sprays particles in a cone from its place,
/// at a rate its generate factor sets, and that may burn the player standing in it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Smoke_UpdateInternalDataHandler</c> makes its particle system: its sprite by index (nought the fireball, which also burns at half
/// a point a second within a metre unless told otherwise; one blood; two the second smoke; above, the first), its fade mode's
/// transparency, a fall of its gravity factor, its colour, size and size delta, and a view cutoff; its direction is the third column
/// of its alpha-and-gamma turn.
/// </para>
/// <para>
/// <c>Smoke_RunHandler</c>: the plume is moved with its source when asked; the children run; its generate factor follows its expression;
/// it does its damage while generating; then the factor is added to a count and, for each thirty in it, a particle is thrown from a
/// drawn point within its radius about the cone (the speeds passed as retail passes them, the most into the least's place), with a
/// drawn turn speed, for its life and its fade over its intensity, in ticks.
/// </para>
/// </remarks>
public sealed partial class Igi2SmokeTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Smoke";

    /// <summary>The sprites a smoke is drawn with, by its sprite index (<c>Smoke_Open</c>).</summary>
    public static readonly string[] Sprites = ["fireball.spr", "blood1.spr", "smoke2.spr", "smoke1.spr"];

    /// <summary>The transparencies of the fade modes nought to two.</summary>
    public static readonly int[] FadeModeTransparency = [0x1080000, 0x1000008, 0x1000010];

    private LevelExpression? _generate;
    private Igi2Particle2DSystem? _system;
    private Vector3d _previous;
    private bool _isPreviousValid;
    private float _count;
    private int _damageTimer;
    private Vector3f _direction;

    /// <summary>Gets or sets where it is.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its tilt.</summary>
    [ScriptParam(ScriptParamType.Angle, "Alpha")]
    public float Alpha { get; set; }

    /// <summary>Gets or sets its heading.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float Gamma { get; set; }

    /// <summary>Gets or sets the most particles at once.</summary>
    [ScriptParam(ScriptParamType.Int32, "Number of Particles")]
    public int MaxParticles { get; set; } = 100;

    /// <summary>Gets or sets how far from its place a particle may start, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Radius")]
    public float Radius { get; set; }

    /// <summary>Gets or sets the cone's half angle.</summary>
    [ScriptParam(ScriptParamType.Angle, "Maximum Random Angle")]
    public float MaxRandomAngle { get; set; } = 0.785398185f;

    /// <summary>Gets or sets the least speed, in metres a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Minimum Velocity")]
    public float MinVelocity { get; set; } = 4.5f;

    /// <summary>Gets or sets the most speed, in metres a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Maximum Velocity")]
    public float MaxVelocity { get; set; } = 6f;

    /// <summary>Gets or sets its colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Colour")]
    public Vector3f Colour { get; set; } = new(2f, 2f, 2f);

    /// <summary>Gets or sets a particle's life, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Life Time")]
    public float LifeTime { get; set; } = 0.5f;

    /// <summary>Gets or sets a particle's fade, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fade Time")]
    public float FadeTime { get; set; } = 1f;

    /// <summary>Gets or sets how its particles blend.</summary>
    [ScriptParam(ScriptParamType.Int32, "Fade Mode")]
    public int FadeMode { get; set; }

    /// <summary>Gets or sets its sprite, by index.</summary>
    [ScriptParam(ScriptParamType.Int32, "Sprite index")]
    public int Sprite { get; set; } = 3;

    /// <summary>Gets or sets a particle's size, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Particle Size")]
    public float Size { get; set; } = 0.5f;

    /// <summary>Gets or sets how fast a particle grows, a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Particle Size Delta")]
    public float SizeDelta { get; set; } = 2.6666667f;

    /// <summary>Gets or sets the least turn speed.</summary>
    [ScriptParam(ScriptParamType.Angle, "Minimum Rotation Speed")]
    public float MinRotation { get; set; }

    /// <summary>Gets or sets the most turn speed.</summary>
    [ScriptParam(ScriptParamType.Angle, "Maximum Rotation Speed")]
    public float MaxRotation { get; set; }

    /// <summary>Gets or sets how bright its particles start.</summary>
    [ScriptParam(ScriptParamType.Real32, "Intensity")]
    public float Intensity { get; set; } = 1f;

    /// <summary>Gets or sets how much its particles fall.</summary>
    [ScriptParam(ScriptParamType.Real32, "Gravity factor")]
    public float Gravity { get; set; }

    /// <summary>Gets or sets how many particles it makes a second, before its expression says otherwise.</summary>
    [ScriptParam(ScriptParamType.Real64, "Initial generate factor value")]
    public double GenerateFactor { get; set; } = 30.0;

    /// <summary>Gets or sets the expression its generate factor follows.</summary>
    [ScriptParam(ScriptParamType.VarString, "Generate factor")]
    public string GenerateExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether its plume moves with it.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Move Particles")]
    public bool MoveParticles { get; set; }

    /// <summary>Gets or sets how far off it is drawn, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "View cutoff")]
    public float ViewCutoff { get; set; } = 100f;

    /// <summary>Gets or sets a value indicating whether it burns.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Do Damage")]
    public bool DoDamage { get; set; }

    /// <summary>Gets or sets the damage it does a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damage Per Sec")]
    public float DamagePerSecond { get; set; }

    /// <summary>Gets or sets how far it burns, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damage Radius (m)")]
    public float DamageRadius { get; set; }

    /// <summary>Gets the particle system, once made.</summary>
    public Igi2Particle2DSystem? System => _system;

    /// <summary>Gets or sets the particle module its system is made in.</summary>
    public Igi2Particles? Particles { get; set; }

    /// <summary>Gets or sets the module's random stream.</summary>
    public Igi2SmokeRandom? Stream { get; set; }

    /// <summary>Gets or sets the player, whom it burns.</summary>
    public Igi2HumanPlayer? Player { get; set; }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindReal(this, "vGenerateFactor", () => GenerateFactor, value => GenerateFactor = value);
    }

    /// <summary>Makes its particle system, as its create and update-internal-data handlers do.</summary>
    public void Prepare()
    {
        Stream?.Reseed();
        float[] turn = Igi2Pose.Rotation(Alpha, 0f, Gamma);
        _direction = new Vector3f(turn[2], turn[5], turn[8]);
        if (Sprite == 0 && !DoDamage)
        {
            DoDamage = true;
            DamagePerSecond = 0.5f;
            DamageRadius = 1f;
        }

        if (Particles is not { } particles)
        {
            return;
        }

        float size = ((Fpu)Size * 4096.0).F;
        _system = particles.New(
            Sprites[Math.Clamp(Sprite, 0, Sprites.Length - 1)],
            animated: true,
            frames: 0,
            acceleration: new Vector3f(0f, 0f, ((Fpu)Gravity * -204.8000030517578).F),
            color: new Vector3f(((Fpu)Colour.X * 63.0).F, ((Fpu)Colour.Y * 63.0).F, ((Fpu)Colour.Z * 63.0).F),
            width: size,
            height: size,
            range: 409600f,
            transparency: (uint)FadeMode < (uint)FadeModeTransparency.Length ? FadeModeTransparency[FadeMode] : FadeModeTransparency[0],
            animationDelta: 0,
            sizeDelta: ((Fpu)SizeDelta * 0.03333333507180214).F,
            shininess: 0f,
            maxParticles: MaxParticles,
            onCollision: null);
        _system.Range = ((Fpu)ViewCutoff * 4096.0).F;
        // smoke.c Smoke_UpdateInternalDataHandler: the plume owns its system even between emissions.
        _system.IsSelfDestruct = false;
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (_system is not { } system)
        {
            return;
        }

        if (_isPreviousValid && MoveParticles)
        {
            system.MoveParticles(new Vector3d(Position.X - _previous.X, Position.Y - _previous.Y, Position.Z - _previous.Z));
        }

        _previous = Position;
        _isPreviousValid = true;
        level.RunChildren(this);

        _generate ??= Igi2LevelExpressions.Create(GenerateExpression, Name, level.Natives);
        if (!_generate.IsEmpty)
        {
            GenerateFactor = level.Evaluate(_generate);
        }

        if (GenerateFactor > 0.0 && DoDamage)
        {
            Burn();
        }

        Emit();
    }

    /// <summary>Emits a tick of particles, <c>smoke.c Smoke_RunHandler</c>, including dynamically created grenade smoke.</summary>
    public void Emit()
    {
        if (_system is not { } system)
        {
            return;
        }

        _count = ((Fpu)GenerateFactor + _count).F;
        IgiRandom random = Stream?.Random ?? new IgiRandom();
        while (_count > 30f)
        {
            Fpu angle = (Fpu)(double)(ushort)random.NextRaw() * 0.0000152587890625 * 6.2831854820251465;
            Fpu reach = (Fpu)(double)(ushort)random.NextRaw() * 0.0000152587890625 * Radius * 4096.0;
            Fpu spin = (((Fpu)(double)(ushort)random.NextRaw() * 0.0000152587890625 * ((Fpu)MaxRotation - MinRotation)) + MinRotation)
                * 0.03333333507180214;
            Vector3d at = new(
                ((Fpu)Math.Cos(angle.Value) * reach + Position.X).Value,
                ((Fpu)Math.Sin(angle.Value) * reach + Position.Y).Value,
                Position.Z);
            system.AddConeParticle(
                at,
                _direction,
                MaxRandomAngle,
                ((Fpu)MaxVelocity * 136.0).F,
                ((Fpu)MinVelocity * 136.0).F,
                Intensity,
                spin.F,
                0f,
                1f,
                Fpu.ToInt32((Fpu)LifeTime * 30.0),
                Fpu.ToInt32((Fpu)FadeTime / Intensity * 30.0));
            _count = ((Fpu)_count - 30.0).F;
        }
    }

    /// <summary>Burns the player standing within its radius, once a second, at the head (<c>Smoke_DoDamage</c>, <c>HumanPlayer_SmokeDamageHandler</c>).</summary>
    private void Burn()
    {
        if (_damageTimer != 0)
        {
            _damageTimer--;
            return;
        }

        _damageTimer = 30;
        if (Player is not { } player)
        {
            return;
        }

        Vector3d p = player.Position;
        float dx = ((Fpu)p.X - Position.X).F, dy = ((Fpu)p.Y - Position.Y).F;
        Fpu dz = (Fpu)p.Z - Position.Z;
        if (Fpu.Sqrt(((Fpu)dx * dx) + ((Fpu)dy * dy) + (dz * dz)).Value <= ((Fpu)DamageRadius * 4096.0).F)
        {
            player.HandleDamage(Igi2DamagePart.Head, DamagePerSecond, default);
        }
    }
}
