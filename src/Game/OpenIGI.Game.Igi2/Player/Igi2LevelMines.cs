using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

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
