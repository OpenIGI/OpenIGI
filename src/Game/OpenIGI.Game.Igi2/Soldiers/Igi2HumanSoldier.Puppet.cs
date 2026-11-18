using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>A soldier as a co-op server has it, for a client to pose its own copy by.</summary>
/// <param name="Position">Where its feet stand, in world units.</param>
/// <param name="Gamma">Its heading.</param>
/// <param name="Pitch">Its view's pitch.</param>
/// <param name="State">Its locomotion state.</param>
/// <param name="Alive">Whether it lives.</param>
/// <param name="Shots">The rounds its weapon has fired, counted round in a byte: a change is a shot to flash and sound.</param>
public readonly record struct Igi2SoldierPose(Vector3d Position, float Gamma, float Pitch, int State, bool Alive, byte Shots);

/// <content>A co-op client's soldier: posed by the server's, its own mind left alone.</content>
public sealed partial class Igi2HumanSoldier
{
    private static readonly float[] NoChannels = new float[Humans.Igi2HumanChannels.Count];

    /// <summary>
    /// Gets or sets how the server has this soldier, on a co-op client; <see langword="null"/> where the soldier is the level's own
    /// to run (single player, or the server).
    /// </summary>
    /// <remarks>
    /// The soldiers are the one world every player fights, so only the server runs their minds and decides their harm. A
    /// client's copy is stood, turned and animated as the server's is each tick, falls when the server's falls, and takes no harm
    /// of its own: a round from this client's player throws its blood and counts its hit, but the server's copy is the one hurt.
    /// </remarks>
    public Igi2SoldierPose? Puppet { get; set; }

    /// <summary>Gets what shows the rounds the server's copy fires, once posed.</summary>
    internal Weapons.Igi2RemoteGunfire? RemoteGunfire { get; set; }

    /// <summary>Gets the soldier as it stands now, as a server sends it and a client stands its copy before the first is sent.</summary>
    /// <returns>Its pose.</returns>
    public Igi2SoldierPose PuppetPose() =>
        new(Position, Gamma, View.Pitch, Locomotion.CurrentState, !IsDead, unchecked((byte)(Weapon.Inventory?.TotalBulletsFired ?? 0)));

    // The puppet's tick, in place of the mind's update: its state's clip played, the body put where the server's stands.
    private void RunPuppet(in Igi2SoldierPose pose)
    {
        // The server's fell: this one falls as its own death runs, and goes on running it, the controls empty.
        if (!pose.Alive || IsDead)
        {
            if (!IsDead)
            {
                KillOutright();
            }

            Update(NoChannels);
            return;
        }

        if (Locomotion.CurrentState != pose.State)
        {
            Locomotion.Enter(pose.State, this);
        }

        Gamma = pose.Gamma;
        IdealGamma = pose.Gamma;
        Position = pose.Position;
        UpdateBody();
        View.Pitch = pose.Pitch;
        Animation.StepMain();
        TouchPose();
        UpdateViewBody();
        Weapon.UpdateStatus();
        if (Weapon.Firing is { } firing)
        {
            (RemoteGunfire ??= new Weapons.Igi2RemoteGunfire(firing, this)).Take(pose.Shots);
        }
    }

    // The room a puppet is drawn by: its graph's nearest node's, as the server's mind keeps its own footing on the graph.
    private object? PuppetSector()
    {
        if (Mind is not Ai.Runtime.Igi2HumanAi { Graph: { } graph })
        {
            return null;
        }

        Ai.Igi2AiGraphNode? nearest = null;
        double best = double.MaxValue;
        foreach (Ai.Igi2AiGraphNode node in graph.Nodes.Values)
        {
            double d = Vector3d.DistanceSquared(node.Position, Position);
            if (d < best)
            {
                best = d;
                nearest = node;
            }
        }

        return nearest?.Sector;
    }
}
