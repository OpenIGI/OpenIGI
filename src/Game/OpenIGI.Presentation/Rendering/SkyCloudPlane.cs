using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// How a scrolling cloud layer is sampled: one horizontal plane above the camera, faded by view depth.
/// </summary>
/// <remarks>
/// <para>
/// Shared for the same reason the dome is. <c>IGI.exe sub_51E1F0</c> and <c>igi2ed</c>'s
/// <c>FlatSky_EnterHandler</c> (<c>0x653E10</c>) compute the same three things per vertex: the point
/// where the eye ray meets a plane <c>Z Pos</c> above the <b>camera</b>, a texture coordinate that is
/// that point times the layer's scale plus the scroll, and a fade taken from the point's view depth.
/// Even the constants match — the same <c>409.6</c> near cut-off, the same clamp at <c>0.999</c>.
/// </para>
/// <para>
/// <b>The plane is camera-relative in all three axes.</b> I.G.I. 2 makes that unmistakable: the offset
/// it feeds to the texture coordinate is the ray's, from the camera, never a world position. So a cloud
/// layer does not slide past as the player walks and does not drop as the player climbs — the only
/// thing that moves it is its own scroll. That is what makes it a sky rather than a ceiling.
/// </para>
/// <para>
/// <b>How the original tessellates it, and why this does not.</b> I.G.I. 2 builds a 26-vertex strip in
/// <em>screen</em> space: it projects the horizon, fits an oriented box to the viewport around it, and
/// walks thirteen rows across that box, unprojecting each vertex onto the plane only to obtain its
/// texture coordinate and its fade. The construction exists to clip an unbounded plane to the viewport
/// with a fixed-function pipeline. Re-emitting those unprojected points as world geometry is
/// <em>not</em> equivalent — a row that crosses the true vanishing line unprojects to infinity, which
/// costs the original nothing because it never uses the result as a position, and would throw geometry
/// across the world here. A camera-centred polar grid is bounded by construction, is clipped by the
/// hardware for free, and — because the surface is planar and the mapping affine — carries exactly the
/// same texture across it.
/// </para>
/// </remarks>
public static class SkyCloudPlane
{
    /// <summary>The depth, in engine units, the fade window is pinned to at full fog.</summary>
    /// <remarks>
    /// <c>409.60001</c> — a tenth of a metre. The same constant turns up in I.G.I. 2's sector renderer,
    /// so it reads as a shared near-plane figure rather than anything about the sky.
    /// </remarks>
    public const float NearCutoff = 409.60001f;

    /// <summary>How opaque a faded vertex is allowed to get.</summary>
    /// <remarks>Both games clamp here, which keeps the layer from ever being fully opaque.</remarks>
    public const float MaximumFade = 0.999f;

    /// <summary>Below this much fog the layer does not fade at all.</summary>
    public const float MinimumFogAmount = 0.01f;

    /// <summary>Rings of the grid, camera outwards.</summary>
    public const int RowCount = 16;

    /// <summary>Vertices around each ring.</summary>
    public const int ColumnCount = 20;

    /// <summary>How many vertices one layer's grid holds.</summary>
    public const int VertexCount = (RowCount + 1) * ColumnCount;

    /// <summary>How many indices one layer's grid draws.</summary>
    public const int IndexCount = RowCount * ColumnCount * 6;

    /// <summary>How far the grid reaches, as a multiple of the sky's <c>Distance</c>.</summary>
    /// <remarks>
    /// Past one, because <c>Distance</c> is where the fade reaches zero along the <em>view axis</em> and
    /// a point off to the side of the view axis is further away than its depth. Stopping the grid at
    /// exactly <c>Distance</c> would cut the surface off while it was still faintly visible at the edges
    /// of a wide field of view; a half again puts the rim comfortably past the vanishing point.
    /// </remarks>
    public const float RadiusFactor = 1.5f;

