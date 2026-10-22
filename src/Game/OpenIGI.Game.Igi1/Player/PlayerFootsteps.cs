using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The sounds the player's feet make: the footstep each locomotion clip's event 1010 takes, the land
/// sound of an airborne state's contact, and the material under the feet both are chosen by.
/// </summary>
internal sealed class PlayerFootsteps
{
    private readonly PlayerBody _body;
    private int _footCursor;

    /// <summary>Initializes the footsteps for one body.</summary>
    /// <param name="body">The body whose feet are probed.</param>
    internal PlayerFootsteps(PlayerBody body)
    {
        _body = body;
    }

    /// <summary>Gets or sets the level's placed objects, which the ground probe casts against.</summary>
    internal ObjectCollisionIndex? Walls { get; set; }

    /// <summary>Gets or sets the game material table the struck faces name rows of.</summary>
    internal GameMaterialRegistry? Materials { get; set; }

    /// <summary>The game material the level's terrain is made of, or null to fall back to <c>Ground</c>.</summary>
    /// <remarks>
    /// Read from the level's own terrain materials — see
    /// <see cref="FootstepSounds.TerrainGameMaterialIndex"/>. It is <c>Snow</c> in the four Arctic
    /// missions and <c>Ground</c> everywhere else, which is the difference between crunching through
    /// snow and treading on earth.
    /// </remarks>
    internal int? TerrainGameMaterialIndex { get; set; }

    /// <summary>The footstep the player's locomotion clip took this tick, if it took one.</summary>
    /// <param name="animator">The player's locomotion animator, or <see langword="null"/> without one.</param>
    /// <returns>The footstep, or <see langword="null"/> when no step landed.</returns>
    /// <remarks>
    /// <para>
    /// The player's rig is <see cref="HumanLocomotionAnimator"/>, not the <c>BoneClip</c> a soldier
    /// walks on, so the event has to be asked of the animator — but it is the same clip carrying the
    /// same event 1010, and the animator now tests the same tick interval a <c>BoneClip</c> does.
    /// </para>
    /// <para>
    /// The material comes from its own short downward cast, as the original's does, and a step taken in
    /// mid-air is not a step at all.
    /// </para>
    /// </remarks>
    internal Footstep? TakeFootstep(HumanLocomotionAnimator? animator)
    {
        if (animator is null
            || Materials is null
            || !_body.IsGrounded
            || !animator.CrossedEvent(FootstepSounds.EventId))
        {
            return null;
        }

        if (!TryGetGroundMaterial(out GameMaterial material, out Vector3d where))
        {
            return null;
        }

        string sound = FootstepSounds.Next(material, ref _footCursor, out int slot);
        if (sound.Length == 0)
        {
            return null;
        }

        // The crouch byte does two things in 0x463920: it drops the voice to two thirds and it skips
        // the AI hearing event. A zero radius carries the second half to the caller.
        bool crouched = _body.IsCrouched;
        return new Footstep(
            sound,
            where,
            crouched ? 0.0 : FootstepSounds.HearingRadiusInUnits(material),
            slot,
            crouched ? FootstepSounds.CrouchedVolumeScale : 1f);
    }

    /// <summary>The sound the ground makes when the player lands on it, or null when it names none.</summary>
    /// <returns>The definition name and where to play it, in engine units.</returns>
    /// <remarks>
    /// Both airborne states play the struck material's land sound on contact — <c>0x40BE40</c> for a
    /// fall and <c>0x40BBD0</c> for a jump — reading the same <c>Human+1772</c> the footstep handler
    /// reads. It is separate from, and additional to, the <c>player_fall_N</c> that a damaging drop
    /// plays out of <c>0x408460</c>.
    /// </remarks>
    internal (string Sound, Vector3d PositionInUnits)? TakeLandingSound()
    {
        if (!TryGetGroundMaterial(out GameMaterial material, out Vector3d where))
        {
            return null;
        }

        string sound = FootstepSounds.LandSound(material);
        return sound.Length == 0 ? null : (sound, where);
    }

    /// <summary>Finds the material under the feet with the original's own short downward probe.</summary>
    /// <param name="material">Receives the material.</param>
    /// <param name="where">Receives the ground contact point, in engine units.</param>
    /// <returns><see langword="false"/> when there is no material table, or no row to fall back to.</returns>
    /// <remarks>
    /// The probe answers only for OBJECTS. A step that finds nothing solid landed on open terrain,
    /// which the original resolves by asking the terrain's own task -- something this port has no
    /// equivalent of, so it takes the Ground row by name instead.
    /// </remarks>
    private bool TryGetGroundMaterial(out GameMaterial material, out Vector3d where)
    {
        (Vector3d from, Vector3d to) = FootstepSounds.Probe(_body.Position);
        where = _body.Position;

        if (Materials is not { } materials)
        {
            material = null!;
            return false;
        }

        // The SURFACE cast, not the body's. The player moves against collision level one -- the
        // human-collision hull, every face of which is material 21 and names no walk sound at all --
        // so probing with it found a floor made of nothing and left every step on placed geometry
        // silent. The general cast is the geometry bullets hit, and its faces carry the real materials:
        // wood, concrete, metal, gravel. The guards were already using it.
        //
        // A struck face still only answers if it names something to walk on; see FootstepSounds.NamesAWalk.
        if (Walls?.Caster() is { } walls
            && walls(from, to, out WorldRayHit hit)
            && materials.TryGet(hit.Material, out GameMaterial struck)
            && FootstepSounds.NamesAWalk(struck))
        {
            material = struck;
            where = hit.Position;
            return true;
        }

        // Open terrain: the level says what its ground is made of, exactly as a face does.
        if (TerrainGameMaterialIndex is int terrain && materials.TryGet(terrain, out material))
        {
            return true;
        }

        return materials.TryGetByName(FootstepSounds.TerrainMaterialName, out material);
    }
}
