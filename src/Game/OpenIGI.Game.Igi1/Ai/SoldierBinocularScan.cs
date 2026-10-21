using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using System.Runtime.InteropServices;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>The binocular detector's scan of the soldiers: the native ten-slot overlay, and the bodies it marked.</summary>
/// <remarks>
/// The overlay is the original's (<c>FUN_00471B60</c> and the slots it fills); the contacts it marked are kept so the
/// remastered optics can glow exactly those bodies through cover without re-deriving the detector's own visibility.
/// </remarks>
/// <param name="context">The soldiers, their brains and the sight cast.</param>
internal sealed class SoldierBinocularScan(SoldierContext context)
{
    private readonly SoldierContext _context = context;

    /// <summary>Gets the detector's slots.</summary>
    public EnemyDetectorOverlay Overlay { get; } = new();

    /// <summary>Gets the soldiers the last binocular scan drew a contact for, and the team colour their marker uses.</summary>
    // Kept so the remastered optics can glow exactly those bodies through cover without re-deriving the
    // detector's own visibility, keyed by view so the outline pass can find each one's mesh and group.
    public Dictionary<SoldierView, BinocularMarkerColor> Detected { get; } = [];

    /// <summary>Gets each soldier's line of sight from the eye, as last cast on <see cref="VisibilityTick"/>.</summary>
    // LOS is a simulation fact, not a render fact. Cache it for the fixed tick so a second render
    // of the same simulation state does not cast the same three rays again.
    public Dictionary<SoldierView, bool> Visibility { get; } = [];

    /// <summary>Gets or sets the fixed tick <see cref="Visibility"/> was cast on.</summary>
    public int VisibilityTick { get; set; } = int.MinValue;

    /// <summary>The through-wall glow colour for a detector marker class.</summary>
    public static Bgra32 MarkerGlow(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => Bgra32.FromRgba(96, 220, 120, 200),
        BinocularMarkerColor.Disabled => Bgra32.FromRgba(96, 150, 255, 200),
        _ => Bgra32.FromRgba(255, 72, 72, 210),
    };