    /// <summary>Gets the depth at which a layer starts to fade, in engine units.</summary>
    /// <param name="distanceUnits">The sky's <c>Distance</c>, in engine units.</param>
    /// <param name="fogAmount">The sky's <c>Fog Amount</c>.</param>
    /// <returns>The near depth.</returns>
    /// <remarks>
    /// <c>distance - (distance - 409.6) × fogAmount</c>. So <c>Fog Amount</c> slides the near edge of
    /// the window between the far distance itself, at nought, and <see cref="NearCutoff"/> at one.
    /// </remarks>
    public static float NearDepthOf(float distanceUnits, float fogAmount) =>
        distanceUnits - ((distanceUnits - NearCutoff) * fogAmount);

    /// <summary>Gets how opaque the layer is at one view depth.</summary>
    /// <param name="depthUnits">The vertex's view depth, in engine units.</param>
    /// <param name="distanceUnits">The sky's <c>Distance</c>, in engine units.</param>
    /// <param name="fogAmount">The sky's <c>Fog Amount</c>.</param>
    /// <returns>The fade, nought to <see cref="MaximumFade"/>.</returns>
    /// <remarks>
    /// Opaque nearer than <see cref="NearDepthOf"/>, then falling hyperbolically to nothing at
    /// <paramref name="distanceUnits"/>. A sky with almost no fog skips the ramp outright, which is the
    /// <c>&lt; 0.01</c> branch both binaries take.
    /// </remarks>
    public static float FadeAt(float depthUnits, float distanceUnits, float fogAmount)
    {
        float near = NearDepthOf(distanceUnits, fogAmount);
        if (depthUnits < near || fogAmount < MinimumFogAmount)
        {
            return 1f;
        }

        float fade = (near - (near * distanceUnits / depthUnits)) / (near - distanceUnits);
        return float.IsFinite(fade) ? Math.Clamp(fade, 0f, MaximumFade) : 0f;
    }

    /// <summary>Advances a layer's scroll by one simulation tick.</summary>
    /// <param name="offset">Where the layer had scrolled to.</param>
    /// <param name="speed">The layer's <c>X Speed</c> or <c>Y Speed</c>.</param>
    /// <returns>The new offset, in <c>[0, 1)</c>.</returns>
    /// <remarks>
    /// <c>FlatSkyLayer_RunHandler</c> (<c>igi2ed 0x654820</c>) adds the speed once a tick and then walks
    /// the result back into <c>[0, 1)</c> a whole turn at a time. The wrap costs nothing visually —
    /// the texture repeats — and is what stops the coordinate growing without bound over a long
    /// session, which single-precision texture sampling notices well before a player would.
    /// </remarks>
    public static float AdvanceScroll(float offset, float speed)
    {
        float moved = offset + speed;
        if (!float.IsFinite(moved))
        {
            return 0f;
        }

        moved -= MathF.Floor(moved);
        return moved >= 1f ? 0f : moved;
    }

