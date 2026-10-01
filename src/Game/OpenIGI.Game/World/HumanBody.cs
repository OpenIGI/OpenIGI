using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>A living human's collision body, as a capsule in world units.</summary>
/// <remarks>
/// <para>
/// Retail gave every registered human a body through the collision-mesh registry; the port renders
/// soldiers dynamically and never registered them there. This is the analytic stand-in: the same
/// 0.55 metre by 1.8 metre cylinder <see cref="Player.HumanSeparation"/> uses, expressed as the
/// segment <see cref="AxisA"/>..<see cref="AxisB"/> (foot to top of head) plus a
/// <see cref="Radius"/>.
/// </para>
/// <para>
/// The host re-supplies the live list every frame, so a body tracks its soldier and disappears the
/// moment the soldier dies or its conditional container goes dormant.
/// </para>
/// </remarks>
public readonly record struct HumanBody(Vector3d AxisA, Vector3d AxisB, double Radius);
