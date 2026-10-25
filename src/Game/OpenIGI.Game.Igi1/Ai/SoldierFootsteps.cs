using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>The soldiers' footsteps and landings: what the feet met, and which of its material's sounds is next.</summary>
/// <remarks>
/// A soldier steps through its own <c>0x45A7F0</c>, which reads the ground as the player's <c>0x463920</c> does and walks
/// the same twelve-step table with a cursor of its own; a fall that ends on the ground plays the ground's land sound.
/// </remarks>
/// <param name="context">The soldiers and the cast their steps probe.</param>
internal sealed class SoldierFootsteps(SoldierContext context)
{
    private readonly SoldierContext _context = context;

    // One cursor per soldier, so two guards walking abreast do not step in unison.
    private readonly Dictionary<SoldierView, int> _footCursors = [];

    /// <summary>Gets or sets where a resolved step is sounded, or <see langword="null"/> to make soldiers walk silently.</summary>
    public Action<Footstep>? FootstepSound { get; set; }

    /// <summary>Gets or sets where a landing's sound is played: its name and where, or <see langword="null"/> for none.</summary>
    public Action<string, Vector3d>? LandSound { get; set; }

    /// <summary>Gets or sets the material table a footstep picks its sound from.</summary>
    public GameMaterialRegistry? Materials { get; set; }

    /// <summary>The game material the level's terrain is made of, or null to fall back to <c>Ground</c>.</summary>
    /// <remarks>Guards walk on the same ground the player does; see the player camera's copy.</remarks>
    public int? TerrainGameMaterialIndex { get; set; }

    /// <summary>Plays the ground's land sound under a soldier whose fall has ended on it (<c>0x45C210</c>).</summary>
    /// <param name="view">The soldier.</param>
    public void ReportLanding(SoldierView view)
    {
        if (LandSound is { } land && TryGetGround(view, out GameMaterial material, out Vector3d where)
            && FootstepSounds.LandSound(material) is { Length: > 0 } sound)
        {
            land(sound, where);
        }
    }

    // The ground under a soldier's feet, by the original's short downward probe. The probe answers only for OBJECTS --
    // the original asks the hit task for its material, and open terrain is no task in this port -- so feet that find
    // nothing solid are on the ground, the level's terrain row or the Ground row.
    private bool TryGetGround(SoldierView view, out GameMaterial material, out Vector3d where)
    {
        material = null!;
        where = view.Position;
        if (Materials is not { } materials || _context.Cast is not { } cast)
        {
            return false;
        }

        // As the player: a face that names no walk sound is not the floor, so keep looking down.
        (Vector3d probeFrom, Vector3d probeTo) = FootstepSounds.Probe(view.Position);
        if (cast(probeFrom, probeTo, out WorldRayHit hit)
            && materials.TryGet(hit.Material, out GameMaterial struck)
            && FootstepSounds.NamesAWalk(struck))
        {
            material = struck;
            where = hit.Position;
            return true;
        }

        if (TerrainGameMaterialIndex is int terrainRow && materials.TryGet(terrainRow, out GameMaterial declared))
        {
            material = declared;
            return true;
        }

        if (materials.TryGetByName(FootstepSounds.TerrainMaterialName, out GameMaterial ground))
        {
            material = ground;
            return true;
        }

        return false;
    }

    /// <summary>Forgets a soldier's place in its material's sounds, as it dies.</summary>
    /// <param name="view">The soldier.</param>
    public void Forget(SoldierView view) => _footCursors.Remove(view);

    /// <summary>Resolves what a soldier's step landed on and hands the footstep out.</summary>
    /// <remarks>
    /// The material comes from its own short downward cast rather than from the ground query that moved
    /// the body — the original probes separately, from 0.05 m above the ground point to 0.5 m below it,
    /// which is short enough not to punch through a floor into whatever is beneath it.
    /// </remarks>
    /// <param name="view">The soldier.</param>
    /// <param name="crouched">The soldier's crouch byte, which keeps the step at <see cref="FootstepSounds.CrouchedVolumeScale"/> (<c>0x45A7F0</c>).</param>
    public void ReportFootstep(SoldierView view, bool crouched)
    {
        if (FootstepSound is not { } footstepSound || !TryGetGround(view, out GameMaterial material, out Vector3d where))
        {
            return;
        }

        // One cursor per soldier, so two guards walking abreast do not step in unison.
        _footCursors.TryGetValue(view, out int cursor);
        string sound = FootstepSounds.Next(material, ref cursor, out int slot);
        _footCursors[view] = cursor;

        if (sound.Length > 0)
        {
            // 0x45A7F0, the soldier's own step: the same twelve-step table, wet layer and pitch as the player's
            // 0x463920, and no hearing event.
            footstepSound(new Footstep(
                sound,
                where,
                FootstepSounds.HearingRadiusInUnits(material),
                slot,
                crouched ? FootstepSounds.CrouchedVolumeScale : 1f));
        }
    }
}
