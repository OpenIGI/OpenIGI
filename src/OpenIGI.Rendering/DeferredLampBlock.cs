namespace OpenIGI.Rendering;

/// <summary>What a frame's lamps do, beyond where they are.</summary>
/// <param name="FrameCounter">Which frame this is, so the shadow's sample points move.</param>
/// <param name="ScreenMarch">
/// Whether the shading marches the depth buffer to occlude each lamp. On wherever lamp shadows are
/// asked for at all: unlike the trace, this needs no ray query, so it is what keeps a lamp from lighting
/// through a wall on an adapter that cannot trace, and what shadows the second lamp on a pixel the
/// traced reservoir's one ray could not reach.
/// </param>
/// <param name="Traced">
/// Whether the shading traces a ray per lamp against the world — the off-screen half of the shadow.
/// Needs an adapter that can trace, the switch to be on, and a world built to trace against.
/// </param>
/// <param name="MaxSourceRadius">The largest source a shadow ray may sample, whatever a fixture measures.</param>
public readonly record struct DeferredLampState(
    ulong FrameCounter = 0,
    bool ScreenMarch = false,
    bool Traced = false,
    float MaxSourceRadius = 0.02f);

/// <summary>The level's local lights, as the deferred shading reads them.</summary>
/// <remarks>
/// Uploaded whole every frame, count included, so a frame that set none lights from none — which is why
/// neither backend needs a way to say "clear the lamps". The layout mirrors the shader's block exactly:
/// a control vector, then three parallel arrays rather than an array of structs, because a
/// <c>vec4</c> array is the one layout whose std140 padding is the same on every implementation.
/// </remarks>
public static class DeferredLampBlock
{
    /// <summary>How many floats the block holds.</summary>
    public const int Floats = DeferredLightingBlocks.LampBytes / sizeof(float);

    /// <summary>Where the positions start, in floats: after the two control vectors.</summary>
    public const int PositionOffset = 8;

    /// <summary>Where the colours start.</summary>
    public const int ColourOffset = PositionOffset + (DeferredLocalLight.Maximum * 4);

    /// <summary>Where the directions start.</summary>
    public const int DirectionOffset = ColourOffset + (DeferredLocalLight.Maximum * 4);

    /// <summary>Where the emitting rectangles start: xyz the long axis at half length, w the half width.</summary>
    public const int RectOffset = DirectionOffset + (DeferredLocalLight.Maximum * 4);

    /// <summary>Writes the block.</summary>
    /// <param name="block">Room for <see cref="Floats"/> floats.</param>
    /// <param name="state">What the frame's lamps do.</param>
    /// <param name="lights">The lights themselves, in view space; a longer list is truncated.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void Write(
        Span<float> block,
        in DeferredLampState state,
        ReadOnlySpan<DeferredLocalLight> lights)
    {
        if (block.Length != Floats)
        {
            throw new ArgumentException($"The lamp block is {Floats} floats, not {block.Length}.", nameof(block));
        }

        block.Clear();

        int count = Math.Min(lights.Length, DeferredLocalLight.Maximum);

        block[0] = count;

        // Both of these ride on there being a lamp at all: with none, there is nothing to occlude and
        // nothing to trace.
        block[1] = count > 0 && state.Traced ? 1f : 0f;

        // Wrapped short — the dither only needs the pattern to differ from the last frame's, not to
        // never repeat.
        block[2] = state.FrameCounter % 64UL;
        block[3] = count > 0 && state.ScreenMarch ? 1f : 0f;
        block[4] = MathF.Max(state.MaxSourceRadius, 0.02f);

        for (int i = 0; i < count; i++)
        {
            DeferredLocalLight light = lights[i];
            int at = i * 4;

            block[PositionOffset + at] = light.ViewPosition.X;
            block[PositionOffset + at + 1] = light.ViewPosition.Y;
            block[PositionOffset + at + 2] = light.ViewPosition.Z;
            block[PositionOffset + at + 3] = light.Radius;

            block[ColourOffset + at] = light.Colour.X;
            block[ColourOffset + at + 1] = light.Colour.Y;
            block[ColourOffset + at + 2] = light.Colour.Z;
            block[ColourOffset + at + 3] = light.Intensity;

            block[DirectionOffset + at] = light.ViewDirection.X;
            block[DirectionOffset + at + 1] = light.ViewDirection.Y;
            block[DirectionOffset + at + 2] = light.ViewDirection.Z;

            // How big the fixture is, which is what gives its shadow a penumbra. Rides beside the facing
            // because both describe the SOURCE rather than its falloff, and the fourth component there
            // was spare.
            block[DirectionOffset + at + 3] = light.SourceRadius;

            block[RectOffset + at] = light.ViewAxis.X;
            block[RectOffset + at + 1] = light.ViewAxis.Y;
            block[RectOffset + at + 2] = light.ViewAxis.Z;
            block[RectOffset + at + 3] = light.HalfWidth;
        }
    }
}
