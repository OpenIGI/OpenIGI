using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Project I.G.I.'s <c>MineField</c>: the shared task, whose explosion is the mission's blasts'.</summary>
/// <remarks>
/// <c>MineField_Explode</c> (<c>0x43AC40</c>) is an Explosion0 at its target, dropped to the ground beneath it, which
/// <see cref="Igi1Blasts"/> makes with the mission's trace and effects.
/// </remarks>
public sealed class Igi1MineFieldTask : MineFieldTask
{
    /// <summary>Gets or sets the mission's blasts, which make the explosion; none until the mission is set up.</summary>
    internal Igi1Blasts? Blasts { get; set; }

    /// <inheritdoc/>
    public override void Explode() => Blasts?.ExplodeMineField(this);
}

/// <summary>Finds a level's minefields.</summary>
public static class Igi1MineFields
{
    /// <summary>The script name level data instantiates the type under.</summary>
    public const string TypeName = MineFieldTask.TypeName;

    /// <summary>Lists the level's minefields, in the tree's order.</summary>
    /// <param name="root">The level's root.</param>
    /// <returns>The minefields.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<Igi1MineFieldTask> Build(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return [.. root.Descendants().OfType<Igi1MineFieldTask>()];
    }
}
