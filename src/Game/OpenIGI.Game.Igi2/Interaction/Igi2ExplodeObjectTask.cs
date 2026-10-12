using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// An object that explodes, as a barrel does, I.G.I. 2's <c>ExplodeObject</c>, exporting <c>isExploded</c>; what it does is
/// <see cref="Igi2Explosive"/>'s.
/// </summary>
/// <remarks>It is still placed by the world scene, which reads its parameters by name (<see cref="IIgi2PlacedTask"/>).</remarks>
public sealed partial class Igi2ExplodeObjectTask
    : Igi2Task, IIgi2PlacedTask, IIgi2ExplosiveParameters, IIgi2WeaponHitTarget, IIgi2ExplodingTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ExplodeObject";

    /// <summary>Initializes a new instance of the <see cref="Igi2ExplodeObjectTask"/> class.</summary>
    public Igi2ExplodeObjectTask() => Explosive = new Igi2Explosive(this, this);

    /// <summary>Gets what it does when it explodes.</summary>
    public Igi2Explosive Explosive { get; }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String16, "Destroyed model")]
    public string DestroyedModel { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Damage scale")]
    public float DamageScale { get; set; } = 1f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Explosion radius")]
    public float ExplosionRadius { get; set; } = 2f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Explosion falloff radius")]
    public float ExplosionFalloffRadius { get; set; } = 1.5f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Explosion damage scale")]
    public float ExplosionDamageScale { get; set; } = 1f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Real32, "Explosion delay")]
    public float ExplosionDelay { get; set; } = 1f;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Int32, "Explosion fragments")]
    public int ExplosionFragments { get; set; } = 3;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.Int32, "Explosion fireballs")]
    public int ExplosionFireballs { get; set; } = 3;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.VarString, "Explosion expression")]
    public string ExplosionScript { get; set; } = string.Empty;

    /// <inheritdoc/>
    [ScriptParam(ScriptParamType.String16, "Explosion sound")]
    public string ExplosionSound { get; set; } = string.Empty;

    /// <inheritdoc/>
    public bool IsDestroyed => Explosive.IsExploded;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        Explosive.Run(level);
    }

    /// <inheritdoc/>
    public void WeaponHit(in Igi2WeaponHit hit) => Explosive.WeaponHit(hit.Damage);

    /// <inheritdoc/>
    public void ExplosionHit(in Igi2ExplosionHit hit) => Explosive.ExplosionHit(hit);

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Orientation" => Orientation,
            "Model" => Model,
            "Destroyed model" => DestroyedModel,
            "Damage scale" => DamageScale,
            "Explosion radius" => ExplosionRadius,
            "Explosion falloff radius" => ExplosionFalloffRadius,
            "Explosion damage scale" => ExplosionDamageScale,
            "Explosion delay" => ExplosionDelay,
            "Explosion fragments" => ExplosionFragments,
            "Explosion fireballs" => ExplosionFireballs,
            "Explosion expression" => ExplosionScript,
            "Explosion sound" => ExplosionSound,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables) => Explosive.ExportVars(variables);

    /// <inheritdoc/>
    protected override void OnParsed() => Explosive.Reset();
}

/// <summary>A placed object the world may come to draw as its destroyed model.</summary>
public interface IIgi2Destructible
{
    /// <summary>Gets the level id of the task that placed it.</summary>
    int Id { get; }

    /// <summary>Gets a value indicating whether it is destroyed.</summary>
    bool IsDestroyed { get; }
}

/// <summary>Which of the level's placements are drawn as their destroyed model, by the task that placed them.</summary>
/// <param name="destructibles">The level's destructible objects.</param>
public sealed class Igi2PlacementDestruction(IEnumerable<IIgi2Destructible> destructibles) : OpenIGI.Game.World.IPlacementDestruction
{
    private readonly Dictionary<int, IIgi2Destructible> _byId = destructibles.ToDictionary(static d => d.Id);

    /// <inheritdoc/>
    public bool IsDestroyed(OpenIGI.Game.World.WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return IsDestroyed(placement.LevelId);
    }

    /// <summary>Gets a value indicating whether the object a task placed is destroyed.</summary>
    /// <param name="levelId">The placing task's level id.</param>
    /// <returns><see langword="true"/> once that task's object is destroyed.</returns>
    public bool IsDestroyed(int levelId) =>
        levelId >= 0 && _byId.TryGetValue(levelId, out IIgi2Destructible? destructible) && destructible.IsDestroyed;
}
