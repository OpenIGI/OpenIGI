using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Ai;

/// <summary>A retail <c>GuardGenerator</c>: one authored soldier template with a bounded spawn count.</summary>
/// <remarks>
/// IGI1 registers this type at <c>0x487BE0</c>. Its update at <c>0x487EE0</c> evaluates the
/// <c>Generate guards</c> expression, creates at most <c>Max spawns</c> copies of the one authored child,
/// and exports <c>isOn</c>, <c>isSpawned</c>, and <c>nSpawns</c>. The native clone helper
/// (<c>0x488000</c>/<c>0x488040</c>) recursively rebuilds the authored subtree; the managed runtime keeps
/// the typed authored task as the template while creating a fresh live brain/view for every instance.
/// </remarks>
public partial class GuardGeneratorTask : ContainerTask
{
    /// <summary>The level-script type name.</summary>
    public const string TypeName = "GuardGenerator";

    /// <summary>The expression that enables spawning.</summary>
    [ScriptParam(ScriptParamType.VarString, "Generate guards")]
    public string GenerateGuards { get; set; } = string.Empty;

    /// <summary>The total number of live instances this generator may create, including the first.</summary>
    [ScriptParam(ScriptParamType.Int32, "Max spawns")]
    public int MaxSpawns { get; set; }

    /// <summary>The last evaluated value of <see cref="GenerateGuards"/>.</summary>
    public bool IsOn { get; private set; }

    /// <summary>Whether the authored child has been spawned at least once.</summary>
    public bool IsSpawned { get; private set; }

    /// <summary>How many instances have been spawned so far.</summary>
    public int SpawnCount { get; private set; }

    /// <summary>Advances the native generator state after its expression and child state have been sampled.</summary>
    /// <param name="isOn">The evaluated <c>Generate guards</c> expression.</param>
    /// <param name="childIsDead">Whether the currently spawned authored child has died.</param>
    /// <returns>Whether the update records the first instance or a replacement; see <see cref="GuardSpawnDecision"/>.</returns>
    public GuardSpawnDecision Advance(bool isOn, bool childIsDead)
    {
        IsOn = isOn;
        if (!isOn || MaxSpawns <= SpawnCount)
        {
            return GuardSpawnDecision.None;
        }

        if (!IsSpawned)
        {
            IsSpawned = true;
            SpawnCount++;
            return GuardSpawnDecision.FirstSpawn;
        }

        if (!childIsDead)
        {
            return GuardSpawnDecision.None;
        }

        SpawnCount++;
        return GuardSpawnDecision.Respawn;
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        variables.BindBool(this, "isOn", () => IsOn, value => IsOn = value);
        variables.BindBool(this, "isSpawned", () => IsSpawned, value => IsSpawned = value);
        variables.BindInt(this, "nSpawns", () => SpawnCount, value => SpawnCount = value);
    }
}

/// <summary>The observable transition produced by one <see cref="GuardGeneratorTask"/> update.</summary>
public enum GuardSpawnDecision
{
    /// <summary>No child transition occurred.</summary>
    None,

    /// <summary>The authored template became its first live instance.</summary>
    FirstSpawn,

    /// <summary>A fresh instance replaces the dead guard; the previous corpse remains in the world.</summary>
    Respawn,
}
