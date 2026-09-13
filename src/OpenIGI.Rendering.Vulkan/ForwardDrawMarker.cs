namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The one number a forward draw carries that says what it <em>is</em>: the world, the first-person rig,
/// or something translucent — and, for the rig, whether there is a light to key it with.
/// </summary>
/// <remarks>
/// <para>
/// It rides the first component of the mesh push block and does two jobs, which is why it is a number
/// rather than a flag. The fragment stage branches on it to choose the shading, and an opaque draw
/// leaves it in the lit target's alpha so that a later pass reading that colour back can tell world
/// geometry from geometry that only exists in front of the eye — the eye adaptation must not meter the
/// arms, and the indirect gather must not bounce light off them.
/// </para>
/// <para>
/// <see cref="UnlitFirstPerson"/> is the fourth value and the newest. The studio key light the rig is
/// shaded with is a remaster light: the original draws the first-person layer through the same
/// fixed-function lighting as every other model (<c>sub_49F700</c> installs the layer's own view block
/// and hands the mesh to <c>sub_49B700</c>, which writes position, normal and UV and no colour at all).
/// Under <see cref="LightingModel.Baked"/> there is nothing to key with — <c>SceneLighting.Baked</c> is a
/// black sun over a black ambient, on purpose — and the rig came out as a solid black silhouette in the
/// corner of every vanilla frame. It is still the rig, so it still leaves the rig's zero in the alpha;
/// what the extra value buys is the shading branch.
/// </para>
/// </remarks>
internal static class ForwardDrawMarker
{
    /// <summary>Anything not opaque: the draw keeps its own alpha, because the blend reads it.</summary>
    public const float Translucent = -1f;

    /// <summary>The first-person rig, shaded by the remaster's studio key.</summary>
    public const float LitFirstPerson = 0f;

    /// <summary>Ordinary world geometry.</summary>
    public const float World = 1f;

    /// <summary>The first-person rig, shaded as ordinary geometry because no dynamic light is set.</summary>
    public const float UnlitFirstPerson = 2f;

    /// <summary>Picks the marker for a draw.</summary>
    /// <param name="blend">How the draw is blended.</param>
    /// <param name="firstPerson">Whether the draw is bracketed as first-person geometry.</param>
    /// <param name="lighting">The lighting model in force.</param>
    /// <returns>One of the four markers.</returns>
    public static float For(BlendMode blend, bool firstPerson, LightingModel lighting)
    {
        if (blend != BlendMode.Opaque)
        {
            return Translucent;
        }

        if (!firstPerson)
        {
            return World;
        }

        return lighting == LightingModel.Baked ? UnlitFirstPerson : LitFirstPerson;
    }

    /// <summary>Determines whether a marker names the draw the studio key light is for.</summary>
    /// <param name="marker">The marker <see cref="For"/> produced.</param>
    /// <returns><see langword="true"/> only for <see cref="LitFirstPerson"/>.</returns>
    public static bool IsStudioRig(float marker) => marker == LitFirstPerson;

    /// <summary>Gets what a draw leaves in the lit target's alpha, which is not always its marker.</summary>
    /// <param name="marker">The marker <see cref="For"/> produced.</param>
    /// <returns>
    /// The marker itself, except for <see cref="UnlitFirstPerson"/> — a shading instruction, whose alpha
    /// is the rig's own <see cref="LitFirstPerson"/> zero. The fragment stage applies the same rule.
    /// </returns>
    public static float AlphaFor(float marker) => marker == UnlitFirstPerson ? LitFirstPerson : marker;
}
