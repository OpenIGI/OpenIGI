using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// A soldier as a mod's <see cref="ISoldierBody"/> sees him: who he is, his brain, and where the game has him this tick and
/// this frame. Read only. <b>Not retail.</b>
/// </summary>
public sealed class SoldierBodySoldier
{
    private readonly SoldierGroup _group;
    private readonly SoldierMember _member;

    internal SoldierBodySoldier(SoldierGroup group, SoldierMember member, SoldierSimulation simulation, IGraphicsDevice device)
    {
        _group = group;
        _member = member;
        Simulation = simulation;
        Device = device;
    }

    /// <summary>Gets the soldier.</summary>
    public SoldierView View => _member.Agent.View;

    /// <summary>Gets his brain, or <see langword="null"/> for one the simulation does not run.</summary>
    public Igi1HumanAi? Brain => Simulation.BrainOf(_member.Agent.View);

    /// <summary>Gets the simulation he is part of.</summary>
    public SoldierSimulation Simulation { get; }

    /// <summary>Gets the device he is drawn with.</summary>
    public IGraphicsDevice Device { get; }

    /// <summary>Gets the lowest point of his own model, in metres above his origin: where its feet are.</summary>
    public float LowestVertexZ => _group.Body.LowestVertexZ;

    /// <summary>Gets a value indicating whether he has been placed for a tick yet.</summary>
    public bool HasTickState => _member.HasTickState;

    /// <summary>Gets where he stands this tick, in metres.</summary>
    public Vector3d TickWhere => _member.TickWhere;

    /// <summary>Gets where he stood the tick before, in metres.</summary>
    public Vector3d PrevTickWhere => _member.PrevTickWhere;

    /// <summary>Gets his turn this tick.</summary>
    public Matrix3f TickOrientation => _member.TickOrientation;

    /// <summary>Gets a value indicating whether he has been placed for a frame yet.</summary>
    public bool HasFrameState => _member.HasFrameState;

    /// <summary>Gets where he was last drawn, in metres.</summary>
    public Vector3d DrawnWhere => _member.DrawnWhere;

    /// <summary>Gets his turn as last drawn.</summary>
    public Matrix3f DrawnOrientation => _member.DrawnOrientation;

    /// <summary>Gets a value indicating whether this frame's motion vectors are not to be trusted for him (a teleport).</summary>
    public bool NoHistoryThisFrame => _member.NoHistoryThisFrame;

    /// <summary>Gets how many shots he has fired: it goes up by one with each.</summary>
    public int ShotSequence => _member.Agent.ShotSequence;

    /// <summary>Gets the far end of the gun in his hand, in its own frame and units, or <see langword="null"/> while he holds none.</summary>
    public Vector3f? HeldWeaponMuzzle => _member.Weapon?.Muzzle;
}
