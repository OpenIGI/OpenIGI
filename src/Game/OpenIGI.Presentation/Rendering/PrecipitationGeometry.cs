using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Builds the storm's quads into <see cref="StormRenderer"/>'s one vertex buffer: the far sheets,
/// the splashes, then the drops, each a contiguous run so the whole storm is three draws.
/// </summary>
/// <remarks>
/// Reads the live particles from <see cref="PrecipitationSimulation"/> and writes camera-relative
/// vertices; it holds no state of its own between frames.
/// </remarks>
internal sealed class PrecipitationGeometry
{
    private const float TicksPerSecond = 30f;

    // The fill the generated sheet is built to: a sharp streak covers about thirty-nine hundredths of its
    // cell's width and nine tenths of its height, so a quad sized straight from DropWidthMetres would draw
    // a streak two and a half times thinner than the tuning asks for. Dividing the quad by the fill is
    // what keeps DropWidthMetres meaning the width of the streak.
    private const float FocusedStreakFill = 0.39f;
    private const float StreakLengthFill = 0.9f;

    // The defocused half is drawn wider inside the same cell, which is the whole of the defocus: same quad,
    // same drop, a streak that comes out about two and a half times fatter because the sheet says so.
    // Nothing here widens the geometry to fake it.

    // How much of the ambient light a drop still adds over what it refracts: the faint sheen of the sky on
    // its surface. The rest of what made every drop a white scratch is now the scene behind it.
    private const float AmbientGlint = 0.12f;

    private readonly PrecipitationSimulation _simulation;

    /// <summary>Initializes the builder over the simulation whose particles it draws.</summary>
    /// <param name="simulation">The storm's particle state.</param>
    internal PrecipitationGeometry(PrecipitationSimulation simulation) => _simulation = simulation;

    /// <summary>The light a particle is seen by: the frame's sky fill, desaturated and scaled.</summary>
    /// <remarks>
    /// <para>
    /// One frame's worth, so it is computed once and handed to every particle rather than recomputed per
    /// quad. What it stands in for, and why it is not the frame itself, is in the type's own remarks.
    /// </para>
    /// <para>
    /// <b>It multiplies the particle's colour; it is not added to it.</b> Adding a desaturated tap of the
    /// light behind a drop to its emissive while also lighting its base colour comes out, over alpha
    /// blending, very close to simply adding the lit colour, because the ambient term all but cancels the
    /// attenuation the blend applies. So the drops are drawn additively and carry the lit colour, which is
    /// one multiply instead of two terms and a blend, and lands in the same place.
    /// </para>
    /// </remarks>
    // The average of max(n.l, 0) over a sphere: how much of a light a small round body facing every way
    // takes, against a surface turned square to it.
    private const float SphereAverageCosine = 0.25f;

    /// <summary>The fill a drop is lit by, raised to the veil where the scene gives it less.</summary>
    /// <remarks>Raised in brightness and not in hue, so a blue night leaves blue streaks.</remarks>
    private static Vector3f Veiled(in Vector3f fill, float veil)
    {
        float luma = (fill.X * 0.2126f) + (fill.Y * 0.7152f) + (fill.Z * 0.0722f);

        if (!(veil > luma))
        {
            return fill;
        }

        return luma > 1e-6f ? fill * (veil / luma) : new Vector3f(veil, veil, veil);
    }

    private static (float R, float G, float B) Ambient(PrecipitationSettings s, in Vector3f ambientColour)
    {
        float luma =
            (ambientColour.X * 0.212639f) + (ambientColour.Y * 0.715169f) + (ambientColour.Z * 0.072192f);
        float greying = Math.Clamp(1f - s.AmbientSaturation, 0f, 1f);
        float scale = MathF.Max(0f, s.AmbientIntensity);
        return (
            (ambientColour.X + ((luma - ambientColour.X) * greying)) * scale,
            (ambientColour.Y + ((luma - ambientColour.Y) * greying)) * scale,
            (ambientColour.Z + ((luma - ambientColour.Z) * greying)) * scale);
    }

