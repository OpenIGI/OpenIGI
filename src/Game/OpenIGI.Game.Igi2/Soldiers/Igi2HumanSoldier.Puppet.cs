using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

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

    // Whether the server's state has been entered on this copy since it became a puppet.
    private bool _puppetEntered;

    /// <summary>
    /// Gets or sets what a co-op client foresees of its player's rounds on this puppet, and the soldier's name in it: a round past
    /// what the server last said it had left fells it here at once, one short of it marks its hit. <see langword="null"/> elsewhere.
    /// </summary>
    public (Game.Multiplayer.PredictedSoldierHarm Harm, int Key)? Foresight { get; set; }

    // The client's player's round on this puppet: the hit counted to the player's tally and cried out at, then the fall when
    // the round takes what the server last said was left.
    // It is weighed by the least it can have dealt, not by the zone it struck here: the copy plays the server's clip but not at
    // its frame, and a round through the chest here is now and then one through an arm there. Weighed by its zone here, such a
    // round felled a soldier the server kept alive, who got up again when its word came.
    // The flinch is the server's to pose. Flinched here at once, the copy bent out of the pose the server's still stood in, and
    // the next round met another zone of it -- here alone.
    private void ForeseeHit(in Igi2WeaponHit hit)
    {
        if (Foresight is not { } foresight || hit.Tally is not { } tally
            || (hit.Shooter is Igi2Human shooter && shooter.Team == Team) || Mind is { IsInvulnerable: true })
        {
            return;
        }

        Igi2DamagePart part = DamagePartOf(hit);
        bool head = part == Igi2DamagePart.Head;
        tally.Hit(head);
        Damage.LastPart = part;
        if (foresight.Harm.NoteHit(foresight.Key, LeastDamageOf(hit, Mind?.IsInstantDeathDisabled ?? false)) == Game.Multiplayer.ForeseenHarm.Kill)
        {
            tally.Killed(head);

            // It falls as the server's will, the way the round sent it -- the confirmed fall alone knew no way.
            DeathDirection = hit.ShotVector;
            DeathDamage = hit.Damage;
            DeathByType = DeathTypeOf(hit.WeaponType.WeaponType);
            KillOutright();
            RemoteGunfire?.Silence();
            return;
        }

        PlayHitSound(HurtSounds);
    }

    // The least a round that harms at all deals this soldier, whichever zone it strikes.
    private float LeastDamageOf(in Igi2WeaponHit hit, bool instantDeathDisabled)
    {
        float least = float.MaxValue;
        for (int part = 0; part < Igi2HumanDamage.PartCount; part++)
        {
            float dealt = DamageOf(hit, (Igi2DamagePart)part, instantDeathDisabled);
            if (dealt > 0f && dealt < least)
            {
                least = dealt;
            }
        }

        return least == float.MaxValue ? 0f : least;
    }

    /// <summary>
    /// Stands up a puppet the client foresaw falling whom the server kept alive (<see cref="Game.Multiplayer.ForeseenHarmVerdict.GetUp"/>):
    /// its fall undone, what the death set put back as a living body has it, and the server's state entered on the next pose.
    /// <b>Not retail.</b>
    /// </summary>
    /// <param name="remaining">What the server says it has left, in health, or <see langword="null"/> when not told.</param>
    public void RiseAsPuppet(float? remaining)
    {
        if (!IsDead)
        {
            return;
        }

        Damage.Damage = remaining is { } left ? Math.Max(0f, Damage.MaxDamage - left) : 0f;
        IsDead = false;
        Locomotion.Flags &= ~DeadSignal;
        Signal &= ~Igi2SoldierSignal.Dead;
        LifeState = 0;
        IsDeathAnimationDone = false;
        IsDeathPositionStabilized = false;
        IsDeathSoundPlayed = false;
        Pose = Humans.Igi2HumanPose.Standing;
        CameraMode = 1;
        View.IsLockOrientation = false;
        IsLockOrientation = false;
        UpperBodyFlags &= ~1;
        GroundTolerance = LandingGroundTolerance;
        IsDisableDeltaTranslation = false;
        Animation.SetMainFlags(raise: Igi2AnimControllerBits.None, clear: Igi2AnimControllerBits.NoRootTranslation);
        _puppetEntered = false;
    }

    /// <summary>Gets what shows the rounds the server's copy fires, once posed.</summary>
    internal Weapons.Igi2RemoteGunfire? RemoteGunfire { get; set; }

    /// <summary>Gets the soldier as it stands now, as a server sends it and a client stands its copy before the first is sent.</summary>
    /// <returns>Its pose.</returns>
    public Igi2SoldierPose PuppetPose() =>
        new(Position, Gamma, View.Pitch, Locomotion.CurrentState, !IsDead, unchecked((byte)(Weapon.Inventory?.ShotSequence ?? 0)));

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

            RemoteGunfire?.Silence();
            Update(NoChannels);
            return;
        }

        // A puppet's locomotion never ticks, so a state's entry -- which plays its clip -- runs only as it is entered here. The
        // soldier is made in Stand with that entry left for its first tick: one the server keeps standing was never entered and
        // stood in no clip at all, its bind pose, on every client.
        if (Locomotion.CurrentState != pose.State || !_puppetEntered)
        {
            Locomotion.Enter(pose.State, this);
            _puppetEntered = true;
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
