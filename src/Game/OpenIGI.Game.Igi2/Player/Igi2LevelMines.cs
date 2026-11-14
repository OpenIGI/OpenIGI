using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// The level the player's proximity mines are laid in: each joins the dynamic task, the collision world, the blasts' targets and the
/// notify list, and leaves them all once it has exploded.
/// </summary>
/// <param name="player">The player, whose dynamic task and explosions the mines use.</param>
/// <param name="collision">The collision world.</param>
/// <param name="models">The level's models.</param>
/// <param name="notified">The list humans tell their place to.</param>
public sealed class Igi2LevelMines(Igi2TitlePlayer player, Igi2CollisionWorld collision, Func<string, Formats.World.MefModel?> models, Weapons.Igi2NotifyList notified)
    : Weapons.IIgi2MineWorld
{
    private readonly List<Weapons.Igi2Mine> _laid = [];

    /// <summary>Gets the mines lying in the level, in the order they were laid.</summary>
    public IReadOnlyList<Weapons.Igi2Mine> Laid => _laid;

    /// <inheritdoc/>
    public Igi2CollisionWorld Collision => collision;

    /// <inheritdoc/>
    public Func<string, Formats.World.MefModel?> Models => models;

    /// <inheritdoc/>
    public Weapons.Igi2ExplosionWorld? Explosions => player.Explosions;

    /// <inheritdoc/>
    public Weapons.Igi2NotifyList Notified => notified;

    /// <inheritdoc/>
    public void Lay(Weapons.Igi2Mine mine)
    {
        ArgumentNullException.ThrowIfNull(mine);
        _laid.Add(mine);
        player.Dynamic.Add(mine);
        if (mine.Solid is { } solid)
        {
            collision.AddObject(solid);
        }

        player.Explosions?.AddTarget(mine);
        notified.Add(mine);
    }

    /// <summary>
    /// Deletes every mine, as <c>Game_LoadFromSlot</c> does before a load (<c>QTask_Delete</c> down <c>Mine_ptMineQTaskList</c>):
    /// each the save holds is made again by it.
    /// </summary>
    public void DeleteAll()
    {
        foreach (Weapons.Igi2Mine mine in _laid.ToArray())
        {
            mine.Delete();
            Remove(mine);
        }
    }

    /// <summary>
    /// Makes the mine a save holds, as <c>Serialize_GetChildTaskFromArgList</c> makes one that is not found. A save keeps neither
    /// its model nor its blast's radius, which its type gives it: the weapon whose fire type lays that kind of mine.
    /// </summary>
    /// <param name="saved">Its state.</param>
    /// <param name="tables">The session's weapon tables.</param>
    /// <returns>The mine, or <see langword="null"/> when no weapon lays that kind.</returns>
    public Weapons.Igi2Mine? Recreate(in Weapons.Igi2Mine.Saved saved, Weapons.Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        foreach (Weapons.Igi2WeaponTypeTask weapon in tables.WeaponTypes)
        {
            for (int slot = 0; slot < Weapons.Igi2WeaponTypeTask.FireTypeSlots; slot++)
            {
                float? radius = weapon.FireType(slot) switch
                {
                    Weapons.Igi2RemoteMineTask remote when saved.IsRemote => remote.ExplosionRadiusMeters,
                    Weapons.Igi2ProximityMineTask proximity when !saved.IsRemote => proximity.ExplosionRadiusMeters,
                    _ => null,
                };
                if (radius is not { } explosion)
                {
                    continue;
                }

                Weapons.Igi2Mine mine = new(
                    this, saved.Position, saved.Frame, weapon.FixedModel, saved.ArmTicks, saved.TimeBeforeExplode, saved.DetectionRadius, explosion, saved.IsRemote);
                mine.Apply(saved);
                Lay(mine);
                return mine;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void Remove(Weapons.Igi2Mine mine)
    {
        ArgumentNullException.ThrowIfNull(mine);
        _laid.Remove(mine);
        if (mine.Solid is { } solid)
        {
            collision.RemoveObject(solid);
        }

        player.Explosions?.RemoveTarget(mine);
        notified.Remove(mine);
    }

    /// <inheritdoc/>
    public void PlaySoundAt(string sound, Vector3d position) => player.Sounds.New(sound, position);
}