    /// <summary>The faint cool cast of a drop, which refracts mostly sky.</summary>
    private static readonly (float R, float G, float B) WaterTint = (0.80f, 0.90f, 1f);

    /// <summary>A drop's added light, rolled off rather than clipped: bright light approaches white, never sits on it.</summary>
    private static float WaterLevel(float light)
    {
        float level = MathF.Max(light, 0f);
        return Math.Clamp(level / (0.85f + level), 0f, 1f);
    }

    /// <summary>A repeatable number in [0, 1) for a drop, so its brightness does not flicker frame to frame.</summary>
    private static float Hash01(float seed)
    {
        float value = MathF.Sin(seed * 12.9898f) * 43758.5453f;
        return value - MathF.Floor(value);
    }

    /// <summary>Builds every visible drop's streak, lit by the frame's light and lamps.</summary>
    /// <returns>How many quads were written.</returns>
    internal int BuildDrops(
        Vertex3D[] vertices,
        PrecipitationSettings s,
        float sheetSplitV,
        Vector3d eye,
        int firstQuad,
        float interpolation,
        in Vector3f sunDirection,
        in Vector3f sunColour,
        in Vector3f ambientColour,
        float seconds,
        ReadOnlySpan<StormLamp> lamps,
        float veil)
    {
        float alphaScale = Math.Clamp(s.Alpha, 0f, 1f) * Math.Clamp(s.Intensity, 0f, 1f);

        // The gust field: on only when asked for and given a size it can be sampled at.
        float gustAmount = Math.Clamp(s.GustAmount, 0f, 1f);
        bool gusting = gustAmount > 0f && s.GustSizeMetres > 0.01f;
        float gustScale = gusting ? 1f / s.GustSizeMetres : 0f;
        float gustTravel = gusting ? seconds * s.GustMetresPerSecond * gustScale : 0f;

        float blend = Math.Clamp(interpolation, 0f, 1f);
        float nearFade = MathF.Max(0.01f, s.NearFadeMetres);
        float cull = nearFade * 0.35f;

        // Nearer than this, a drop is drawn from the defocused strip; nought draws every drop sharp.
        float focusDistance = MathF.Max(0f, s.FocusDistanceMetres);
        Vector3f sun = sunDirection.NormalizedOr(-Vector3f.UnitZ, 1e-6f);

        // Never darker than the veil. A drop refracts the light around it, so in an even light it is as
        // bright as what is behind it and all but disappears -- which is what a night lit only by its sky
        // does to rain, and the eye, adapted to that night, still sees it as a faint veil. The floor is
        // set in what the frame is exposed to (the caller divides a display level by the exposure), so it
        // is nothing by day, when the sky outshines it many times over, and a dim grey streak by night.
        (float lightR, float lightG, float lightB) = Ambient(s, Veiled(ambientColour, veil));

        float baseR = (s.Colour.R * lightR) + s.EmissiveIntensity;
        float baseG = (s.Colour.G * lightG) + s.EmissiveIntensity;
        float baseB = (s.Colour.B * lightB) + s.EmissiveIntensity;

        int quads = 0;
        for (int i = 0; i < _simulation.LiveDrops; i++)
        {
            ref PrecipitationDrop drop = ref _simulation.Drops[i];

            double px = drop.Previous.X + ((drop.Position.X - drop.Previous.X) * blend);
            double py = drop.Previous.Y + ((drop.Position.Y - drop.Previous.Y) * blend);
            double pz = drop.Previous.Z + ((drop.Position.Z - drop.Previous.Z) * blend);

            float lx = (float)(px - eye.X);
            float ly = (float)(py - eye.Y);
            float lz = (float)(pz - eye.Z);
            float distance = MathF.Sqrt((lx * lx) + (ly * ly) + (lz * lz));

            // Rejected by polarity, so a NaN distance fails the test rather than passing it.
            if (!(distance > cull))
            {
                continue;
            }

            if (s.ShelterHeightMetres > 0f && _simulation.IsSheltered(px, py, pz, s.ShelterHeightMetres))
            {
                continue;
            }

            // A hard switch between the two strips, never a blend: a drop is either in the depth the lens
            // holds sharp or it is not, and a mix of the two strips is a streak with a ghost beside it.
            bool focused = !(focusDistance > 0f && distance < focusDistance);
            float v0 = focused ? 0f : sheetSplitV;
            float v1 = focused ? sheetSplitV : 1f;

            float alpha = alphaScale;

            // Gusts: the rain thins where the smooth field is low and keeps its full density where it is
            // high. The field travels at a slant through all three axes, so the gaps sweep across the yard
            // rather than marching along one of the world's axes.
            if (gusting)
            {
                float field = PrecipitationSimulation.SmoothNoise(
                    ((float)px * gustScale) + (gustTravel * 0.74f),
                    ((float)py * gustScale) + (gustTravel * 0.52f),
                    ((float)pz * gustScale) - (gustTravel * 0.31f));
                float density = Math.Clamp((field - 0.2f) / 0.6f, 0f, 1f);
                alpha *= 1f - (gustAmount * (1f - density));
            }

            // Faded in from the lens: this column is centred on the eye, so without a ramp a defocused
            // streak the size of a hand snaps into existence a metre away.
            alpha *= Math.Clamp((distance - cull) / nearFade, 0f, 1f);

            if (!(alpha > 0.002f))
            {
                continue;
            }

            Vector3f velocity = drop.Velocity;
            Vector3f axis = velocity.NormalizedOr(-Vector3f.UnitZ, 1e-6f);
            float speed = MathF.Sqrt(
                (velocity.X * velocity.X) + (velocity.Y * velocity.Y) + (velocity.Z * velocity.Z));

            // The streak is the distance the drop covers while the shutter is open, so it leans the way
            // the drop is actually travelling instead of hanging vertical in a crosswind. Divided by how
            // much of its cell the painted streak actually fills, so the tuning keeps meaning the streak
            // rather than the quad the streak is printed on.
            float length =
                (s.DropLengthMetres + (speed * s.MotionStretch / TicksPerSecond)) * drop.Scale / StreakLengthFill;
            float width =
                (s.DropWidthMetres + (distance * s.WidthBoostPerMetre)) * drop.Scale / FocusedStreakFill;

            float half = length * 0.5f;
            float halfWidth = width * 0.5f;

            float ex = -lx / distance, ey = -ly / distance, ez = -lz / distance;
            Vector3f side = (new Vector3f(
                (axis.Y * ez) - (axis.Z * ey),
                (axis.Z * ex) - (axis.X * ez),
                (axis.X * ey) - (axis.Y * ex))).NormalizedOr(-Vector3f.UnitZ, 1e-6f);

            float red = baseR * AmbientGlint, green = baseG * AmbientGlint, blue = baseB * AmbientGlint;

            // The lamps, which are what light rain at night: a drop falling past a fitting catches it and
            // the rain shows in a cone under every light. The same windowed inverse square and facing the
            // deferred lighting gives the lamps, so the drops are lit by what lights the ground beside
            // them. A drop is lit from every side, so a lamp reaches it as a sphere's average cosine rather
            // than a surface's n.l, and one seen against the lamp -- the lamp behind it -- glows through it
            // as the sun's backlighting does.
            for (int l = 0; l < lamps.Length; l++)
            {
                StormLamp lamp = lamps[l];
                float dx = lamp.Position.X - lx, dy = lamp.Position.Y - ly, dz = lamp.Position.Z - lz;
                float lampDistanceSq = (dx * dx) + (dy * dy) + (dz * dz);
                float reachSq = lamp.Reach * lamp.Reach;

                if (!(lampDistanceSq < reachSq))
                {
                    continue;
                }

                float ratio = lampDistanceSq / MathF.Max(reachSq, 1e-4f);
                float window = Math.Clamp(1f - (ratio * ratio), 0f, 1f);
                float attenuation = (window * window) / (lampDistanceSq + 1f);

                float inverse = 1f / MathF.Max(MathF.Sqrt(lampDistanceSq), 1e-4f);
                float tx0 = dx * inverse, ty0 = dy * inverse, tz0 = dz * inverse;

                if ((lamp.Facing.X * lamp.Facing.X) + (lamp.Facing.Y * lamp.Facing.Y) + (lamp.Facing.Z * lamp.Facing.Z) > 0.25f)
                {
                    attenuation *= MathF.Max(-((lamp.Facing.X * tx0) + (lamp.Facing.Y * ty0) + (lamp.Facing.Z * tz0)), 0f);
                }

                if (!(attenuation > 0f))
                {
                    continue;
                }

                float lit = attenuation * SphereAverageCosine;
                red += s.Colour.R * lamp.Radiance.X * lit;
                green += s.Colour.G * lamp.Radiance.Y * lit;
                blue += s.Colour.B * lamp.Radiance.Z * lit;

                if (focused && s.Backlighting > 0f)
                {
                    float through = Math.Clamp(-((ex * tx0) + (ey * ty0) + (ez * tz0)), 0f, 1f);
                    float glow = through * through * s.Backlighting * attenuation;
                    red += s.BacklightTint.R * lamp.Radiance.X * glow;
                    green += s.BacklightTint.G * lamp.Radiance.Y * glow;
                    blue += s.BacklightTint.B * lamp.Radiance.Z * glow;
                }
            }

            // Backlighting, and only on the sharp drops -- the same depth switch that picks the strip, so
            // the near defocused ones take none of it. A drop is backlit when
            // the eye looks through it towards the sun, which is the dot of the eye-to-drop direction
            // with the direction towards the sun; the eye-to-drop vector is the negation of the
            // drop-to-eye one built just above.
            if (focused && s.Backlighting > 0f)
            {
                float backlit = Math.Clamp(-((ex * sun.X) + (ey * sun.Y) + (ez * sun.Z)), 0f, 1f);
                float glow = backlit * backlit * s.Backlighting;
                red += s.BacklightTint.R * sunColour.X * glow;
                green += s.BacklightTint.G * sunColour.Y * glow;
                blue += s.BacklightTint.B * sunColour.Z * glow;
            }

            // Water, not paint. What a drop adds is the light it bends into the eye, a little of what surrounds it,
            // so it rolls off softly instead of clipping: clipped, every drop in daylight became the same flat
            // white. A faint cool cast for the sky it mostly refracts; each drop a little brighter or dimmer than
            // the next, as their sizes and the light catching them differ.
            float shimmer = 0.8f + (0.4f * Hash01((drop.Seed * 97.13f) + drop.Cell));
            Bgra32 colour = new(
                (byte)(WaterLevel(red * WaterTint.R * shimmer) * 255f),
                (byte)(WaterLevel(green * WaterTint.G * shimmer) * 255f),
                (byte)(WaterLevel(blue * WaterTint.B * shimmer) * 255f),
                (byte)(MathF.Sqrt(Math.Clamp(alpha, 0f, 1f)) * 255f));

            float u0 = drop.Cell / (float)StormRenderer.SheetCells;
            float u1 = (drop.Cell + 1) / (float)StormRenderer.SheetCells;

            float tx = axis.X * half, ty = axis.Y * half, tz = axis.Z * half;
            float sx = side.X * halfWidth, sy = side.Y * halfWidth, sz = side.Z * halfWidth;

            // The axis points along the velocity, so the +axis end leads the fall. The painted streaks
            // carry their bead of light at the top of the image and smear downwards from it — that is
            // the head of the drop and its tail — so the top row of the sheet belongs on the leading
            // end, and v runs backwards along the quad to put it there.
            Vector3f trailingLeft = new(lx - tx - sx, ly - ty - sy, lz - tz - sz);
            Vector3f trailingRight = new(lx - tx + sx, ly - ty + sy, lz - tz + sz);
            Vector3f leadingRight = new(lx + tx + sx, ly + ty + sy, lz + tz + sz);
            Vector3f leadingLeft = new(lx + tx - sx, ly + ty - sy, lz + tz - sz);

            if (!FiniteChecks.IsFinite(trailingLeft) || !FiniteChecks.IsFinite(leadingRight))
            {
                continue;
            }

            // Facing the eye, as the far sheets already do. It pointed the other way, which the fully
            // emissive override happens to make invisible -- but a billboard whose normal faces away
            // from the camera is a trap set for whatever reads normals next.
            Vector3f normal = new(ex, ey, ez);
            int v = (firstQuad + quads) * 4;
            vertices[v] = new Vertex3D(trailingLeft, normal, u0, v1, colour: colour);
            vertices[v + 1] = new Vertex3D(trailingRight, normal, u1, v1, colour: colour);
            vertices[v + 2] = new Vertex3D(leadingRight, normal, u1, v0, colour: colour);
            vertices[v + 3] = new Vertex3D(leadingLeft, normal, u0, v0, colour: colour);
            quads++;
        }

        return quads;
    }

