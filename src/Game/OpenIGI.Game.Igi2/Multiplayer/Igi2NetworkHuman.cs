using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>A player the server runs for one of its clients: an I.G.I. 2 player whose controls are the commands the client sends.</summary>
/// <remarks>
/// <para>
/// It is the level's own kind of player, <see cref="Igi2HumanPlayer"/>, made as the level's player is, standing in every list
/// the level's player stands in, and run at the same two points of the tick (<see cref="Igi2TitlePlayer.NetworkHumans"/>). What
/// differs is only where its channel frame comes from: not the keyboard and mouse, but <see cref="Commands"/>, the client's
/// commands by tick.
/// </para>
/// <para>
/// It has no first-person view of its own on the server; its client looks through its own copy.
/// </para>
/// </remarks>
public sealed class Igi2NetworkHuman
{
    private readonly float[] _frame = new float[Igi2HumanChannels.Count];

    internal Igi2NetworkHuman(Igi2HumanPlayer human, Igi2PlayerWeapons? weapons, Igi2CollisionWorld world)
    {
        Human = human;
        Weapons = weapons;
        World = world;
    }

    /// <summary>Gets the player.</summary>
    public Igi2HumanPlayer Human { get; }

    /// <summary>Gets what it carries, or <see langword="null"/> in a session without weapon tables.</summary>
    public Igi2PlayerWeapons? Weapons { get; }

    /// <summary>Gets its client's commands, by the tick each is for.</summary>
    public UserCommandBuffer Commands { get; } = new(Igi2HumanChannels.Count);

    /// <summary>Gets the channel frame it last ran on.</summary>
    public ReadOnlySpan<float> Frame => _frame;

    /// <summary>Gets the collision world its body stands in.</summary>
    internal Igi2CollisionWorld World { get; }

    /// <summary>Its delayed half on a tick: its zoom, its update on its client's command for the tick, and its delayed handler.</summary>
    /// <param name="tick">The tick being run.</param>
    internal void RunDelayed(long tick)
    {
        Commands.Take(tick, _frame);
        Human.View.RunZoom();
        Human.Ticks = (int)tick;
        Human.Update(_frame);
        Human.RunDelayed();
    }
}
