using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI;
using OpenIGI.UI.Widgets;
using System.Globalization;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>The remaster's tactical, resolution-independent in-game HUD.</summary>
/// <remarks>
/// The status screen remains the faithful retail renderer.  This deliberately consumes the same live
/// player, weapon and objective data but presents it with the modern shell's distance-field fonts and
/// layout system, so changing the surface cannot change the simulation behind it.
/// </remarks>
internal sealed class RemasteredHudRenderer : IRemasteredHudPresenter
{
    /// <summary>Leaves breathing room below the compact objective readout for the modern comms cards.</summary>
    public const float MessageTopInsetDesign = 116f;

    /// <inheritdoc/>
    public float MessageTopInset => MessageTopInsetDesign;

    /// <summary>The interaction badge's radius, in the marker's own design units.</summary>
    /// <remarks>
    /// Design units, not pixels: the marker stands in the level, so what fixes its size is
    /// <see cref="MetresPerUnit"/> and how far away the player is, not the resolution.
    /// </remarks>
    public const float BadgeRadiusDesign = 21f;

    /// <summary>How much of the world one of the marker's design units covers.</summary>
    /// <remarks>
    /// Sized so the badge is about eight centimetres across — a thing you could hang on a door handle.
    /// Everything else in the marker is laid out against the badge, so this one number sets the whole
    /// marker's presence in the level, and perspective does the rest: it grows as the player closes on
    /// it and foreshortens when they look down at it, because it is standing there.
    /// </remarks>
    public const float MetresPerUnit = 0.0019f;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private UiFont? _body;
    private UiFont? _display;
    private UiTheme? _theme;
    private UiSurfaces? _surfaces;
    private InteractionBadgeSprite? _badge;
    private bool _disposed;

    public RemasteredHudRenderer(IGraphicsDevice device, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        _device = device;
        _logger = logger;
    }

    public bool IsReady => _body is not null;

