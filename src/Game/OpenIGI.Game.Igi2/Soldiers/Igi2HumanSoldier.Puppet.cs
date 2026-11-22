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
    /// what the server last said it had left fells it here at once, one short of it flinches it. <see langword="null"/> elsewhere.
    /// </summary>
    public (Game.Multiplayer.PredictedSoldierHarm Harm, int Key)? Foresight { get; set; }

    // The flinch the client foresaw, and when, on the foresight's clock, it gives way to the server's pose: once the server's own
    // flinch is posed (the same state, which goes on rather than starting again), or its word was due and never came.
    private (int State, double Until)? _foreseenFlinch;

    // The client's player's round on this puppet, weighed as the server's copy will take it (WeaponHit): the hit counted to the
    // player's tally, then the fall when it takes what the server last said was left, else the cry and the flinch.
    private void ForeseeHit(in Igi2WeaponHit hit)
    {
        if (Foresight is not { } foresight || hit.Tally is not { } tally
            || (hit.Shooter is Igi2Human shooter && shooter.Team == Team) || Mind is { IsInvulnerable: true })
        {
            return;
        }

        Igi2DamagePart part = DamagePartOf(hit);
        float damage = DamageOf(hit, part, Mind?.IsInstantDeathDisabled ?? false);
        bool head = part == Igi2DamagePart.Head;
        tally.Hit(head);
        Damage.LastPart = part;
        if (foresight.Harm.NoteHit(foresight.Key, damage) == Game.Multiplayer.ForeseenHarm.Kill)
        {
            tally.Killed(head);

            // It falls as the server's will, the way the round sent it -- the confirmed fall alone knew no way.
            DeathDirection = hit.ShotVector;
            DeathDamage = hit.Damage;
            DeathByType = DeathTypeOf(hit.WeaponType.WeaponType);
            _foreseenFlinch = null;
            KillOutright();
            RemoteGunfire?.Silence();
            return;
        }

        RecoilMeter = ((Fpu)damage + RecoilMeter).F;
        PlayHitSound(HurtSounds);
        int flinch = Pose == Humans.Igi2HumanPose.LyingFlat ? Igi2SoldierState.LieDownHit : Igi2SoldierState.Hit;
        Locomotion.Enter(flinch, this);
        _foreseenFlinch = (flinch, foresight.Harm.Now + foresight.Harm.ConfirmSeconds);
    }

    // Whether the puppet keeps the flinch the client foresaw rather than the server's pose: until the server's own is posed or its
    // word was due.
    private bool HoldsForeseenFlinch(in Igi2SoldierPose pose)
    {
        if (_foreseenFlinch is not { } flinch)
        {
            return false;
        }

        if (pose.State == flinch.State || Foresight is not { } foresight || foresight.Harm.Now >= flinch.Until)
        {
            _foreseenFlinch = null;
            return false;
        }

        return true;
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
        _foreseenFlinch = null;
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
        if (!HoldsForeseenFlinch(pose) && (Locomotion.CurrentState != pose.State || !_puppetEntered))
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
