using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>An item taken by walking over it, which stays a task once taken: Project I.G.I.'s <c>GenericPickup</c>.</summary>
/// <remarks>
/// The player's touch (<c>0x46C630</c>) marks it picked up; its run handler (<c>0x46C3C0</c>) then takes it out of the
/// world and runs no child. The task stays in the tree, so the level's expressions keep reading its <c>isPickedUp</c>.
/// The item is kept with the level's other pickups (<c>GenericPickupRegistry</c>), which the renderer and the
/// collision index ask whether it still lies there.
/// </remarks>
public sealed class GenericPickupTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "GenericPickup";

    /// <summary>Gets or sets the item this task is, or <see langword="null"/> until the level's pickups keep it.</summary>
    public GenericPickup? Pickup { get; set; }

    /// <summary>Gets a value indicating whether the item has been taken.</summary>
    public bool IsPickedUp => Pickup?.IsCollected == true;

    /// <inheritdoc/>
    /// <remarks>Nothing to step: the handler only withdraws a taken item, which the pickups' readers see from its state.</remarks>
    public override void Run(LevelRun level) => ArgumentNullException.ThrowIfNull(level);
}