    /// <summary>Builds every visible splash: crowns standing up out of the ground, droplets facing the eye.</summary>
    /// <returns>How many quads were written.</returns>
    internal int BuildSplashes(Vertex3D[] vertices, PrecipitationSettings s, Vector3d eye, int firstQuad, in Vector3f ambientColour)
    {
        float alphaScale = Math.Clamp(s.SplashAlpha, 0f, 1f) * Math.Clamp(s.Intensity, 0f, 1f);
        float reach = MathF.Max(s.SplashReachMetres, 1f);

        // Splashes take the scene's own light on their base colour, and no glimmer: water thrown off a
        // pavement does not sparkle the way a falling drop does.
        (float lightR, float lightG, float lightB) = Ambient(s, ambientColour);
        float baseR = s.SplashColour.R * lightR;
        float baseG = s.SplashColour.G * lightG;
        float baseB = s.SplashColour.B * lightB;

        Bgra32 Tint(float alpha) => new(
            (byte)(Math.Clamp(baseR, 0f, 1f) * 255f),
            (byte)(Math.Clamp(baseG, 0f, 1f) * 255f),
            (byte)(Math.Clamp(baseB, 0f, 1f) * 255f),
            (byte)(Math.Clamp(alpha, 0f, 1f) * 255f));

        int quads = 0;
        for (int i = 0; i < _simulation.LiveSplashes; i++)
        {
            ref PrecipitationSplash splash = ref _simulation.Splashes[i];
            float life = Math.Clamp(splash.Age / MathF.Max(splash.Life, 1e-4f), 0f, 1f);

            float lx = (float)(splash.X - eye.X);
            float ly = (float)(splash.Y - eye.Y);
            float lz = (float)(splash.Z - eye.Z);
            float distance = MathF.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
            if (!(distance > 0.05f))
            {
                continue;
            }

            float alpha = alphaScale;

            // A thrown droplet is a couple of millimetres across and goes under a pixel long before the edge
            // of the splash disc, so it fades out over the disc rather than vanishing at its rim. A crown
            // keeps its strength: it is the part of the splash that still reads from across the yard.
            if (splash.Small)
            {
                alpha *= 1f - Math.Clamp(distance / reach, 0f, 1f);
            }

            // Held at full strength until the flipbook has most of its water in the air, then let go.
            // The animation dissipates on its own; fading it from the first frame throws away the frames
            // that were drawn to be seen.
            alpha *= 1f - Math.Clamp((life - 0.65f) / 0.35f, 0f, 1f);
            if (!(alpha > 0.004f))
            {
                continue;
            }

            // Eight frames over the life, and the last one has to be reachable: with a plain floor a
            // splash that reaches exactly the end of its life indexes past the strip, and clamping the
            // life first is what keeps that from wrapping to frame zero for one frame.
            // Four across, two down, counted in reading order, and the variant chooses which pair of
            // strips, because each of the three animations was unpacked from one colour channel of the
            // source into two strips of its own.
            int frame = Math.Clamp((int)(life * StormRenderer.SplashFrames), 0, StormRenderer.SplashFrames - 1);
            int variant = Math.Clamp(splash.Variant, 0, StormRenderer.SplashVariants - 1);
            int column = splash.Small ? 0 : frame % StormRenderer.SplashColumns;
            int row = splash.Small
                ? StormRenderer.SplashBlobRow
                : (variant * StormRenderer.SplashFrameRows) + (frame / StormRenderer.SplashColumns);
            (float u0, float u1) = PrecipitationSpriteSheets.SplashColumnUv(column, splash.FlipU);
            float v0 = row / (float)StormRenderer.SplashRows;
            float v1 = (row + 1) / (float)StormRenderer.SplashRows;

            int v = (firstQuad + quads) * 4;

            if (splash.Small)
            {
                // A droplet in the air, so it faces the eye squarely rather than lying flat: it has left
                // the ground and is on its way back down, and a flat quad seen edge-on disappears at
                // exactly the moment the arc is most visible.
                float radius = splash.Radius;
                Bgra32 colour = Tint(alpha);
                Vector3f towards = new(-lx / distance, -ly / distance, -lz / distance);

                // Any axis will do for a round blob; the world's vertical crossed with the view gives a
                // stable one, and its degenerate case is a droplet directly overhead.
                Vector3f across = (new Vector3f(-towards.Y, towards.X, 0f)).NormalizedOr(-Vector3f.UnitZ, 1e-6f);
                Vector3f down = new(
                    (towards.Y * across.Z) - (towards.Z * across.Y),
                    (towards.Z * across.X) - (towards.X * across.Z),
                    (towards.X * across.Y) - (towards.Y * across.X));

                Vector3f a = new(
                    lx - (across.X * radius) - (down.X * radius),
                    ly - (across.Y * radius) - (down.Y * radius),
                    lz - (across.Z * radius) - (down.Z * radius));
                Vector3f b = new(
                    lx + (across.X * radius) - (down.X * radius),
                    ly + (across.Y * radius) - (down.Y * radius),
                    lz + (across.Z * radius) - (down.Z * radius));
                Vector3f c = new(
                    lx + (across.X * radius) + (down.X * radius),
                    ly + (across.Y * radius) + (down.Y * radius),
                    lz + (across.Z * radius) + (down.Z * radius));
                Vector3f d = new(
                    lx - (across.X * radius) + (down.X * radius),
                    ly - (across.Y * radius) + (down.Y * radius),
                    lz - (across.Z * radius) + (down.Z * radius));
                if (!FiniteChecks.IsFinite(a) || !FiniteChecks.IsFinite(c))
                {
                    continue;
                }

                vertices[v] = new Vertex3D(a, towards, u0, v0, colour: colour);
                vertices[v + 1] = new Vertex3D(b, towards, u1, v0, colour: colour);
                vertices[v + 2] = new Vertex3D(c, towards, u1, v1, colour: colour);
                vertices[v + 3] = new Vertex3D(d, towards, u0, v1, colour: colour);
                quads++;
                continue;
            }

            // The crown stands up out of the ground. Every frame in the strip is painted with the water
            // rising from the bottom edge of its cell, so the quad is anchored at its base and grows
            // upwards, turned to the eye about the vertical. Drawn flat on the floor, which is what these
            // were, a crown is seen from directly above at every angle and the animation reads as a stain
            // spreading rather than as water being thrown.
            float half = splash.Radius;
            float height = half * 2f;

            float flat = MathF.Max(MathF.Sqrt((lx * lx) + (ly * ly)), 1e-4f);
            float rx = -ly / flat;
            float ry = lx / flat;

            Bgra32 crown = Tint(alpha);
            Vector3f facing = new(-lx / distance, -ly / distance, 0f);

            Vector3f bottomLeft = new(lx - (rx * half), ly - (ry * half), lz);
            Vector3f bottomRight = new(lx + (rx * half), ly + (ry * half), lz);
            Vector3f topRight = new(lx + (rx * half), ly + (ry * half), lz + height);
            Vector3f topLeft = new(lx - (rx * half), ly - (ry * half), lz + height);
            if (!FiniteChecks.IsFinite(bottomLeft) || !FiniteChecks.IsFinite(topRight))
            {
                continue;
            }

            // v runs from the top of the cell at the top of the quad: the sheet is painted with the
            // ground at the bottom of each frame, and the image's first row is its top.
            vertices[v] = new Vertex3D(bottomLeft, facing, u0, v1, colour: crown);
            vertices[v + 1] = new Vertex3D(bottomRight, facing, u1, v1, colour: crown);
            vertices[v + 2] = new Vertex3D(topRight, facing, u1, v0, colour: crown);
            vertices[v + 3] = new Vertex3D(topLeft, facing, u0, v0, colour: crown);
            quads++;
        }

        return quads;
    }

