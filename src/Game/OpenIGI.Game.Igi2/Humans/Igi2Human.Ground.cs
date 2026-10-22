using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>The ground under a human: what it stands on, how far below its feet, and the roof above its head.</summary>
public abstract partial class Igi2Human
{
    /// <summary>The signal bit raised while the human stands on the ground, <c>bSignal &amp; 2</c>.</summary>
    protected const uint GroundedSignal = 0x2;

    /// <summary>The signal bit raised while a roof is lower than a standing human, <c>bSignal &amp; 8</c>.</summary>
    protected const uint LowRoofSignal = 0x8;

    /// <summary>How far down the footing query reaches from the check point: ten metres.</summary>
    private const double GroundReach = -40960.0;

    /// <summary>How far up the roof query reaches from the feet: two metres.</summary>
    private const double RoofReach = 8192.0;

    /// <summary>How far up the footing query reaches when nothing was found below: ten metres.</summary>
    private const double BuriedReach = 40960.0;

    /// <summary>A roof nearer than this above the feet, 1.8 metres, is low.</summary>
    private const double LowRoofHeight = 7372.8;

    /// <summary>Where the material probe starts above the struck point, and how far down it reaches.</summary>
    private const double MaterialProbeAbove = 1638.4;

    private const double MaterialProbeDepth = 3686.4;

    /// <summary>Radians to quarter turns, <c>2/π</c> as the float <c>0x3F22F983</c>.</summary>
    private const float QuarterTurnsPerRadian = 0.63661975f;

    /// <summary>The query groups the footing asks with: every object type, and the ground.</summary>
    private const uint FootingGroups = 0xFFFFFFFF;

    /// <summary>The mesh level the footing meets, <c>iMesh</c> 1; the material probe meets level 0.</summary>
    private const int FootingMesh = 1;

    private readonly WorldLineTrace _footing = new(2) { Mesh = FootingMesh };
    private readonly WorldLineTrace _buried = new(1) { Mesh = FootingMesh };
    private readonly WorldLineTrace _material = new(1);
    private readonly WorldIntersection _graphFooting = new();

    /// <summary>Gets or sets where the human stands, <c>tModelObj.tPos</c>, in world units.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets what the human stands on, <c>Human_s.ptGroundQTaskRef</c>; <see langword="null"/> in the air.</summary>
    public IWorldCollider? GroundCollider { get; private set; }

    /// <summary>Gets the last footing hit, <c>Human_s.tGroundIntersection</c>.</summary>
    public WorldIntersection GroundIntersection { get; } = new();

    /// <summary>Gets how far the feet are above the ground found, <c>Human_s.vDistanceToGround</c>.</summary>
    /// <remarks><c>Human_CreateHandler</c> starts it at 2^30, as far as "nothing below" reaches.</remarks>
    public double DistanceToGround { get; private set; } = 1073741824.0;

    /// <summary>Gets how far the roof is above the feet, <c>Human_s.vDistanceToRoof</c>, as of the last roof found.</summary>
    /// <remarks>Starts at 2^30 (<c>Human_CreateHandler</c>).</remarks>
    public double DistanceToRoof { get; private set; } = 1073741824.0;

    /// <summary>Gets or sets how far above the feet the footing query starts, <c>Human_s.vGroundCheckZOffset</c>.</summary>
    public float GroundCheckZOffset { get; set; }

    /// <summary>Gets or sets how far the collision body is lifted, <c>Human_s.vCollisionZOffset</c>.</summary>
    public float CollisionZOffset { get; set; }

    /// <summary>Gets the slope of the last step on the ground, in radians, <c>Human_s.vGroundAngle</c>.</summary>
    public float GroundAngle { get; private set; }

    /// <summary>Gets or sets how lit the human is where it stands, <c>Human_s.vLightmap</c>.</summary>
    /// <remarks>Starts fully lit, 1 (<c>Human_CreateHandler</c>), which the first probe averages against.</remarks>
    public float Lightmap { get; set; } = 1f;

    /// <summary>Gets or sets the speed a slope is pushing the body at, <c>Human_s.tIdealSlidingSpeed</c>.</summary>
    public Vector3f IdealSlidingSpeed { get; set; }

    /// <summary>Gets or sets a value indicating whether the platform underfoot is to be followed, <c>Human_s.isUpdatePlatformPos</c>.</summary>
    public bool IsUpdatePlatformPos { get; set; }

    /// <summary>Gets or sets a value indicating whether the platform underfoot moved, <c>Human_s.isPlatformMoving</c>.</summary>
    public bool IsPlatformMoving { get; set; }

    /// <summary>
    /// Gets the brain that drives the human, if it has one that keeps it on a graph (<c>Human_s.ptHumanAIQTask</c>); a living human
    /// with one takes its footing from the graph.
    /// </summary>
    protected virtual IIgi2GraphFooting? GraphFooting => null;

