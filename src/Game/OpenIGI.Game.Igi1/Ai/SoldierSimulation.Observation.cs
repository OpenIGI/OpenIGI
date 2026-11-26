using OpenIGI.Core.Graphics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

public sealed partial class SoldierSimulation
{
    /// <summary>
    /// Looks at the soldiers from where the original's camera stood this tick, and marks each as that render would have:
    /// whether its body was drawn (<see cref="SoldierAgent.DrawnLastRender"/>: a dead body not drawn may leave the world) and
    /// whether its eye was re-skinned (<see cref="SoldierAgent.InView"/>: the eye the next tick's sight looks from).
    /// </summary>
    /// <param name="eye">The camera, in metres: the player's eye, or the cutscene's.</param>
    /// <param name="tangentHalfFovX">The tangent of half its horizontal angle: one for the walking eye, narrower in a zoomed sight.</param>
    /// <param name="cull">The level's sector cull, or <see langword="null"/> for a level without one.</param>
    /// <remarks>
    /// <para>
    /// The original stamps both as it draws (<c>Human_DrawHandler</c>, <c>0x45F280</c>, and the camera pass <c>0x45F140</c>),
    /// once a tick, so what it drew is part of how the level plays. The port draws at whatever rate the screen runs, from a
    /// camera that may be smoothed, over the shoulder, a free one or none at all on a server; reading those would make the
    /// level play by how it was watched. So the simulation makes the original's render itself, once a tick: from the original's
    /// camera, at its 4:3 aspect, through the same sector cull -- the same answer however the level is drawn, or whether it
    /// is.
    /// </para>
    /// </remarks>
    public void ObserveFrom(in Camera3D eye, float tangentHalfFovX, Igi1SectorVisibility? cull)
    {
        float tanX = tangentHalfFovX > 0f && float.IsFinite(tangentHalfFovX) ? tangentHalfFovX : 1f;
        float tanY = tanX * 0.75f;
        cull?.BeginView(eye, tanX, tanY);
        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent agent in group.Agents)
            {
                SoldierView view = agent.View;
                Core.Numerics.Vector3d metres = WorldScene.ToMetres(view.Position);
                agent.DrawnLastRender = ConditionalContainerGate.IsTaskActive(view.Soldier)
                    && (cull is null || SoldierViewBounds.IsInSector(cull, group.Body, view.Position, view.Orientation))
                    && SoldierViewBounds.IsInFrustum(eye, metres, tanX, tanY);
                agent.InView = agent.DrawnLastRender
                    && Core.Numerics.Vector3d.Distance(metres, eye.Position) <= SoldierViewBounds.EyeRefreshDistanceMetres;
            }
        }
    }
}
