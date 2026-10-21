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

/// <summary>The soldiers' footsteps: what each step landed on, which of its material's sounds is next, and who hears it.</summary>
/// <remarks>
/// <c>0x463920</c> is one handler for every human, so a guard's step is resolved exactly as the player's is; only the
/// cursor through the material's sounds is the soldier's own.
/// </remarks>
/// <param name="context">The soldiers and the cast their steps probe.</param>
internal sealed class SoldierFootsteps(SoldierContext context)
{
    private readonly SoldierContext _context = context;

    // One cursor per soldier, so two guards walking abreast do not step in unison.
    private readonly Dictionary<SoldierView, int> _footCursors = [];

    /// <summary>Gets or sets where a resolved step is sounded, or <see langword="null"/> to make soldiers walk silently.</summary>
    public Action<Footstep>? FootstepSound { get; set; }

    /// <summary>Gets or sets the material table a footstep picks its sound from.</summary>
    public GameMaterialRegistry? Materials { get; set; }

    /// <summary>The game material the level's terrain is made of, or null to fall back to <c>Ground</c>.</summary>
    /// <remarks>Guards walk on the same ground the player does; see the player camera's copy.</remarks>
    public int? TerrainGameMaterialIndex { get; set; }

    /// <summary>Forgets a soldier's place in its material's sounds, as it dies.</summary>
    /// <param name="view">The soldier.</param>
    public void Forget(SoldierView view) => _footCursors.Remove(view);

    /// <summary>Resolves what a soldier's step landed on and hands the footstep out.</summary>
    /// <remarks>
    /// The material comes from its own short downward cast rather than from the ground query that moved
    /// the body — the original probes separately, from 0.05 m above the ground point to 0.5 m below it,
    /// which is short enough not to punch through a floor into whatever is beneath it.
    /// </remarks>
    public void ReportFootstep(SoldierView view)
    {
        if (FootstepSound is not { } footstepSound || Materials is not { } materials || _context.Cast is not { } cast)
        {
            return;
        }

        // The probe answers only for OBJECTS -- the original asks the hit task for its material, and
        // open terrain is no task in this port. A step that finds nothing solid landed on the ground,
        // so it takes the Ground row; see FootstepSounds.TerrainMaterialName.
        (Vector3d probeFrom, Vector3d probeTo) = FootstepSounds.Probe(view.Position);
        Vector3d where = view.Position;
        GameMaterial? material;

        // As the player: a face that names no walk sound is not the floor, so keep looking down.
        if (cast(probeFrom, probeTo, out WorldRayHit hit)
            && materials.TryGet(hit.Material, out GameMaterial struck)
            && FootstepSounds.NamesAWalk(struck))
        {
            material = struck;
            where = hit.Position;
        }
        else if (TerrainGameMaterialIndex is int terrainRow && materials.TryGet(terrainRow, out GameMaterial declared))
        {
            material = declared;
        }
        else if (!materials.TryGetByName(FootstepSounds.TerrainMaterialName, out GameMaterial ground))
        {
            return;
        }
        else
        {
            material = ground;
        }

        // One cursor per soldier, so two guards walking abreast do not step in unison.
        _footCursors.TryGetValue(view, out int cursor);
        string sound = FootstepSounds.Next(material, ref cursor, out int slot);
        _footCursors[view] = cursor;

        if (sound.Length > 0)
        {
            // 0x463920 is one handler for every human, so a guard walking in the rain lays down the
            // same wet layer the player does. The slot is what names it.
            footstepSound(new Footstep(
                sound,
                where,
                FootstepSounds.HearingRadiusInUnits(material),
                slot));
        }
    }
}