    /// <summary>Gets where the rigid object underfoot stands now, if the human stands on one.</summary>
    /// <param name="position">The object's position.</param>
    /// <returns><see langword="false"/> when the ground is not a rigid object.</returns>
    public bool TryGetGroundObjectPosition(out Vector3d position)
    {
        if (IsGroundRigidObject && GroundCollider is Igi2RigidObject solid)
        {
            position = solid.Position;
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>Lets go of the ground, as a jump does: the reference is dropped and the footing hit forgotten.</summary>
    /// <remarks><c>HumanPlayer_InitStateJump</c> (<c>igi2 0x4ABA60</c>).</remarks>
    public void LeaveGround()
    {
        DropGround();
        GroundIntersection.IsHit = false;
    }

    /// <summary>
    /// Finds the ground under a move and settles the feet on it: <c>Human_GroundCollision</c> (<c>igi2 0x4FD130</c>).
    /// </summary>
    /// <param name="from">Where the move started.</param>
    /// <param name="to">Where it ends; its height is moved onto the ground when the human lands there.</param>
    /// <param name="tolerance">How far below the feet the ground still counts as underfoot.</param>
    /// <remarks>
    /// <para>
    /// Two vertical lines go out together: one down ten metres from <see cref="GroundCheckZOffset"/> above the
    /// destination, and one up two metres from it for the roof. A roof is a hit facing down; one lower than
    /// 1.8 metres raises the low-roof signal. With nothing below, the first line is cast up ten metres instead,
    /// and only a surface facing up counts.
    /// </para>
    /// <para>
    /// The struck object alone is then probed again, from 1638.4 above the hit down through it, on its finest
    /// collision level. That probe's material replaces the footing's, and its light is averaged into the human's.
    /// A living human with a brain on a graph casts nothing and stands on the graph instead (<see cref="GraphFooting"/>).
    /// The human stands when the drop to the ground is within the tolerance, or within two metres while a slope
    /// slides it. Standing puts the feet on the ground, turns the ground's normals up, keeps hold of what is
    /// underfoot, and measures the slope of the step.
    /// </para>
    /// </remarks>
    public void GroundCollision(Vector3d from, ref Vector3d to, float tolerance)
    {
        if (!IsDead && GraphFooting is { } brain)
        {
            GraphGroundCollision(brain, from, ref to);
            return;
        }

        GroundIntersection.Clear();
        DistanceToGround = 1073741824.0;

        Vector3d check = new(to.X, to.Y, ((Fpu)GroundCheckZOffset + to.Z).Value);
        _footing.Starts[0] = check;
        _footing.Starts[1] = to;
        Surroundings.Collision.NextZ(_footing, [GroundReach, RoofReach], FootingGroups);

        WorldIntersection ground = _footing.Intersections[0];
        WorldIntersection roof = _footing.Intersections[1];
        if (roof.IsHit && roof.Normal.Z < 0f)
        {
            DistanceToRoof = roof.Distance;
            if (roof.Distance < LowRoofHeight)
            {
                Locomotion.Flags |= LowRoofSignal;
            }
        }

        if (!ground.IsHit)
        {
            _buried.Starts[0] = check;
            Surroundings.Collision.NextZ(_buried, [BuriedReach], FootingGroups);
            ground = _buried.Intersections[0];
            if (ground.IsHit && ground.Normal.Z < 0f)
            {
                ground.IsHit = false;
            }
        }

        if (ground.IsHit && Stands(ground, ref to, tolerance))
        {
            Settle(ground, from, ref to);
        }
        else
        {
            Locomotion.Flags &= ~GroundedSignal;
            DropGround();
        }

        GroundIntersection.CopyFrom(ground);
        PlaceShadow(ground);
    }

    /// <summary>The body's mark goes where the ground was struck, or is lost with no ground (<c>Human_GroundCollision</c>'s end).</summary>
    private void PlaceShadow(WorldIntersection ground)
    {
        if (ground.IsHit)
        {
            Shadow.Place(ground.Position, ground.Normal, (float)DistanceToGround);
        }
        else
        {
            Shadow.Lose();
        }
    }

    /// <summary>
    /// A living human's footing from its brain's graph, <c>human.c Human_GroundCollision</c> (<c>igi2 0x4FD130</c>, its HumanAI
    /// half): no line is cast and no roof is looked for; the light is the graph's, not averaged; and the human always stands.
    /// </summary>
    /// <remarks>
    /// With no graph the footing stays empty and the human still stands, on it: retail puts its feet at height nought.
    /// </remarks>
    private void GraphGroundCollision(IIgi2GraphFooting brain, Vector3d from, ref Vector3d to)
    {
        GroundIntersection.Clear();
        DistanceToGround = 0.0;
        WorldIntersection ground = _graphFooting;
        ground.Clear();
        bool footed = brain.TryGetFooting(from, ground, out float light);
        if (footed)
        {
            Lightmap = light;
            DistanceToGround = ((Fpu)to.Z - ground.Position.Z).Value;
        }

        Settle(ground, from, ref to);
        GroundIntersection.CopyFrom(ground);
        if (footed)
        {
            // The graph's footing is the ground the mark lies on.
            Shadow.Place(ground.Position, ground.Normal.Z == 0f && ground.Normal.X == 0f && ground.Normal.Y == 0f ? new Vector3f(0f, 0f, 1f) : ground.Normal, (float)DistanceToGround);
        }
        else
        {
            Shadow.Lose();
        }
    }

    /// <summary>The material probe and the distance test: whether the human stands on what the footing struck.</summary>
    private bool Stands(WorldIntersection ground, ref Vector3d to, float tolerance)
    {
        if (ground.HitObject is { } struck)
        {
            Vector3d start = new(ground.Position.X, ground.Position.Y, ((Fpu)ground.Position.Z + MaterialProbeAbove).Value);
            _material.Starts[0] = start;
            _material.Ends[0] = new Vector3d(start.X, start.Y, ((Fpu)start.Z - MaterialProbeDepth).Value);
            WorldIntersection probe = _material.Intersections[0];
            probe.IsHit = false;
            probe.HitObject = null;
            struck.NextZ(_material);
            if (probe.IsHit)
            {
                ground.EnterMaterial = probe.EnterMaterial;
                Lightmap = ((((Fpu)probe.Light - Lightmap) * 0.5f) + Lightmap).F;
            }
        }

        double distance = ((Fpu)to.Z - ground.Position.Z).Value;
        DistanceToGround = distance;
        return distance <= tolerance;
    }

    /// <summary>Standing: normals up, the feet on the ground, hold of what is underfoot, and the slope of the step.</summary>
    private void Settle(WorldIntersection ground, Vector3d from, ref Vector3d to)
    {
        if (ground.Normal.Z < 0f)
        {
            ground.Normal = -ground.Normal;
            ground.LocalNormal = -ground.LocalNormal;
        }

        Locomotion.Flags |= GroundedSignal;

        // A dead body only rises onto the ground, it is never pulled down to it.
        if (!IsDead || ground.Position.Z > Position.Z)
        {
            to = new Vector3d(to.X, to.Y, ground.Position.Z);
        }

        if (ground.HitObject is { } struck)
        {
            SetGround(struck);
        }
        else
        {
            DropGround();
        }

        Fpu dx = (Fpu)to.X - from.X;
        Fpu dy = (Fpu)to.Y - from.Y;
        double angle = 0.0;
        if (dx.Value != 0.0 || dy.Value != 0.0)
        {
            Fpu across = Fpu.Sqrt((dx * dx) + (dy * dy));
            Fpu dz = (Fpu)to.Z - from.Z;
            Fpu length = Fpu.Sqrt((dz * dz) + (across * across));
            angle = Asin((dz / length).Value);
        }

        GroundAngle = ((Fpu)angle).F;
        GroundAngleFactor = ((Fpu)angle * QuarterTurnsPerRadian).F;
    }

    /// <summary>
    /// <c>Real64_Asin</c> (<c>igi2 0x561500</c>): clamped to one either way, then the C runtime's arcsine, which runs at
    /// the default 53-bit precision whatever the game's control word is.
    /// </summary>
    private static double Asin(double value)
    {
        double x = value < -1.0 ? -1.0 : value <= 1.0 ? value : 1.0;
        return Math.Atan2(x, Math.Sqrt((1.0 + x) * (1.0 - x)));
    }

    /// <summary>Takes hold of what is underfoot: <c>Human_SetGroundTask</c> (<c>igi2 0x502C70</c>).</summary>
    /// <remarks>
    /// Only a change of ground does anything. The rigid flag is cleared, and set again with both platform flags when
    /// the new ground is a rigid object. Graph nodes count too, once soldiers stand on them.
    /// </remarks>
    private void SetGround(IWorldCollider ground)
    {
        if (ReferenceEquals(ground, GroundCollider))
        {
            return;
        }

        GroundCollider = ground;
        IsGroundRigidObject = false;
        if (ground is Igi2RigidObject)
        {
            IsGroundRigidObject = true;
            IsUpdatePlatformPos = true;
            IsPlatformMoving = true;
        }
    }

    /// <summary>Drops hold of the ground, if any is held, and the rigid flag with it.</summary>
    protected void DropGround()
    {
        if (GroundCollider is null)
        {
            return;
        }

        GroundCollider = null;
        IsGroundRigidObject = false;
    }
}
