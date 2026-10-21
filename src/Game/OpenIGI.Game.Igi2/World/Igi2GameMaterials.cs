using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// What a gunshot effect leaves where a bullet goes into or out of a surface, <c>GunShotEffectParameters_t</c>, as
/// <c>GunShotEffect_ParseEffectParametersHandler</c> (<c>igi2 0x520A40</c>) reads it from a material's definition.
/// </summary>
/// <param name="IsSmoke">Whether a puff of smoke rises, <c>isSmoke</c>.</param>
/// <param name="IsParticle">Whether chips fly, <c>isParticle</c>.</param>
/// <param name="PenetratingBulletHole">The hole a bullet that goes on leaves, <c>zPenetratingBulletHole</c>; empty for none.</param>
/// <param name="NonPenetratingBulletHole">The hole a bullet that stops leaves, <c>zNonPenetratingBulletHole</c>; empty for none.</param>
/// <param name="BulletHoleSize">The hole's size in world units, <c>vBulletHoleSize</c>: the script's metres times 4096.</param>
/// <param name="Model">The model the effect places, <c>zModelName</c>; empty for none.</param>
public sealed record Igi2GunShotEffectParameters(
    bool IsSmoke,
    bool IsParticle,
    string PenetratingBulletHole,
    string NonPenetratingBulletHole,
    float BulletHoleSize,
    string Model);

/// <summary>An effect a material names for a bullet going into or out of it: its type and, for a gunshot effect, what it leaves.</summary>
/// <param name="TaskType">The effect's task type, <c>eBulletEnterEffectQTaskType</c> or <c>eBulletLeaveEffectQTaskType</c>.</param>
/// <param name="Parameters">What a <c>GunShotEffect</c> leaves; <see langword="null"/> for a type that reads none.</param>
public sealed record Igi2MaterialEffect(int TaskType, Igi2GunShotEffectParameters? Parameters);

/// <summary>
/// The half of a material that draws and that sight passes through, <c>QMaterial_t</c> (the first 32 bytes of
/// <c>GameMaterial_s</c>) as <c>GameMaterial_DefineQMaterialCB</c> (<c>igi2 0x432DC0</c>) fills it.
/// </summary>
/// <param name="Transparency">How much of a line of sight passes a face of it, <c>vTransparency</c>: none at zero.</param>
/// <param name="Density">Its density, <c>vDensity</c>.</param>
/// <param name="Friction">Its friction, <c>vFriction</c>.</param>
/// <param name="Color">Its colour, <c>tColor</c>: each channel the script's whole number times <c>2^-20</c>.</param>
public readonly record struct Igi2QMaterial(float Transparency, float Density, float Friction, Vector3f Color);

/// <summary>One surface the world is made of, <c>GameMaterial_s</c> as <c>GameMaterial_DefineGameMaterialCB</c> (<c>igi2 0x432FF0</c>) fills it.</summary>
public sealed class Igi2GameMaterial
{
    /// <summary>Gets the drawing half, which <c>DefineQMaterial</c> set for this row before the game half.</summary>
    public Igi2QMaterial QMaterial { get; init; }

    /// <summary>Gets the row the material is in, the level material row a collision face names.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the material's name, <c>zName</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets how much of a bullet's penetration the material takes on each face it crosses, <c>vPenetrationPower</c>.</summary>
    public float PenetrationPower { get; init; }

    /// <summary>Gets the steepest angle a bullet still ricochets at, <c>vMaxAngleForRiccochet</c>.</summary>
    public float MaxAngleForRicochet { get; init; }

    /// <summary>Gets the thinnest the material is taken to be, <c>vMinThickness</c>.</summary>
    public float MinThickness { get; init; }

    /// <summary>Gets a value indicating whether a body walks through it, <c>isWalkThrough</c>.</summary>
    public bool IsWalkThrough { get; init; }

    /// <summary>Gets how far the AI hears steps on it, <c>vAIEventRange</c>.</summary>
    public float AiEventRange { get; init; }

    /// <summary>Gets the eight walking steps, <c>azWalkSound</c>.</summary>
    public IReadOnlyList<string> WalkSounds { get; init; } = [];

    /// <summary>Gets the eight running steps, <c>azRunSound</c>.</summary>
    public IReadOnlyList<string> RunSounds { get; init; } = [];

    /// <summary>Gets the sound of landing on it, <c>zLandSound</c>.</summary>
    public string LandSound { get; init; } = string.Empty;

