namespace OpenIGI.Audio;

/// <summary>The mixing bus a sound belongs to.</summary>
/// <remarks>
/// <para>
/// The original has three user-facing volumes — effects, music and speech — stored per player profile
/// and applied as plain gain multipliers inside the voice update <c>0x496E60</c>: a flag bit on the
/// voice selects between one global (<c>0x54390C</c>) and the product of two (<c>0x543908</c> ×
/// <c>0x543904</c>).
/// </para>
/// <para>
/// <see cref="Ambience"/> has no counterpart in the original, which mixes the level's beds in with the
/// effects. It is split out because the enhanced mix ducks and limits the buses separately, and folding
/// a continuous bed into the same bus as gunfire makes both worse. Under the vanilla profile it takes
/// the effects gain, so the split is inaudible there.
/// </para>
/// </remarks>
public enum SoundCategory
{
    /// <summary>Weapons, footsteps, doors, impacts — everything the original calls a sound effect.</summary>
    Effects,

    /// <summary>The mission score and the menu theme.</summary>
    Music,

    /// <summary>Voice lines, including the ones a subtitle transcribes.</summary>
    Speech,

    /// <summary>Continuous level beds. Folded into <see cref="Effects"/> under the vanilla profile.</summary>
    Ambience,
}
