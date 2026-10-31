using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

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

    /// <summary>
    /// Gets or sets where a client puts it: another player as the snapshots show it, or <see langword="null"/> on the server,
    /// where it runs on its client's commands.
    /// </summary>
    /// <remarks>
    /// A client has no other player's commands, only where the server had it. So its copy is a puppet: each tick its body
    /// is put in the state the snapshot names and run with no controls, so the body's own states choose its clips as the
    /// server's did, and then it is stood exactly where the snapshot puts it, facing and looking as it does.
    /// </remarks>
    public ReplicatedHuman? PuppetTarget { get; set; }

    /// <summary>Its delayed half on a tick: its zoom, its update on its client's command for the tick, and its delayed handler.</summary>
    /// <param name="tick">The tick being run.</param>
    internal void RunDelayed(long tick)
    {
        if (PuppetTarget is { } target)
        {
            RunPuppet(tick, target);
            return;
        }

        // The sequel's human is one of the level's and runs every tick, so a dry queue repeats the last command.
        Commands.TakeOrRepeat(_frame);
        Human.View.RunZoom();
        Human.Ticks = (int)tick;
        Human.Update(_frame);
        Human.RunDelayed();
    }

    private void RunPuppet(long tick, in ReplicatedHuman target)
    {
        Array.Clear(_frame);
        if (Human.Locomotion.CurrentState != target.State)
        {
            Human.Locomotion.Enter(target.State, Human);
        }

        Human.Ticks = (int)tick;
        Human.Update(_frame);
        Human.Position = target.Position.ToVector();
        Human.Speed = target.Velocity;
        Human.Gamma = Quantize.AngleFromBits(target.Yaw);
        Human.IdealGamma = Human.Gamma;
        Human.View.Pitch = Quantize.AngleFromBits(target.Pitch);
        Human.UpdateBody();
        Human.UpdateViewBody(updateAnimation: false);
    }
}
