using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A soldier as a co-op server has it, for a client to pose its own copy by.</summary>
/// <param name="Position">Where the soldier stands, in world units.</param>
/// <param name="Facing">Its heading, radians about the vertical.</param>
/// <param name="Animation">Its body's clip.</param>
/// <param name="UpperBody">Its torso's clip, or -1.</param>
/// <param name="LookPitch">Its look's pitch, radians.</param>
/// <param name="Alive">Whether it lives.</param>
/// <param name="Shots">Its shots so far, counted round in a byte: a change is a shot to flame and sound.</param>
public readonly record struct SoldierPuppet(Vector3d Position, float Facing, int Animation, int UpperBody, float LookPitch, bool Alive, byte Shots = 0)
{
    /// <summary>Gets the heading an orientation turns a soldier to, the inverse of <see cref="Matrix3f.RotationZ"/>.</summary>
    /// <param name="orientation">The orientation.</param>
    /// <returns>The heading, radians.</returns>
    public static float FacingOf(Matrix3f orientation) => MathF.Atan2(orientation.M21, orientation.M11);
}

/// <summary>A co-op client's soldiers, posed by the server's rather than run by their own minds.</summary>
public sealed partial class SoldierSimulation
{
    private readonly Dictionary<SoldierView, SoldierPuppet> _puppets = [];

    /// <summary>Gets what a soldier holds and fires, or <see langword="null"/>.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>Its agent.</returns>
    public SoldierAgent? AgentOf(SoldierView view) => _context.FindAgent(view);

    /// <summary>Gets the level's soldiers, in no set order.</summary>
    public IEnumerable<SoldierView> SoldierViews => Brains.Keys;

    /// <summary>Poses a soldier from the server's copy from now on, its own mind no longer run.</summary>
    /// <param name="view">The soldier.</param>
    /// <param name="puppet">How the server has it.</param>
    /// <remarks>
    /// The original's soldiers are the one world both players fight, so only the server runs their minds; a client's copies
    /// are stood, turned and animated as the server's are, and fall when the server's fall.
    /// </remarks>
    public void Puppet(SoldierView view, in SoldierPuppet puppet)
    {
        ArgumentNullException.ThrowIfNull(view);
        _puppets[view] = puppet;
    }

    // A puppeted soldier's tick: its pose taken from the server's, its rig stepped, its mind left alone.
    private bool TickPuppet(SoldierView view)
    {
        if (!_puppets.TryGetValue(view, out SoldierPuppet puppet))
        {
            return false;
        }

        if (!puppet.Alive)
        {
            if (!view.IsDead)
            {
                view.Kill(puppet.Animation);
            }

            view.Tick();

            // Its gun stops with it. An automatic's sound is a loop held for as long as the soldier is said to be firing,
            // and is let go only when told it isn't; a dead puppet told nothing kept firing on the client's speakers.
            if (_context.FindAgent(view) is { } fallen && Brains.TryGetValue(view, out Igi1HumanAi? dead))
            {
                fallen.FiredThisTick = false;
                WeaponSound?.Invoke(dead.RuntimeId, false, fallen.WeaponType, dead.EyePosition());
            }

            return true;
        }

        view.PlaceAt(puppet.Position, Matrix3f.RotationZ(puppet.Facing));
        if (view.CurrentAnimation != puppet.Animation && puppet.Animation >= 0)
        {
            view.PlayStateAnimation(puppet.Animation, loops: true);
        }

        if (view.UpperBodyAnimation != puppet.UpperBody)
        {
            if (puppet.UpperBody >= 0)
            {
                view.PlayUpperBodyAnimation(puppet.UpperBody, loops: true);
            }
            else
            {
                view.StopUpperBodyAnimation();
            }
        }

        view.LookAlong(puppet.LookPitch);
        view.Tick();

        // The server's soldier fired since the last pose: this one's gun flames and is heard as its own would be.
        if (_context.FindAgent(view) is { } agent && Brains.TryGetValue(view, out Igi1HumanAi? brain))
        {
            bool fired = unchecked((byte)agent.ShotSequence) != puppet.Shots;
            if (fired)
            {
                agent.ShotSequence = puppet.Shots;
                agent.FiredThisTick = true;
            }

            WeaponSound?.Invoke(brain.RuntimeId, fired, agent.WeaponType, brain.EyePosition());
        }

        return true;
    }
}