    /// <summary>Projects living, visible soldiers through the native ten-slot binocular detector.</summary>
    /// <param name="camera">The presented eye the contacts are projected through.</param>
    /// <param name="horizontalTangent">The optics' horizontal half-angle tangent (the zoom).</param>
    /// <param name="screenWidth">The back buffer width in pixels.</param>
    /// <param name="screenHeight">The back buffer height in pixels.</param>
    /// <param name="tick">The millisecond tick driving slot lifetime and the entry pulse.</param>
    /// <param name="maxDetectionMetres">
    /// The furthest a contact may be and still be marked, in metres of forward distance. The remastered
    /// optics pass a finite range; the vanilla optics pass positive infinity, staying retail-uncapped.
    /// </param>
    /// <param name="simulationTick">
    /// The fixed simulation tick used to cache line-of-sight results. Leave it at its default when
    /// calling this method from a one-off diagnostic projection.
    /// </param>
    public IReadOnlyList<EnemyOverlayBox> BinocularTargets(
        Camera3D camera,
        float horizontalTangent,
        int screenWidth,
        int screenHeight,
        int tick,
        float maxDetectionMetres,
        int simulationTick = int.MinValue)
    {
        if (horizontalTangent <= 0f)
        {
            Overlay.Clear();
            return [];
        }

        if (simulationTick != int.MinValue && VisibilityTick != simulationTick)
        {
            Visibility.Clear();
            VisibilityTick = simulationTick;
        }

        Overlay.BeginScan();
        Detected.Clear();
        float verticalTangent = horizontalTangent * screenHeight / Math.Max(1f, screenWidth);
        Vector3d eyeUnits = new(
            camera.Position.X * WorldScene.UnitsPerMetre,
            camera.Position.Y * WorldScene.UnitsPerMetre,
            camera.Position.Z * WorldScene.UnitsPerMetre);

        foreach ((SoldierView view, Igi1HumanAi brain) in _context.Brains)
        {
            if (view.IsDead || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            // FUN_00470F70 sizes the marker as projectionScale * Human+0xA0 / depth, where Human+0xA0
            // is the model's authored MESH+0x78 bound. For the soldier LODs that value is ~0.94 m --
            // a half-body radius -- so the rectangle only frames the body when it is centred on the
            // model's own origin, which sits that same ~0.94 m above the grounded feet. view.Position
            // is the grounded foot contact, so lift the marker centre by the bound to reach the retail
            // body origin; leaving it at the feet drew the box half into the ground, over the shins.
            if (!_context.DetectorMarkerScaleMetres.TryGetValue(view, out float markerScaleMetres)
                || markerScaleMetres <= 0f)
            {
                continue;
            }

            Vector3d feetUnits = view.Position;
            double bodyCentreUnits = markerScaleMetres * WorldScene.UnitsPerMetre;
            Vector3d targetUnits = new(feetUnits.X, feetUnits.Y, feetUnits.Z + bodyCentreUnits);
            Vector3d delta = new(
                targetUnits.X / WorldScene.UnitsPerMetre - camera.Position.X,
                targetUnits.Y / WorldScene.UnitsPerMetre - camera.Position.Y,
                targetUnits.Z / WorldScene.UnitsPerMetre - camera.Position.Z);
            Vector3d viewPosition = camera.Orientation.Transposed().Transform(delta);
            if (viewPosition.Z <= 0f)
            {
                continue;
            }

            // The remastered detector has a maximum range: a contact past it is not marked at all.
            // Measured on the forward depth, which is the distance the marker label reports, so the cap
            // is the number the player reads. Skipped here, before the occlusion sweep, so a distant
            // soldier costs nothing.
            if (viewPosition.Z > maxDetectionMetres)
            {
                continue;
            }

            float screenX = screenWidth * 0.5f
                + (float)(viewPosition.X / (viewPosition.Z * horizontalTangent) * screenWidth * 0.5f);
            float screenY = screenHeight * 0.5f
                - (float)(viewPosition.Y / (viewPosition.Z * verticalTangent) * screenHeight * 0.5f);
            if (screenX < -8f || screenX > screenWidth + 8f || screenY < -8f || screenY > screenHeight + 8f)
            {
                continue;
            }

            // FUN_00471B60 gates a contact on FUN_004D03D0, a view-frustum cull that the in-front and
            // on-screen tests above already reproduce. The retail engine then hides fully-occluded
            // humans through its sector/portal visibility, which this reimplementation lacks. Approximate
            // that with a line-of-sight sweep: probe the feet, the body origin and the head, and keep the
            // contact when ANY of them is unobstructed. A guard whose upper body clears a low container is
            // still marked; one standing completely behind a wall is dropped, as the original drops it.
            if (_context.Sight is { } lineOfSight)
            {
                bool anyVisible;
                if (simulationTick != int.MinValue
                    && Visibility.TryGetValue(view, out bool cachedVisibility))
                {
                    anyVisible = cachedVisibility;
                }
                else
                {
                    Vector3d headUnits = new(feetUnits.X, feetUnits.Y, feetUnits.Z + (2d * bodyCentreUnits));
                    anyVisible =
                        SoldierSightQueries.HasLineOfSight(lineOfSight, eyeUnits, targetUnits)
                        || SoldierSightQueries.HasLineOfSight(lineOfSight, eyeUnits, headUnits)
                        || SoldierSightQueries.HasLineOfSight(lineOfSight, eyeUnits, feetUnits);
                    if (simulationTick != int.MinValue)
                    {
                        Visibility[view] = anyVisible;
                    }
                }
                if (!anyVisible)
                {
                    continue;
                }
            }

            // FUN_00470EC0 writes the transformed depth into the value passed to FUN_00471F60;
            // the retail slot's distance is view-space Z, not Euclidean eye-to-target distance.
            float distance = (float)viewPosition.Z;

            float boxWidth = (float)(markerScaleMetres * screenWidth
                / (Math.Max(0.001, viewPosition.Z) * horizontalTangent));
            float boxHeight = (float)(markerScaleMetres * screenHeight
                / (Math.Max(0.001, viewPosition.Z) * verticalTangent));

            // FUN_00471B60 passes the target's colour flags to FUN_00471F60: extraParam1 is the
            // green flag (Team != 1, Human+0x252) and extraParam2 is the blue flag (the +0x3317
            // non-collidable byte). Live scanned soldiers are collidable, so extraParam2 stays 0;
            // team-1 guards fall through to the retail hostile-red rectangle.
            Overlay.Upsert(
                brain.RuntimeId,
                distance,
                configFlag: 1,
                screenX,
                screenY,
                eventParam: 0,
                extraParam1: view.Soldier.Team != 1 ? 1 : 0,
                extraParam2: 0,
                tick,
                boxWidth,
                boxHeight);

            // Record the contact for the through-wall glow, in the same team colour its marker uses.
            Detected[view] = view.Soldier.Team != 1
                ? BinocularMarkerColor.Team
                : BinocularMarkerColor.Hostile;
        }

        Overlay.ExpireInactive(tick);
        return Overlay.OverlayBoxes(tick);
    }
}