    /// <summary>Fills one layer's grid for this frame's camera.</summary>
    /// <param name="vertices">Where to write, <see cref="VertexCount"/> long.</param>
    /// <param name="layer">What the layer looks like and where its plane is.</param>
    /// <param name="forward">The camera's forward axis, which the fade measures depth along.</param>
    /// <exception cref="ArgumentException"><paramref name="vertices"/> is the wrong length.</exception>
    public static void Fill(Span<Vertex3D> vertices, in SkyCloudLayerSample layer, Vector3f forward)
    {
        if (vertices.Length != VertexCount)
        {
            throw new ArgumentException(
                $"A cloud grid holds {VertexCount} vertices, not {vertices.Length}.",
                nameof(vertices));
        }

        float maximumRadius = layer.DistanceUnits * RadiusFactor / layer.UnitsPerPosition;
        float planeHeight = layer.PlaneHeightUnits / layer.UnitsPerPosition;

        for (int row = 0; row <= RowCount; row++)
        {
            // Squared spacing keeps the rings dense overhead, where the texture is least minified and a
            // linear ramp would show its facets.
            float t = row / (float)RowCount;
            float radial = maximumRadius * t * t;

            for (int column = 0; column < ColumnCount; column++)
            {
                float azimuth = column * (MathF.Tau / ColumnCount);
                Vector3f position = new(MathF.Cos(azimuth) * radial, MathF.Sin(azimuth) * radial, planeHeight);

                float depthUnits =
                    ((forward.X * position.X) + (forward.Y * position.Y) + (forward.Z * position.Z))
                    * layer.UnitsPerPosition;

                float fade = FadeAt(depthUnits, layer.DistanceUnits, layer.FogAmount);

                float u = (position.X * layer.UnitsPerPosition * layer.TextureScale) + layer.ScrollU;
                float v = (position.Y * layer.UnitsPerPosition * layer.TextureScale) + layer.ScrollV;

                vertices[(row * ColumnCount) + column] = new Vertex3D(
                    position,
                    Vector3f.Zero,
                    u,
                    v,
                    0f,
                    0f,
                    ToColour(layer.Colour, fade * layer.Alpha));
            }
        }
    }

    /// <summary>Builds the grid's triangle list, which never changes after that.</summary>
    /// <returns>The index list, <see cref="IndexCount"/> long.</returns>
    public static ushort[] BuildIndices()
    {
        ushort[] indices = new ushort[IndexCount];
        int cursor = 0;

        for (int row = 0; row < RowCount; row++)
        {
            for (int column = 0; column < ColumnCount; column++)
            {
                int next = (column + 1) % ColumnCount;
                int a = (row * ColumnCount) + column;
                int b = (row * ColumnCount) + next;
                int c = ((row + 1) * ColumnCount) + column;
                int d = ((row + 1) * ColumnCount) + next;

                indices[cursor++] = (ushort)a;
                indices[cursor++] = (ushort)c;
                indices[cursor++] = (ushort)b;
                indices[cursor++] = (ushort)b;
                indices[cursor++] = (ushort)c;
                indices[cursor++] = (ushort)d;
            }
        }

        return indices;
    }

    private static Bgra32 ToColour(Vector3f colour, float alpha) => new(
        (byte)Math.Clamp((int)(colour.Z * 255f), 0, 255),
        (byte)Math.Clamp((int)(colour.Y * 255f), 0, 255),
        (byte)Math.Clamp((int)(colour.X * 255f), 0, 255),
        (byte)Math.Clamp((int)(alpha * 255f), 0, 255));
}

/// <summary>One cloud layer as a grid fill needs it.</summary>
/// <param name="PlaneHeightUnits">How far above the camera the plane sits, in engine units.</param>
/// <param name="DistanceUnits">Where the fade reaches nothing, in engine units.</param>
/// <param name="FogAmount">How much of the way in the fade starts.</param>
/// <param name="UnitsPerPosition">
/// Engine units per unit of the position space the caller draws in — one for a title that draws in
/// engine units, and <see cref="OpenIGI.Game.World.WorldScene.UnitsPerMetre"/> for one that draws in
/// metres. It is the only thing that differs between the two games here, and it exists because the fade
/// and the texture scale are both authored against engine units whatever the renderer uses.
/// </param>
/// <param name="TextureScale">The layer's <c>Scale</c>.</param>
/// <param name="ScrollU">How far the layer has scrolled along U.</param>
/// <param name="ScrollV">How far the layer has scrolled along V.</param>
/// <param name="Alpha">The layer's <c>Alpha</c>.</param>
/// <param name="Colour">The layer's tint.</param>
public readonly record struct SkyCloudLayerSample(
    float PlaneHeightUnits,
    float DistanceUnits,
    float FogAmount,
    float UnitsPerPosition,
    float TextureScale,
    float ScrollU,
    float ScrollV,
    float Alpha,
    Vector3f Colour);
