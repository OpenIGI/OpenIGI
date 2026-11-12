using OpenIGI.Game.Level;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A human's weapon: Project I.G.I.'s <c>Gun</c>, a child of the soldier or the player that carries it.</summary>
/// <remarks>
/// Its run handler (<c>0x478640</c>) is the weapon's own clock: it counts its cadence's cooldown down, runs what is
/// attached to it, and eases its kick back. It fires nothing itself -- the human's update does, through its state machine,
/// when its <c>HumanAI</c> or its input has asked. A level declares a soldier's <c>Gun</c> ahead of its <c>HumanAI</c>, so a
/// weapon's cooldown is counted down before the soldier's update of the same tick tests it.
/// </remarks>
public sealed class Igi1GunTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Gun";

    /// <summary>Gets or sets the weapon's clock for this tick, or <see langword="null"/> for a gun nothing has taken up.</summary>
    internal Action? Clock { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Clock?.Invoke();
        level.RunChildren(this);
    }
}
