using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Pickups;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>
/// What an I.G.I. 2 level's tasks keep in a save game: each task its own state, and the weapons lying in the level that are not
/// tasks here -- one dropped at run time, or one a load made -- as the children the original keeps them as.
/// </summary>
/// <param name="pickups">The level's pickups, or <see langword="null"/> for a level without them.</param>
/// <param name="dynamic">The level's <c>Dynamic</c> task, <c>Level_ptDynamicQTask</c>, beneath which the game makes a dropped weapon.</param>
/// <param name="mines">The mines laid in the level, or <see langword="null"/>.</param>
public sealed class Igi2SaveStates(Igi2PickupList? pickups, GameTask? dynamic, Player.Igi2LevelMines? mines = null) : ISaveStates
{
    /// <summary>The level's <c>Dynamic</c> task: the last made (<c>Level_DynamicCreateHandler</c>).</summary>
    /// <param name="root">A task of the level.</param>
    /// <returns>The task, or <see langword="null"/> for a level without one.</returns>
    public static GameTask? DynamicOf(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);
        GameTask top = root;
        while (top.Parent is { } up)
        {
            top = up;
        }

        return top.Descendants().LastOrDefault(static t => t.Type.Name == EngineTaskTypes.Dynamic);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SavedPart> PartsOf(GameTask task)
    {
        IReadOnlyList<SavedPart> own = (task as ISavedState)?.SavedParts ?? [];
        bool isDynamic = ReferenceEquals(task, dynamic);
        List<SavedPart>? all = null;
        if (isDynamic && mines is not null)
        {
            // The mines laid, each a child of the level's Dynamic task in the original.
            foreach (Weapons.Igi2Mine mine in mines.Laid)
            {
                (all ??= [.. own]).Add(new SavedPart(Weapons.Igi2Mine.TypeName, mine));
            }
        }

        if (pickups is null)
        {
            return all ?? own;
        }

        foreach (Igi2GunPickup pickup in pickups.Items)
        {
            if (pickup.Task is null && (pickup.Holder is { } holder ? ReferenceEquals(holder, task) : isDynamic))
            {
                (all ??= [.. own]).Add(new SavedPart(Igi2GunPickup.TypeName, pickup, pickup.LevelId));
            }
        }

        return all ?? own;
    }

    /// <inheritdoc/>
    /// <remarks>A weapon that was taken is deleted in the original, and is not counted among its parent's pickups.</remarks>
    public bool IsPresent(GameTask task) => task is not Igi2GunPickupTask gun || gun.Pickup is { IsDelete: false };

    /// <inheritdoc/>
    public ISavedState? Recreate(GameTask parent, string typeName, int id) =>
        pickups is not null && typeName == Igi2GunPickup.TypeName
            ? new MadeGunPickup(pickups, ReferenceEquals(parent, dynamic) ? null : parent, id)
            : mines is not null && pickups?.Tables is { } tables && typeName == Weapons.Igi2Mine.TypeName
                ? new MadeMine(mines, tables)
                : null;

    // A mine to be made once its state is read: whether it is a remote one is among its fields.
    private sealed class MadeMine(Player.Igi2LevelMines mines, Weapons.Igi2WeaponTables tables) : ISavedState
    {
        public void WriteState(StateWriter writer)
        {
        }

        public void ReadState(StateReader reader) => mines.Recreate(Weapons.Igi2Mine.Saved.Read(reader), tables);
    }

    // A pickup to be made once its state is read: the weapon it gives is among its fields.
    private sealed class MadeGunPickup(Igi2PickupList pickups, GameTask? holder, int id) : ISavedState
    {
        public void WriteState(StateWriter writer)
        {
        }

        public void ReadState(StateReader reader)
        {
            if (pickups.Recreate(holder, Igi2GunPickup.Saved.Read(reader)) is { } pickup)
            {
                pickup.LevelId = id;
            }
        }
    }
}