    /// <summary>Gets the three sounds of a body falling on it, <c>azDeadBodyLandSound</c>.</summary>
    public IReadOnlyList<string> DeadBodyLandSounds { get; init; } = [];

    /// <summary>Gets the sound of rain on it, <c>zRainSound</c>.</summary>
    public string RainSound { get; init; } = string.Empty;

    /// <summary>Gets the six sounds a bullet striking it makes, <c>azBulletHitSound</c>, of which a shot plays one at random.</summary>
    public IReadOnlyList<string> BulletHitSounds { get; init; } = [];

    /// <summary>Gets the two sounds a grenade striking it makes, <c>azGrenadeHitSound</c>.</summary>
    public IReadOnlyList<string> GrenadeHitSounds { get; init; } = [];

    /// <summary>Gets the sound a knife striking it makes, <c>zKnifeHitSound</c>.</summary>
    public string KnifeHitSound { get; init; } = string.Empty;

    /// <summary>Gets what a bullet going into it leaves.</summary>
    public Igi2MaterialEffect BulletEnterEffect { get; init; } = new(Weapons.Igi2WeaponConstants.NoTaskType, null);

    /// <summary>Gets what a bullet coming out of it leaves.</summary>
    public Igi2MaterialEffect BulletLeaveEffect { get; init; } = new(Weapons.Igi2WeaponConstants.NoTaskType, null);
}

/// <summary>
/// The session's materials, read from <c>material/material.qvm</c> (<c>GameMaterial_LoadParameters</c>,
/// <c>igi2 0x433940</c>) after the weapons.
/// </summary>
/// <remarks>
/// <para>
/// The script sets a row with <c>SetActiveMaterial(n)</c> (<c>GameMaterial_SetActiveMaterialCB</c>, <c>0x432D40</c>) and
/// fills it with <c>DefineQMaterial</c>, the half that draws, and <c>DefineGameMaterial</c>, the half the game reads.
/// The row is what a collision face's material and a terrain material's <c>iGameMaterial</c> name.
/// </para>
/// <para>
/// <c>DefineGameMaterial</c> reads its arguments in a fixed order: the name, five numbers, eight walking and eight
/// running steps, the landing, three falls of a body, the rain, six bullet strikes, two grenade strikes and a knife
/// strike. Then come two effects, each a task type and as many further arguments as that type's parameter reader takes:
/// a <c>GunShotEffect</c> takes six; a type with no reader takes none.
/// </para>
/// </remarks>
public sealed class Igi2GameMaterials
{
    /// <summary>The material script.</summary>
    public const string Script = "LOCAL:material/material.qvm";

    /// <summary>The number of rows, <c>GameMaterial_atGameMaterial</c> (<c>GameMaterial_RemoveAll</c>, <c>igi2 0x433800</c>, walks 64).</summary>
    public const int Rows = 64;

    /// <summary>The type name of the one effect that reads parameters.</summary>
    public const string GunShotEffectTypeName = "GunShotEffect";

    private const string LogCategory = "Material";

    private readonly Igi2GameMaterial?[] _rows = new Igi2GameMaterial?[Rows];
    private readonly Igi2QMaterial[] _qMaterials = new Igi2QMaterial[Rows];
    private readonly IOpenIgiLogger _logger;
    private int _gunShotEffectType = Weapons.Igi2WeaponConstants.NoTaskType;
    private int _active;

