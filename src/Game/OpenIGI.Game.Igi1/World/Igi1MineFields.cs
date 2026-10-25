using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>One of Project I.G.I.'s minefields: a blast at a task while its condition holds, again after a wait.</summary>
/// <param name="Task">The <c>MineField</c> task.</param>
/// <param name="Condition">Its "Explode" expression; none never goes off.</param>
/// <param name="RadiusUnits">The blast's full-damage radius, world units.</param>
/// <param name="FalloffUnits">How far past it the damage falls away.</param>
/// <param name="Damage">The blast's damage factor.</param>
/// <param name="DelayTicks">The ticks before the blast, once started.</param>
/// <param name="Fragments">How many fragments it throws.</param>
/// <param name="Fireballs">How many FireBalls it lights.</param>
/// <param name="Sound">The sound it goes off with.</param>
/// <param name="TargetId">The task it goes off at.</param>
/// <param name="WaitSeconds">The wait between blasts; -1 goes off once.</param>
/// <param name="SnapsToGround">Whether the blast is dropped to the ground under the target.</param>
public sealed record Igi1MineField(
    DeclaredTask Task,
    string Condition,
    float RadiusUnits,
    float FalloffUnits,
    float Damage,
    int DelayTicks,
    int Fragments,
    int Fireballs,
    string Sound,
    int TargetId,
    float WaitSeconds,
    bool SnapsToGround)
{
    /// <summary>Gets its wait between blasts.</summary>
    public MineFieldPause Pause { get; } = new();
}

/// <summary>A level's minefields, <c>MineField</c> (<c>0x43A840</c>).</summary>
/// <remarks>
/// Missions 7 and 9 mine stretches of ground: while the player stands in one of the areas a minefield's condition names, it
/// blasts at the player (task 0) every five seconds, dropped to the ground beneath (<c>0x4149B0</c>).
/// </remarks>
public static class Igi1MineFields
{
    /// <summary>The task type.</summary>
    public const string TypeName = "MineField";

    /// <summary>Reads every minefield in a level, with <c>0x43AB50</c>'s defaults for any setting a level leaves out.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <returns>The minefields, in the level's order.</returns>
    public static IReadOnlyList<Igi1MineField> Build(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);
        List<Igi1MineField> fields = [];
        foreach (GameTask task in root.Descendants())
        {
            if (task is not DeclaredTask field || !string.Equals(field.Type.Name, TypeName, StringComparison.Ordinal))
            {
                continue;
            }

            fields.Add(new Igi1MineField(
                field,
                Text(field, "Explode"),
                Real(field, "Explosion radius (meter)", 3f) * 4096f,
                Real(field, "Explosion falloff radius (meter)", 1.5f) * 4096f,
                Real(field, "Explosion damage scale", 3f),
                (int)(Real(field, "Explosion delay (seconds)", 0f) * 30f),
                (int)Real(field, "Explosion fragments", 3f),
                (int)Real(field, "Explosion fireballs", 3f),
                Text(field, "Explosion sound"),
                (int)Real(field, "Explode close to task ID", 0f),
                Real(field, "Explosion delay between explosions (seconds) -1: explode once", 5f),
                !field.TryGetValue("Snap explosion to ground", out object? snap) || snap is not false));
        }

        return fields;
    }

    private static string Text(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

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
