namespace OpenIGI.Game.World;

/// <summary>
/// A part of a level whose state play changes, which the rounds of a multiplayer match put back as the level stood when the
/// match began -- without loading anything: the meshes, textures and collision stay, only what play moved, took, broke or set
/// goes back.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MarkLevelStart"/> records the part as it stands, once, as the first round begins (after the level is up and set
/// out as an arena); <see cref="ResetToLevelStart"/> puts back exactly that. A reset marks what it moved as changed, so the
/// next tick's own path re-places it and its owner's hooks (collision, sight, sound) catch up as they do for any move; a part
/// whose tick carries only that tick's step (a lift and its riders) moves its collision at once instead.
/// </para>
/// <para>
/// A part whose state is only data the level was loaded with -- weapon tables, materials -- has nothing to reset and does not
/// take this. <see cref="MissionState"/> resets every part it holds, and a test fails for a part it holds that neither takes
/// this nor is listed as data, so a new system cannot be forgotten.
/// </para>
/// </remarks>
public interface ILevelReset
{
    /// <summary>Records this part as it stands now, as what <see cref="ResetToLevelStart"/> puts back.</summary>
    void MarkLevelStart();

    /// <summary>Puts this part back as <see cref="MarkLevelStart"/> found it.</summary>
    /// <param name="solids">The level's collision, for a part whose jump back has to move it at once; <see langword="null"/> without.</param>
    void ResetToLevelStart(ObjectCollisionIndex? solids);
}