    /// <summary>Builds the far sheets, upright and turned to the eye.</summary>
    /// <returns>How many quads were written.</returns>
    internal int BuildCurtains(Vertex3D[] vertices, PrecipitationSettings s, Vector3d eye, int firstQuad)
    {
        if (!s.Curtains)
        {
            return 0;
        }

        float alphaScale = Math.Clamp(s.CurtainAlpha, 0f, 1f) * Math.Clamp(s.Intensity, 0f, 1f);
        if (!(alphaScale > 0.001f))
        {
            return 0;
        }

        int quads = 0;
        for (int i = 0; i < _simulation.LiveCurtains; i++)
        {
            ref PrecipitationCurtain curtain = ref _simulation.Curtains[i];
            float lx = (float)(curtain.X - eye.X);
            float ly = (float)(curtain.Y - eye.Y);
            float lz = (float)(curtain.Z - eye.Z);
            float distance = MathF.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
            if (!(distance > 1f))
            {
                continue;
            }

            // Faded in with distance, the opposite of the drops: these exist to fill the depth the
            // drops have stopped resolving at, so one close enough to read as a shape is wrong. Measured
            // against the radius they are actually scattered over, not a different one.
            float reach = PrecipitationSimulation.CurtainReach(s);
            float near = MathF.Max(s.SpawnRadiusMetres, 1f);
            float alpha = alphaScale * Math.Clamp((distance - near) / MathF.Max(reach - near, 1f), 0f, 1f);

            // Weighted by where the sheet sits against the eye: level with it, as tuned; below it, up to
            // twice as strong, because haze lying over far ground is where far rain is seen; above it,
            // fading to nothing well before overhead, so the drizzle never paints the sky.
            float rise = lz / distance;
            alpha = MathF.Min(alpha * Math.Clamp(1f - (2.5f * rise), 0f, 2f), 1f);
            if (!(alpha > 0.002f))
            {
                continue;
            }

            float half = MathF.Max(s.CurtainSizeMetres, 1f) * 0.5f * (0.7f + (curtain.Seed * 0.6f));

            // Upright and turned to face the eye about the vertical: a sheet of falling drizzle hangs,
            // it does not tilt to look at the camera.
            float rx = -ly / MathF.Max(MathF.Sqrt((lx * lx) + (ly * ly)), 1e-4f);
            float ry = lx / MathF.Max(MathF.Sqrt((lx * lx) + (ly * ly)), 1e-4f);

            Bgra32 colour = new(
                (byte)(Math.Clamp(s.Colour.R + 0.25f, 0f, 1f) * 255f),
                (byte)(Math.Clamp(s.Colour.G + 0.25f, 0f, 1f) * 255f),
                (byte)(Math.Clamp(s.Colour.B + 0.28f, 0f, 1f) * 255f),
                (byte)(Math.Clamp(alpha, 0f, 1f) * 255f));
            Vector3f normal = new(-lx / distance, -ly / distance, 0f);

            Vector3f a = new(lx - (rx * half), ly - (ry * half), lz - half);
            Vector3f b = new(lx + (rx * half), ly + (ry * half), lz - half);
            Vector3f c = new(lx + (rx * half), ly + (ry * half), lz + half);
            Vector3f d = new(lx - (rx * half), ly - (ry * half), lz + half);
            if (!FiniteChecks.IsFinite(a) || !FiniteChecks.IsFinite(c))
            {
                continue;
            }

            int v = (firstQuad + quads) * 4;
            vertices[v] = new Vertex3D(a, normal, 0f, 0f, colour: colour);
            vertices[v + 1] = new Vertex3D(b, normal, 1f, 0f, colour: colour);
            vertices[v + 2] = new Vertex3D(c, normal, 1f, 1f, colour: colour);
            vertices[v + 3] = new Vertex3D(d, normal, 0f, 1f, colour: colour);
            quads++;
        }

        return quads;
    }
}