    public bool Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!UiFontLibrary.TypographyAvailable)
        {
            _logger.Warning("Hud", $"No interface font at {UiFontLibrary.FontsDirectory}; remaster HUD cannot draw.");
            return false;
        }

        try
        {
            // The design's faces, shared with the menus: the HUD labels in monospace, its values in the
            // condensed faces, and the binocular's marker labels in the distance-field title face so a
            // range digit can carry the outline and glow that make it read as projected onto the optics.
            IgiTypography typography = UiFontLibrary.Typography(_device);
            _theme = typography.CreateTheme();
            _body = typography.Body;
            _display = typography.Display;

            // The interface's antialiased corner atlas. Every curve this HUD draws over the world -- the
            // interaction badge and its dial -- is a filled rounded rectangle sampled from it, because
            // the painter rasterises triangles with no coverage and a circle built from them shows its
            // facets at exactly the size a badge is drawn.
            _surfaces = UiSurfaces.Create(_device);

            // The interaction badge is a generated sprite rather than a stack of filled shapes; see
            // InteractionBadgeSprite for why a marker that fades cannot be assembled at draw time.
            _badge = InteractionBadgeSprite.Create(_device);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning("Hud", $"Could not load the remaster HUD fonts: {ex.Message}");
            return false;
        }
    }

    public void Draw(SpriteBatch batch, in RemasteredHudSnapshot snapshot, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_theme is not { } theme)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        DrawCompass(painter, context, theme, snapshot.HeadingRadians);
        DrawObjectives(painter, context, theme, snapshot.Objectives);
        DrawVitals(painter, context, theme, snapshot.HealthFraction);
        DrawWeapon(painter, context, theme, snapshot);
        DrawCrosshair(painter, context, snapshot.Weapon, snapshot.SniperScoped);
        if (!snapshot.WorldInteractionPrompt)
        {
            DrawInteraction(painter, context, theme, snapshot.ActionSprite);
        }
    }

    /// <summary>Draws the remastered binocular optics overlay over the glass-lens post-process.</summary>
    /// <remarks>
    /// The glass tint, bloom and bezel vignette are a resolve-time display transform (see
    /// <see cref="OpenIGI.Rendering.BinocularDisplaySettings"/>); this draws the crisp vector interface
    /// on top of it. Contacts are modern chevron markers with a glowing range label rather than the
    /// retail rotating rectangle -- the faithful rectangle stays on the vanilla <c>StatusScreenRenderer</c>.
    /// </remarks>
    public void DrawBinocularOverlay(
        SpriteBatch batch,
        float headingRadians,
        float tanX,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets,
        float seconds,
        float? rangeMetres)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_body is not { } body)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);

        // No full-screen wash and no opaque frame: the lens post-process owns the glass now, so the HUD
        // stays crisp chrome over it. Thin rails and corner ticks read as an optics frame without
        // tinting the picture a second time.
        float inset = context.Px(30f);
        UiRect frame = new(inset, inset, context.SafeRect.Width - (inset * 2f), context.SafeRect.Height - (inset * 2f));
        painter.FillRect(new UiRect(frame.X, frame.Y, frame.Width, context.Px(2f)), Signal.WithAlpha(190));
        painter.FillRect(new UiRect(frame.X, frame.Bottom - context.Px(2f), frame.Width, context.Px(2f)), Signal.WithAlpha(120));
        DrawCornerTicks(painter, context, frame, Signal.WithAlpha(170));

        if (_theme is { } theme)
        {
            DrawCompass(painter, context, theme, headingRadians);
        }

        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float centreY = context.SafeRect.Y + (context.SafeRect.Height * 0.5f);
        DrawReticle(painter, context, centreX, centreY);

        if (targets is not null)
        {
            foreach (EnemyOverlayBox target in targets)
            {
                if (target.TryClipToViewport(screenWidth, screenHeight, out EnemyOverlayBox clipped))
                {
                    DrawContactMarker(painter, context, clipped, seconds);
                }
            }
        }

        // The range is the crosshair raycast the caller supplies -- the distance to whatever the centre
        // rests on -- exactly as the vanilla readout shows, not the nearest detected contact. Same
        // 1000 m cap as the retail widget.
        string range = rangeMetres is { } metres && float.IsFinite(metres) && metres > 0f && metres < 1000f
            ? FormattableString.Invariant($"RANGE  {metres:0}M")
            : "RANGE  1000+M";
        float magnification = tanX > 0.0001f ? HumanView.DefaultTanX / tanX : 1f;
        UiVectors.DrawTracked(
            painter, body, "OPTICS // BINOCULAR",
            new UiVec2(frame.X + context.Px(18f), frame.Bottom - context.Px(48f)), context.Px(15f), Signal, context.Px(2f));
        painter.DrawText(
            body, FormattableString.Invariant($"x{magnification:0.0}    {range}"),
            new UiVec2(frame.Right - context.Px(250f), frame.Bottom - context.Px(48f)), context.Px(15f), Text);
    }

    /// <summary>The colour a contact's marker is drawn in, by its detector team classification.</summary>
    private static Bgra32 MarkerColour(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => Bgra32.FromRgb(120, 235, 140),
        BinocularMarkerColor.Disabled => Bgra32.FromRgb(120, 170, 255),
        _ => Bgra32.FromRgb(255, 72, 72),
    };

    /// <summary>Draws one detected contact: a bobbing chevron over the target and a glowing range label.</summary>
    private void DrawContactMarker(UiPainter painter, LayoutContext context, EnemyOverlayBox target, float seconds)
    {
        float alpha = Math.Clamp(target.Alpha, 0f, 1f);
        if (alpha <= 0f)
        {
            return;
        }

        float centreX = (target.Left + target.Right) * 0.5f;
        Bgra32 colour = MarkerColour(target.Marker);

        // The acquisition flourish reuses the detector's own entry pulse: while PulseScale is still
        // large the contact is freshly seen, so an expanding, fading ring collapses onto the chevron.
        if (target.PulseScale > 1.05f)
        {
            float boxCentreY = (target.Top + target.Bottom) * 0.5f;
            float ringRadius = context.Px(9f) * target.PulseScale * 0.5f;
            float ringFade = Math.Clamp((target.PulseScale - 1f) / 10f, 0f, 1f) * alpha * 0.7f;
            UiVectors.StrokeRing(
                painter, new UiVec2(centreX, boxCentreY), ringRadius,
                MathF.Max(1f, context.Px(2f)), colour.WithAlpha((byte)(255f * ringFade)));
        }

        // The chevron floats just above the contact, bobbing on a per-target phase so a cluster of
        // marks does not pulse in lockstep. A dark backing chevron keeps it legible over bright terrain.
        float bob = context.Px(4f) * MathF.Sin((seconds * 2.2f) + target.TargetId);
        var apex = new UiVec2(centreX, target.Top - context.Px(7f) + bob);
        float halfWidth = context.Px(9f);
        float height = context.Px(11f);
        UiVectors.DrawChevron(
            painter, new UiVec2(apex.X, apex.Y + context.Px(1.5f)),
            halfWidth + context.Px(1.5f), height + context.Px(2f), Bgra32.FromRgba(0, 0, 0, (byte)(130f * alpha)));
        UiVectors.DrawChevron(painter, apex, halfWidth, height, colour.WithAlpha((byte)(255f * alpha)));

        // The range, above the chevron, glowing so it reads as projected onto the optics.
        string label = $"{MathF.Max(0f, target.DistanceMetres):0}M";
        float labelSize = context.Px(15f);
        UiFont labelFont = _display ?? _body!;
        float labelTop = apex.Y - height - labelFont.LineHeight(labelSize) - context.Px(4f);
        DrawGlowLabel(painter, label, centreX, labelTop, labelSize, alpha);
    }

    /// <summary>Draws a centred, glowing label, using the distance-field face when one loaded.</summary>
    private void DrawGlowLabel(UiPainter painter, string text, float centreX, float topY, float size, float alpha)
    {
        Bgra32 white = Bgra32.FromRgb(238, 246, 250).WithAlpha((byte)(255f * alpha));
        if (_display is { } display)
        {
            float width = display.MeasureWidth(text, size);
            SdfTextParams effects = SdfTextParams.Plain(0f)
                .WithOutline(Bgra32.FromRgba(0, 0, 0, (byte)(190f * alpha)), 0.18f)
                .WithGlow(Signal, 0.5f, 0.5f * alpha);
            painter.DrawText(display, text, new UiVec2(centreX - (width * 0.5f), topY), size, white, effects);
        }
        else if (_body is { } body)
        {
            float width = body.MeasureWidth(text, size);
            Bgra32 backing = Bgra32.FromRgba(0, 0, 0, (byte)(170f * alpha));
            painter.DrawText(body, text, new UiVec2(centreX - (width * 0.5f) + 1f, topY + 1f), size, backing);
            painter.DrawText(body, text, new UiVec2(centreX - (width * 0.5f), topY), size, white);
        }
    }

    /// <summary>Draws the gapped rangefinder reticle: crosshair arms, a framing ring and a centre dot.</summary>
    private static void DrawReticle(UiPainter painter, LayoutContext context, float centreX, float centreY)
    {
        Bgra32 mint = Signal.WithAlpha(210);
        float arm = context.Px(24f);
        float gap = context.Px(10f);
        float weight = MathF.Max(1f, context.Px(1.5f));
        painter.FillRect(new UiRect(centreX - gap - arm, centreY - (weight * 0.5f), arm, weight), mint);
        painter.FillRect(new UiRect(centreX + gap, centreY - (weight * 0.5f), arm, weight), mint);
        painter.FillRect(new UiRect(centreX - (weight * 0.5f), centreY - gap - arm, weight, arm), mint);
        painter.FillRect(new UiRect(centreX - (weight * 0.5f), centreY + gap, weight, arm), mint);
        UiVectors.StrokeRing(
            painter, new UiVec2(centreX, centreY), context.Px(3.4f), MathF.Max(1f, context.Px(1.2f)), mint.WithAlpha(150));
        UiVectors.FillDisc(painter, new UiVec2(centreX, centreY), MathF.Max(1f, context.Px(1.6f)), Accent);
    }

    /// <summary>Draws short L-brackets at the corners of the optics frame.</summary>
    private static void DrawCornerTicks(UiPainter painter, LayoutContext context, UiRect frame, Bgra32 colour)
    {
        float length = context.Px(28f);
        float weight = MathF.Max(1f, context.Px(2f));
        painter.FillRect(new UiRect(frame.X, frame.Y, length, weight), colour);
        painter.FillRect(new UiRect(frame.X, frame.Y, weight, length), colour);
        painter.FillRect(new UiRect(frame.Right - length, frame.Y, length, weight), colour);
        painter.FillRect(new UiRect(frame.Right - weight, frame.Y, weight, length), colour);
        painter.FillRect(new UiRect(frame.X, frame.Bottom - weight, length, weight), colour);
        painter.FillRect(new UiRect(frame.X, frame.Bottom - length, weight, length), colour);
        painter.FillRect(new UiRect(frame.Right - length, frame.Bottom - weight, length, weight), colour);
        painter.FillRect(new UiRect(frame.Right - weight, frame.Bottom - length, weight, length), colour);
    }

    /// <summary>Draws the world-anchored interaction marker over the thing the player can use.</summary>
    /// <param name="batch">The open sprite batch.</param>
    /// <param name="view">The prompt to draw, from <see cref="InteractionPromptPresentation"/>.</param>
    /// <param name="eye">The presented camera the frame was drawn from.</param>
    /// <param name="horizontalTangent">The tangent of half the horizontal field of view.</param>
    /// <param name="aspectRatio">The output's width over its height.</param>
    /// <param name="screenWidth">The framebuffer width.</param>
    /// <param name="screenHeight">The framebuffer height.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The marker is pinned to the object rather than printed across the middle of the screen, which is
    /// what makes it read as a property of the door in front of the player instead of a caption about
    /// it. The badge carries the key that is actually bound, the ring carries the hold, and the label
    /// beside it names the action.
    /// </para>
    /// <para>
    /// It is drawn on a <see cref="WorldCanvas"/> — a surface standing at the object, turned to the
    /// player — rather than as an overlay at a projected point. That is what makes it behave like a
    /// thing in the level: it grows as the player closes on it, foreshortens when they look down at it,
    /// and holds its place on the door as they walk past, none of which a flat marker on the screen
    /// does however carefully its size is scaled.
    /// </para>
    /// </remarks>
    public void DrawInteractionPrompt(
        SpriteBatch batch,
        in InteractionPromptView view,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_body is not { } body || view.Opacity <= 0f)
        {
            return;
        }

        // The entry pop scales the canvas rather than the layout: one number, and every part of the
        // marker grows into place together while the design keeps its proportions.
        float pop = 0.62f + (0.38f * Math.Clamp(view.Pop, 0f, 1.15f));
        var canvas = new WorldCanvas(
            view.AnchorMetres,
            eye,
            horizontalTangent,
            aspectRatio,
            screenWidth,
            screenHeight,
            MetresPerUnit * pop);

        byte alpha = (byte)(255f * Math.Clamp(view.Opacity, 0f, 1f));
        float radius = BadgeRadiusDesign;
        float ring = 2.4f;

        bool wide = InteractionPromptPresentation.IsWideBadge(view.KeyLabel);
        float keySize = wide ? 17f : 22f;
        UiFont keyFont = _display ?? body;
        float keyWidth = keyFont.MeasureWidth(view.KeyLabel, keySize);

        // A key cap is as wide as its legend plus a cap's worth of shoulder on each side, so SPACE and
        // ENTER keep the same breathing room a single letter has inside the circle.
        float halfWidth = wide ? MathF.Max(radius, (keyWidth * 0.5f) + (radius * 0.8f)) : radius;

        // Shadow, then the dial, then the badge over both: the badge's own face is what gives the dial
        // its clean inner edge, and drawing the shadow last would darken the dial it sits inside.
        DrawCanvasShadow(canvas, batch, halfWidth, radius, ring * 2.6f, alpha);
        DrawCanvasProgress(canvas, batch, halfWidth, radius, ring, wide, view.Progress, alpha);
        _badge?.Draw(canvas, batch, 0f, 0f, halfWidth, radius, Bgra32.FromRgba(255, 255, 255, alpha));

        // The key is centred on its own ink, not on its line box: a capital measured by the font's
        // ascent sits visibly high inside a circle, which is most of what makes a badge look homemade.
        canvas.DrawText(
            batch,
            keyFont,
            view.KeyLabel,
            -keyWidth * 0.5f,
            -InkCentre(keyFont, view.KeyLabel, keySize) - keyFont.Ascent(keySize),
            keySize,
            Text.WithAlpha(alpha),
            OutlineOnly(keyFont, 120, 0.1f),
            keyFont.Kind == FontAtlasKind.SignedDistanceField);

        DrawPromptLabel(canvas, batch, body, view, halfWidth, alpha, screenWidth, screenHeight);
    }

    /// <summary>Lays the badge's shadow on the canvas, under everything else.</summary>
    private void DrawCanvasShadow(
        in WorldCanvas canvas,
        SpriteBatch batch,
        float halfWidth,
        float radius,
        float spread,
        byte alpha)
    {
        if (_surfaces is not { } surfaces)
        {
            return;
        }

        canvas.DrawSlice(
            batch, surfaces.Shadow, 0f, 0f, halfWidth + spread, radius + spread,
            Bgra32.FromRgba(0, 0, 0, (byte)(118f * alpha / 255f)));
    }

    /// <summary>Draws the hold as a dial around a round badge, or a bar under a wide one.</summary>
    /// <remarks>
    /// The dial is a whole disc that the badge is then drawn over, which is what gives its inner edge
    /// the same smooth silhouette as its outer one. The filled arc between them is the one part of the
    /// marker that no rectangle describes, so it is built from quads and softened by hand.
    /// </remarks>
    private void DrawCanvasProgress(
        in WorldCanvas canvas,
        SpriteBatch batch,
        float halfWidth,
        float radius,
        float ring,
        bool wide,
        float? progress,
        byte alpha)
    {
        if (progress is not { } value || _surfaces is not { } surfaces)
        {
            return;
        }

        float fraction = Math.Clamp(value, 0f, 1f);
        float band = MathF.Max(1.8f, ring * 1.3f);
        float scale = alpha / 255f;
        if (!wide)
        {
            float inner = (radius * InteractionBadgeSprite.OuterScale) + (ring * 0.5f);
            float outer = inner + band;
            canvas.DrawSlice(
                batch, surfaces.Rounded, 0f, 0f, outer + 1f, outer + 1f,
                Bgra32.FromRgba(0, 0, 0, (byte)(104f * scale)));
            canvas.DrawSlice(batch, surfaces.Rounded, 0f, 0f, outer, outer, Text.WithAlpha((byte)(42f * scale)));
            if (fraction <= 0f)
            {
                return;
            }

            float track = (inner + outer) * 0.5f;
            float sweep = MathF.Tau * fraction;
            canvas.DrawArc(batch, track, band, -MathF.PI * 0.5f, sweep, Accent.WithAlpha(alpha));

            // A round cap at each end, so a part-filled dial does not end in two cut edges.
            canvas.DrawSlice(batch, surfaces.Rounded, 0f, -track, band * 0.5f, band * 0.5f, Accent.WithAlpha(alpha));
            canvas.DrawSlice(
                batch,
                surfaces.Rounded,
                MathF.Cos(sweep - (MathF.PI * 0.5f)) * track,
                MathF.Sin(sweep - (MathF.PI * 0.5f)) * track,
                band * 0.5f,
                band * 0.5f,
                Accent.WithAlpha(alpha));
            return;
        }

        float barRadius = band * 0.75f;
        float barY = radius + (ring * 2.8f) + barRadius;
        canvas.DrawSlice(
            batch, surfaces.Rounded, 0f, barY, halfWidth + 1f, barRadius + 1f,
            Bgra32.FromRgba(0, 0, 0, (byte)(165f * scale)));
        canvas.DrawSlice(
            batch, surfaces.Rounded, 0f, barY, halfWidth - (barRadius * 0.35f), barRadius * 0.65f,
            Text.WithAlpha((byte)(52f * scale)));
        if (fraction <= 0f)
        {
            return;
        }

        float filled = MathF.Max(barRadius, halfWidth * fraction);
        canvas.DrawSlice(
            batch, surfaces.Rounded, -halfWidth + filled, barY, filled, barRadius, Accent.WithAlpha(alpha));
    }

    /// <summary>Draws the action name and its qualifier beside the badge, flipping sides near an edge.</summary>
    private void DrawPromptLabel(
        in WorldCanvas canvas,
        SpriteBatch batch,
        UiFont body,
        in InteractionPromptView view,
        float halfWidth,
        byte alpha,
        int screenWidth,
        int screenHeight)
    {
        if (view.Title.Length == 0)
        {
            return;
        }

        // Both lines are set in the distance-field face where there is one. The body atlas is
        // rasterised at forty-eight pixels and a qualifier drawn at a fraction of that minifies it,
        // which crawls along the letterforms; a distance field is exact at any size, and standing in
        // the level that difference is the whole of whether the marker looks finished.
        UiFont titleFont = _display ?? body;
        UiFont detailFont = _display ?? body;
        float titleSize = 23f;
        float detailSize = 15f;
        bool hasDetail = view.Detail.Length > 0;

        // Measured and placed on ink, so the title's own middle lands on the badge's middle whether or
        // not there is a second line under it.
        float titleInk = InkHeight(titleFont, view.Title, titleSize);
        float detailInk = hasDetail ? InkHeight(detailFont, view.Detail, detailSize) : 0f;
        float gap = hasDetail ? 7f : 0f;
        float blockTop = -(titleInk + gap + detailInk) * 0.5f;

        float titleWidth = titleFont.MeasureWidth(view.Title, titleSize);
        float detailWidth = hasDetail ? detailFont.MeasureWidth(view.Detail, detailSize) : 0f;
        float blockWidth = MathF.Max(titleWidth, detailWidth);
        float margin = halfWidth + 16f;

        // Beside the badge, on whichever side has room. Where the far end of the name would land off
        // the frame it goes the other way instead: a marker close to the edge of the screen would
        // otherwise push its own name past it at exactly the moment the player needs to read it.
        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        float x = margin;
        if (canvas.TryProject(x + blockWidth, 0f, out float endX, out _)
            && endX > context.SafeRect.Right)
        {
            x = -margin - blockWidth;
        }

        DrawCanvasWorldLabel(
            canvas, batch, titleFont, view.Title, x, InkTop(titleFont, view.Title, titleSize, blockTop),
            titleSize, Text.WithAlpha(alpha));
        if (hasDetail)
        {
            // Brighter than the HUD's ordinary muted grey: this line is read over sunlit ground rather
            // than over the HUD's own dark chrome, and the readable grey there is not the readable grey
            // in a corner of the screen.
            float detailTop = InkTop(detailFont, view.Detail, detailSize, blockTop + titleInk + gap);
            DrawCanvasWorldLabel(
                canvas, batch, detailFont, view.Detail, x, detailTop, detailSize,
                Bgra32.FromRgba(214, 223, 231, alpha));
        }
    }

    /// <summary>Sets a label on the canvas, shadowed and outlined so it survives any background.</summary>
    private static void DrawCanvasWorldLabel(
        in WorldCanvas canvas,
        SpriteBatch batch,
        UiFont font,
        string text,
        float x,
        float y,
        float size,
        Bgra32 colour)
    {
        // A cast shadow and a tight outline, deliberately kept thin: a heavy outline on a
        // distance-field face fattens the letterforms until the name looks printed on the badge rather
        // than set beside it. The shadow is offset within the canvas, so it falls on the plane the
        // text stands on and turns with it.
        byte shadowAlpha = (byte)(colour.A * 0.42f);
        bool sdf = font.Kind == FontAtlasKind.SignedDistanceField;
        canvas.DrawText(
            batch, font, text, x + (size * 0.05f), y + (size * 0.07f), size,
            Bgra32.FromRgba(0, 0, 0, shadowAlpha), OutlineOnly(font, (byte)(shadowAlpha * 0.8f), 0.16f), sdf);
        canvas.DrawText(
            batch, font, text, x, y, size, colour, OutlineOnly(font, (byte)(colour.A * 0.72f), 0.15f), sdf);
    }

    /// <summary>A distance-field face's plain shading with an outline, at the atlas's own range.</summary>
    /// <remarks>
    /// The range has to be set here. <see cref="UiPainter"/> fills it in from the font on the way past;
    /// a quad handed straight to the batch carries whatever the caller put in it.
    /// </remarks>
    private static SdfTextParams OutlineOnly(UiFont font, byte outlineAlpha, float thickness) =>
        SdfTextParams.Plain(font.Atlas.SdfPadding)
            .WithOutline(Bgra32.FromRgba(0, 0, 0, outlineAlpha), thickness);

    /// <summary>How far a run of text's inked middle sits below its baseline, in canvas units.</summary>
    private static float InkCentre(UiFont font, ReadOnlySpan<char> text, float size)
    {
        (float top, float bottom) = Ink(font, text, size);
        return (top + bottom) * 0.5f;
    }

    /// <summary>The height of a run of text's inked box, ignoring the empty space in its line.</summary>
    private static float InkHeight(UiFont font, ReadOnlySpan<char> text, float size)
    {
        (float top, float bottom) = Ink(font, text, size);
        return MathF.Max(0f, bottom - top);
    }

    /// <summary>The top-left Y that puts a run of text's ink at <paramref name="inkTop"/>.</summary>
    private static float InkTop(UiFont font, ReadOnlySpan<char> text, float size, float inkTop)
    {
        (float top, _) = Ink(font, text, size);
        return inkTop - top - font.Ascent(size);
    }

    /// <summary>The inked box of a run of text, relative to its baseline.</summary>
    private static (float Top, float Bottom) Ink(UiFont font, ReadOnlySpan<char> text, float size)
    {
        float scale = font.ScaleFor(size);
        float top = float.PositiveInfinity;
        float bottom = float.NegativeInfinity;
        foreach (char character in text)
        {
            GlyphInfo glyph = font.GetGlyph(character);
            if (!glyph.HasBitmap)
            {
                continue;
            }

            top = MathF.Min(top, glyph.OffsetY * scale);
            bottom = MathF.Max(bottom, (glyph.OffsetY + glyph.Height) * scale);
        }

        return float.IsFinite(top) ? (top, bottom) : (-font.Ascent(size), 0f);
    }

    /// <summary>Draws the modern presentation of the same timed state progress the retail HUD exposes.</summary>
    public void DrawInteractionProgress(SpriteBatch batch, float progress, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_theme is not { } theme)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float width = MathF.Min(context.Px(430f), context.SafeRect.Width * 0.52f);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float y = context.SafeRect.Bottom - context.Px(118f);
        painter.DrawLabel(theme.Mono, "INTERACTING", new UiVec2(x, y), context.Px(11f), Accent, 0.28f);
        DrawMeter(
            painter,
            new UiRect(x, y + context.Px(25f), width, context.Px(6f)),
            progress,
            Accent);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The faces are the device's shared set, not this renderer's to free.
        _body = null;
        _display = null;
        _theme = null;
        _surfaces?.Dispose();
        _surfaces = null;
        _badge?.Dispose();
        _badge = null;
    }

    private static void DrawObjectives(
        UiPainter painter,
        LayoutContext context,
        UiTheme type,
        IReadOnlyList<MissionObjective> objectives)
    {
        MissionObjective? primary = RemasteredHudPresentation.PrimaryObjective(objectives);
        if (primary is null && objectives.Count == 0)
        {
            return;
        }

        float x = context.SafeRect.X + context.Px(34f);
        float y = context.SafeRect.Y + context.Px(30f);
        float width = MathF.Min(context.Px(470f), context.SafeRect.Width * 0.36f);
        float accentWidth = context.Px(3f);
        float textX = x + context.Px(18f);
        float labelSize = context.Px(11f);
        float textSize = context.Px(22f);
        string text = primary?.Text.Length > 0 ? primary.Text : primary?.TextResource ?? "OBJECTIVES COMPLETE";
        string wrapped = type.Body.WrapText(text, textSize, width - (textX - x));
        float labelHeight = type.Mono.LineHeight(labelSize);
        float height = labelHeight + context.Px(8f) + type.Body.Measure(wrapped, textSize).Y + context.Px(6f);

        // The design's selected-row treatment: an accent bar down the leading edge over a wash that fades
        // out to the right, which keeps the text legible over any sky without boxing it in.
        var block = new UiRect(x, y - context.Px(8f), width + context.Px(40f), height + context.Px(16f));
        Span<UiGradientStop> wash = [new(0f, UiPalette.InkAt(0.55f)), new(1f, UiPalette.InkAt(0f))];
        painter.FillGradient(block, wash, UiGradientDirection.Horizontal);
        painter.FillRect(new UiRect(x, block.Y, accentWidth, block.Height), Accent);
        painter.DrawLabel(type.Mono, "MISSION OBJECTIVE", new UiVec2(textX, y), labelSize, Accent, 0.28f);
        painter.DrawText(type.Body, wrapped, new UiVec2(textX, y + labelHeight + context.Px(8f)), textSize, Text);
    }

    private static void DrawCompass(UiPainter painter, LayoutContext context, UiTheme type, float headingRadians)
    {
        float width = MathF.Min(context.Px(320f), context.SafeRect.Width * 0.26f);
        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float x = centreX - (width * 0.5f);
        float y = context.SafeRect.Y + context.Px(12f);
        float lineY = y + context.Px(30f);
        float heading = RemasteredHudPresentation.HeadingDegrees(headingRadians);
        const float visibleDegrees = 100f;
        const float halfVisible = visibleDegrees * 0.5f;
        int centreDegrees = ((int)MathF.Round(heading) + 360) % 360;

        // The rule fades out at both ends rather than stopping, so the dial reads as a window onto a ring.
        Span<UiGradientStop> rule =
        [
            new(0f, UiPalette.White(0f)),
            new(0.2f, UiPalette.White(0.18f)),
            new(0.8f, UiPalette.White(0.18f)),
            new(1f, UiPalette.White(0f)),
        ];
        painter.FillGradient(new UiRect(x, lineY, width, context.Px(1f)), rule, UiGradientDirection.Horizontal);

        float firstTick = MathF.Ceiling((heading - halfVisible) / 10f) * 10f;
        for (float degrees = firstTick; degrees <= heading + halfVisible; degrees += 10f)
        {
            int normalized = ((int)degrees % 360 + 360) % 360;
            float tickX = centreX + (((degrees - heading) / visibleDegrees) * width);
            bool major = normalized % 30 == 0;
            float tickHeight = context.Px(major ? 7f : 3f);
            float edge = 1f - MathF.Abs((degrees - heading) / halfVisible);
            painter.FillRect(
                new UiRect(tickX - context.Px(0.5f), lineY, context.Px(1f), tickHeight),
                UiPalette.Fade(Muted, (major ? 0.8f : 0.4f) * Math.Clamp(edge * 2f, 0f, 1f)));
        }

        // The heading marker: an accent diamond on the rule, the bearing in monospace over it.
        painter.FillDiamond(new UiVec2(centreX, lineY), context.Px(7f), Accent);
        string readout = $"{centreDegrees:000}";
        float readoutSize = context.Px(14f);
        float readoutWidth = UiPainter.MeasureLabel(type.Mono, readout, readoutSize, 0.12f);
        painter.DrawLabel(type.Mono, readout, new UiVec2(centreX - (readoutWidth * 0.5f), y + context.Px(4f)), readoutSize, Text, 0.12f);

        for (int cardinal = 0; cardinal < 360; cardinal += 45)
        {
            float relative = cardinal - heading;
            if (relative > 180f)
            {
                relative -= 360f;
            }
            else if (relative < -180f)
            {
                relative += 360f;
            }

            if (MathF.Abs(relative) > halfVisible)
            {
                continue;
            }

            string label = CompassCardinal(cardinal);
            bool major = cardinal % 90 == 0;
            float labelSize = context.Px(major ? 16f : 13f);
            float labelWidth = UiPainter.MeasureLabel(type.Heading, label, labelSize, 0.12f);
            float edge = Math.Clamp((1f - (MathF.Abs(relative) / halfVisible)) * 2f, 0f, 1f);
            painter.DrawLabel(
                type.Heading,
                label,
                new UiVec2(centreX + ((relative / visibleDegrees) * width) - (labelWidth * 0.5f), lineY + context.Px(12f)),
                labelSize,
                UiPalette.Fade(major ? Text : Muted, edge),
                0.12f);
        }
    }

    private static string CompassCardinal(int degrees) => degrees switch
    {
        0 => "N",
        45 => "NE",
        90 => "E",
        135 => "SE",
        180 => "S",
        225 => "SW",
        270 => "W",
        315 => "NW",
        _ => string.Empty,
    };

    private static void DrawVitals(UiPainter painter, LayoutContext context, UiTheme type, float health)
    {
        float width = context.Px(326f);
        float height = context.Px(74f);
        float x = context.SafeRect.X + context.Px(34f);
        float y = context.SafeRect.Bottom - context.Px(38f) - height;
        float fraction = Math.Clamp(health, 0f, 1f);
        Bgra32 healthColour = RemasteredHudPresentation.HealthColour(fraction);

        painter.DrawLabel(type.Mono, "HEALTH", new UiVec2(x, y), context.Px(11f), Muted, 0.28f);
        float valueSize = context.Px(52f);
        var valueBox = new UiRect(x, y + context.Px(18f), context.Px(92f), valueSize * 0.85f);
        painter.DrawLabel(
            type.Display,
            $"{(int)MathF.Round(fraction * 100f):00}",
            new UiVec2(x, UiRowStyle.CapsCenterY(type.Display, valueSize, valueBox)),
            valueSize,
            healthColour,
            0.02f);

        float barX = x + context.Px(92f);
        float barY = y + context.Px(56f);
        float barWidth = width - context.Px(92f);
        DrawMeter(painter, new UiRect(barX, barY, barWidth, context.Px(4f)), fraction, healthColour);

        // One segment per tenth, the design's progress marks, under the bar.
        KitDraw.Segments(painter, new UiVec2(barX, barY + context.Px(9f)), (int)MathF.Ceiling(fraction * 10f), 10, (326f - 92f - 36f) / 10f, 2f, 4f, UiPalette.Fade(healthColour, 0.7f));
    }

    private static void DrawWeapon(
        UiPainter painter,
        LayoutContext context,
        UiTheme type,
        in RemasteredHudSnapshot snapshot)
    {
        if (snapshot.Weapon is not { } weapon)
        {
            return;
        }

        float right = context.SafeRect.Right - context.Px(34f);
        float y = context.SafeRect.Bottom - context.Px(118f);
        float left = right - context.Px(380f);
        float ammoGap = context.Px(26f);
        float ammoAreaWidth = context.Px(176f);
        float weaponRight = right - ammoAreaWidth - ammoGap;
        float labelSize = context.Px(11f);

        painter.DrawLabel(type.Mono, RemasteredHudPresentation.WeaponClass(weapon).ToUpperInvariant(), new UiVec2(left, y), labelSize, Muted, 0.28f);
        float nameSize = context.Px(30f);
        var nameBox = new UiRect(left, y + context.Px(20f), weaponRight - left, nameSize * 0.85f);
        painter.DrawLabel(type.Heading, weapon.DisplayName.ToUpperInvariant(), new UiVec2(left, UiRowStyle.CapsCenterY(type.Heading, nameSize, nameBox)), nameSize, Text, 0.06f);
        painter.FillRect(
            new UiRect(left, y + context.Px(55f), Math.Max(context.Px(42f), weaponRight - left), context.Px(2f)),
            Accent.WithAlpha(185));

        string state = RemasteredHudPresentation.WeaponStateLabel(snapshot.WeaponState);
        if (state.Length > 0)
        {
            painter.FillDiamond(new UiVec2(left + context.Px(3f), y + context.Px(71f)), context.Px(6f), Accent);
            painter.DrawLabel(type.Mono, state.ToUpperInvariant(), new UiVec2(left + context.Px(14f), y + context.Px(64f)), labelSize, Accent, 0.18f);
        }

        (string magazine, string reserve) =
            RemasteredHudPresentation.AmmoColumns(weapon, snapshot.RoundsInClip, snapshot.ReserveAmmo);
        float ammoSize = context.Px(48f);
        float reserveWidth = UiPainter.MeasureLabel(type.Display, reserve, ammoSize, 0.02f);
        float reserveX = right - reserveWidth;
        float ammoTop = y + context.Px(18f);
        var ammoBox = new UiRect(0f, ammoTop, 0f, ammoSize * 0.85f);
        const string ReserveLabel = "RESERVE";
        float reserveLabelWidth = UiPainter.MeasureLabel(type.Mono, ReserveLabel, labelSize, 0.28f);
        painter.DrawLabel(type.Mono, ReserveLabel, new UiVec2(right - reserveLabelWidth, y), labelSize, Muted, 0.28f);
        painter.DrawLabel(type.Display, reserve, new UiVec2(reserveX, UiRowStyle.CapsCenterY(type.Display, ammoSize, ammoBox)), ammoSize, Muted, 0.02f);

        float magazineWidth = UiPainter.MeasureLabel(type.Display, magazine, ammoSize, 0.02f);
        float magazineRight = reserveX - context.Px(30f);
        const string MagazineLabel = "MAG";
        float magazineLabelWidth = UiPainter.MeasureLabel(type.Mono, MagazineLabel, labelSize, 0.28f);
        painter.DrawLabel(type.Mono, MagazineLabel, new UiVec2(magazineRight - magazineLabelWidth, y), labelSize, Accent, 0.28f);
        painter.DrawLabel(type.Display, magazine, new UiVec2(magazineRight - magazineWidth, UiRowStyle.CapsCenterY(type.Display, ammoSize, ammoBox)), ammoSize, Text, 0.02f);
        painter.FillRect(
            new UiRect(reserveX - context.Px(15f), ammoTop + context.Px(4f), context.Px(1f), context.Px(34f)),
            UiPalette.White(0.18f));
    }

    private static void DrawCrosshair(
        UiPainter painter,
        LayoutContext context,
        WeaponType? weapon,
        bool sniperScoped)
    {
        if (!RemasteredHudPresentation.ShowsCrosshair(weapon, sniperScoped))
        {
            return;
        }

        // Dead centre, in every view. Third person does not move it: the shot converges on what the
        // centre of the screen is over (PlayerCamera.AimConvergencePointUnits), rather than the cross
        // chasing a shot that leaves an eye the camera is no longer sitting at.
        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float centreY = context.SafeRect.Y + (context.SafeRect.Height * 0.5f);
        float gap = context.Px(7f);
        float arm = context.Px(7f);
        float thickness = MathF.Max(1f, context.Px(1.5f));
        Bgra32 cross = Text.WithAlpha(220);
        painter.FillRect(new UiRect(centreX - gap - arm, centreY - (thickness * 0.5f), arm, thickness), cross);
        painter.FillRect(new UiRect(centreX + gap, centreY - (thickness * 0.5f), arm, thickness), cross);
        painter.FillRect(new UiRect(centreX - (thickness * 0.5f), centreY - gap - arm, thickness, arm), cross);
        painter.FillRect(new UiRect(centreX - (thickness * 0.5f), centreY + gap, thickness, arm), cross);
        painter.FillRect(new UiRect(centreX - (thickness * 0.5f), centreY - (thickness * 0.5f), thickness, thickness), Accent);
    }

    private static void DrawInteraction(UiPainter painter, LayoutContext context, UiTheme type, string? actionSprite)
    {
        string label = RemasteredHudPresentation.InteractionLabel(actionSprite);
        if (label.Length == 0)
        {
            return;
        }

        // The design's key hint: the key in a cap, then what it does, centred under the crosshair.
        const string Key = "E";
        float textSize = context.Px(16f);
        string action = label.ToUpperInvariant();
        UiVec2 cap = KitDraw.MeasureKeyCap(context, type, Key);
        float actionWidth = UiPainter.MeasureLabel(type.Heading, action, textSize, 0.16f);
        float total = cap.X + context.Px(10f) + actionWidth;
        float x = context.SafeRect.X + ((context.SafeRect.Width - total) * 0.5f);
        float y = context.SafeRect.Y + (context.SafeRect.Height * 0.5f) + context.Px(47f);
        UiRect box = KitDraw.KeyCap(painter, type, Key, new UiVec2(x, y));
        painter.DrawLabel(type.Heading, action, new UiVec2(box.Right + context.Px(10f), UiRowStyle.CapsCenterY(type.Heading, textSize, box)), textSize, Text, 0.16f);
    }

    private static void DrawMeter(UiPainter painter, UiRect rect, float value, Bgra32 colour)
    {
        painter.FillRect(rect, UiPalette.White(0.1f));
        float fill = rect.Width * Math.Clamp(value, 0f, 1f);
        if (fill > 0f)
        {
            painter.FillRect(new UiRect(rect.X, rect.Y, fill, rect.Height), colour);
        }
    }

    /// <summary>The badge's face. Opaque, so each layer drawn over it composites predictably.</summary>
    private static Bgra32 BadgeFace => Bgra32.FromRgb(13, 16, 21);

    private static Bgra32 Text => UiPalette.Text;
    private static Bgra32 Muted => UiPalette.TextSecondary;
    private static Bgra32 Accent => UiPalette.Accent;
    private static Bgra32 Signal => UiPalette.Success;
}