    /// <summary>Initializes a new instance of the <see cref="Igi2GameMaterials"/> class.</summary>
    /// <param name="logger">Where the load reports.</param>
    public Igi2GameMaterials(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Finds the material in a row.</summary>
    /// <param name="row">The level material row.</param>
    /// <returns>The material, or <see langword="null"/> for a row outside the table or never defined.</returns>
    public Igi2GameMaterial? this[int row] => (uint)row < Rows ? _rows[row] : null;

    /// <summary>Registers the three natives the script calls and the effect type it names.</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="types">The task-type registry the effect's type id comes from.</param>
    public void Register(NativeRegistry natives, TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(types);

        TaskType effect = types.TryGet(GunShotEffectTypeName, out TaskType? known) ? known : types.Declare(GunShotEffectTypeName, []);
        _gunShotEffectType = effect.Id;
        natives.RegisterConstant("TASKTYPE_GUNSHOTEFFECT", effect.Id);
        natives.RegisterFunction("SetActiveMaterial", SetActive);
        natives.RegisterFunction("DefineGameMaterial", Define);

        natives.RegisterFunction("DefineQMaterial", DefineQ);
    }

    /// <summary>Runs the material script.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> applied.</param>
    public void Load(ResourceRegistry resources, NativeRegistry natives)
    {
        Array.Clear(_rows);
        Array.Clear(_qMaterials);
        _active = 0;
        Igi2SessionScript.Run(resources, natives, Script);
        _logger.Information(LogCategory, $"{_rows.Count(static r => r is not null)} materials read.");
    }

    private QvmValue SetActive(QvmCallContext call)
    {
        _active = call.GetInt32(0);
        return QvmValue.FromInt32(1);
    }

    /// <summary>The drawing half of the active row: three numbers, then the colour as three whole numbers of <c>2^-20</c>.</summary>
    private QvmValue DefineQ(QvmCallContext call)
    {
        if ((uint)_active < Rows)
        {
            _qMaterials[_active] = new Igi2QMaterial(
                call.GetSingle(0),
                call.GetSingle(1),
                call.GetSingle(2),
                new Vector3f(Channel(call.GetInt32(3)), Channel(call.GetInt32(4)), Channel(call.GetInt32(5))));
        }

        return QvmValue.FromInt32(1);

        static float Channel(int value) => (new Fpu((double)value) * 0.00000095367432).F;
    }

    private QvmValue Define(QvmCallContext call)
    {
        int cursor = 6;
        string[] walk = Strings(call, ref cursor, 8);
        string[] run = Strings(call, ref cursor, 8);
        string land = Text(call, cursor++, 32);
        string[] dead = Strings(call, ref cursor, 3);
        string rain = Text(call, cursor++, 32);
        string[] bullet = Strings(call, ref cursor, 6);
        string[] grenade = Strings(call, ref cursor, 2);
        string knife = Text(call, cursor++, 32);
        Igi2MaterialEffect enter = Effect(call, ref cursor);
        Igi2MaterialEffect leave = Effect(call, ref cursor);

        if ((uint)_active >= Rows)
        {
            _logger.Warning(LogCategory, $"Material row {_active} is outside the table's {Rows}.");
            return QvmValue.FromInt32(1);
        }

        _rows[_active] = new Igi2GameMaterial
        {
            QMaterial = _qMaterials[_active],
            Index = _active,
            Name = Text(call, 0, 32),
            PenetrationPower = call.GetSingle(1),
            MaxAngleForRicochet = call.GetSingle(2),
            MinThickness = call.GetSingle(3),
            IsWalkThrough = (sbyte)call.GetInt32(4) != 0,
            AiEventRange = call.GetSingle(5),
            WalkSounds = walk,
            RunSounds = run,
            LandSound = land,
            DeadBodyLandSounds = dead,
            RainSound = rain,
            BulletHitSounds = bullet,
            GrenadeHitSounds = grenade,
            KnifeHitSound = knife,
            BulletEnterEffect = enter,
            BulletLeaveEffect = leave,
        };
        return QvmValue.FromInt32(1);
    }

    /// <summary>An effect's type and, when the type reads parameters, its six.</summary>
    private Igi2MaterialEffect Effect(QvmCallContext call, ref int cursor)
    {
        int type = (ushort)call.GetInt32(cursor++);
        if (type != _gunShotEffectType)
        {
            return new Igi2MaterialEffect(type, null);
        }

        bool smoke = (sbyte)call.GetInt32(cursor++) != 0;
        bool particle = (sbyte)call.GetInt32(cursor++) != 0;
        string penetrating = Text(call, cursor++, 24);
        string nonPenetrating = Text(call, cursor++, 24);
        float size = (new Fpu(call.GetSingle(cursor++)) * 4096.0).F;
        string model = Text(call, cursor++, 32);
        return new Igi2MaterialEffect(type, new Igi2GunShotEffectParameters(smoke, particle, penetrating, nonPenetrating, size, model));
    }

    private static string[] Strings(QvmCallContext call, ref int cursor, int count)
    {
        string[] values = new string[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = Text(call, cursor++, 32);
        }

        return values;
    }

    /// <summary>A string argument as a fixed buffer of the given size keeps it: at most one character fewer.</summary>
    private static string Text(QvmCallContext call, int index, int size)
    {
        string value = call.GetString(index);
        return value.Length < size ? value : value[..(size - 1)];
    }
}
