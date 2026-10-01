using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Tests;

/// <summary>An <see cref="IUiRenderer"/> that records what it is asked to draw, so the painter's output can be asserted without a GPU or a canvas.</summary>
internal sealed class RecordingUiRenderer : IUiRenderer
{
    public List<RecordedQuad> Quads { get; } = [];

    public List<UiGlyph> Glyphs { get; } = [];

    public List<RecordedBackdrop> Backdrops { get; } = [];

    public void DrawQuad(UiImage image, in UiVertex topLeft, in UiVertex topRight, in UiVertex bottomRight, in UiVertex bottomLeft, UiBlend blend) =>
        Quads.Add(new RecordedQuad(image, [topLeft, topRight, bottomRight, bottomLeft], blend));

    public void DrawGlyph(UiFont font, in UiGlyph glyph, Bgra32 color, in UiTextEffects? effects, UiBlend blend) =>
        Glyphs.Add(glyph);

    public void DrawBackdrop(ReadOnlySpan<UiVec2> polygon, float sigmaPx, Bgra32 tint, float opacity, float zoom) =>
        Backdrops.Add(new RecordedBackdrop(polygon.ToArray(), sigmaPx, tint, opacity));

    internal sealed record RecordedQuad(UiImage Image, UiVertex[] Corners, UiBlend Blend);

    internal sealed record RecordedBackdrop(UiVec2[] Polygon, float SigmaPx, Bgra32 Tint, float Opacity);
}
