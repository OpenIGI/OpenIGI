using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Igi2.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <summary>
/// I.G.I. 2's look for the remastered display: its own status blue, glass of deep night blue, small chamfers and paired rules,
/// a shimmer now and then, and a fine-lined holographic visor. Every part where the original put it.
/// </summary>
public static class Igi2HudTheme
{
    private static readonly Bgra32 Blue = Bgra32.FromRgba(102, 153, 255, 255);

    /// <summary>Gets the theme.</summary>
    public static HudTheme Theme { get; } = new(
        "I.G.I. 2",
        Igi2HudRenderer.AuthoredWidth,
        Igi2HudRenderer.AuthoredHeight,
        new HudPalette(
            Primary: Blue,
            Dim: Bgra32.FromRgba(58, 92, 168, 210),
            Highlight: Bgra32.FromRgba(214, 230, 255, 255),
            Accent: Bgra32.FromRgba(255, 196, 92, 255),
            Danger: Bgra32.FromRgba(255, 74, 64, 255),
            Glass: Bgra32.FromRgba(10, 22, 48, 70),
            Health: Health,
            Visibility: Visibility),
        new HudMotifs(HudCorner.Chamfer, CornerSize: 5f, Hairline: 1f, BracketLength: 7f, Shimmer: true, DoubleRules: true),
        new HudFxSettings
        {
            Intensity = 1f,
            Curvature = 0.008f,
            FringePixels = 1f,
            ScanlineDensity = 540f,
            ScanlineStrength = 0.14f,
            Glow = 0.65f,
            DamageColour = (1f, 0.08f, 0.06f),
        },
        new Dictionary<HudElement, HudAnchor>
        {
            // humanplayer.c HumanPlayer_StatusDisplayHandler: the inventory from five across and five down, the status at the lower
            // left, the weapon at the lower right, the action row from 175 across and 90 up, the progress bar centred low.
            [HudElement.Inventory] = new(HudEdgeX.Left, 5f, HudEdgeY.Top, 5f),
            [HudElement.Vitals] = new(HudEdgeX.Left, 16f, HudEdgeY.Bottom, 16f),
            [HudElement.Weapon] = new(HudEdgeX.Right, 16f, HudEdgeY.Bottom, 16f),
            [HudElement.Actions] = new(HudEdgeX.Left, 175f, HudEdgeY.Bottom, 90f),
            [HudElement.Progress] = new(HudEdgeX.Centre, 0f, HudEdgeY.Bottom, 117f),
            [HudElement.Timer] = new(HudEdgeX.Centre, 0f, HudEdgeY.Top, 14f),
            [HudElement.Messages] = new(HudEdgeX.Centre, 0f, HudEdgeY.Top, 36f),
        });

    /// <summary>The health gauge's colour, retail's ramp: red when spent, blue when whole.</summary>
    /// <param name="health">The health, nought to one.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 Health(float health)
    {
        float h = Math.Clamp(health, 0f, 1f);
        return Rgb(0.8f - (h * 0.52f), (h * 0.22f) + 0.2f, (h * 0.7f) + 0.2f);
    }

    /// <summary>The visibility meter's colour, retail's ramp: blue in the dark, red in full light.</summary>
    /// <param name="visibility">The visibility, nought to one.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 Visibility(float visibility)
    {
        float b = Math.Clamp(visibility - 0.5f, 0f, 0.5f) * 2f;
        return Rgb((b + 1f) * 0.4f, 0.6f - (b * 0.4f), 1f - (b * 0.8f));
    }

    private static Bgra32 Rgb(float r, float g, float b) =>
        Bgra32.FromRgba((byte)(Math.Clamp(r, 0f, 1f) * 255f), (byte)(Math.Clamp(g, 0f, 1f) * 255f), (byte)(Math.Clamp(b, 0f, 1f) * 255f), 255);
}

/// <summary>I.G.I. 2's pictures for the remastered display: its status archive's sprites, by name.</summary>
/// <param name="assets">Where the status display's sprites are loaded from.</param>
internal sealed class Igi2HudIcons(Func<Igi2SpriteResources?> assets) : IHudIconSource
{
    /// <inheritdoc/>
    public bool TryMeasure(HudIcon icon, out float width, out float height)
    {
        if (icon.IsSome && assets()?.Get(icon.Name) is { Asset.Frames.Count: > 0 } sprite)
        {
            width = sprite.Asset.Frames[0].Width;
            height = sprite.Asset.Frames[0].Height;
            return true;
        }

        width = 0f;
        height = 0f;
        return false;
    }

    /// <inheritdoc/>
    public void Draw(SpriteBatch batch, HudIcon icon, float x, float y, float scale, Bgra32 colour)
    {
        if (icon.IsSome && assets()?.Get(icon.Name) is { Asset.Frames.Count: > 0 } sprite)
        {
            sprite.DrawFrame(batch, sprite.Asset.Frames[0], x, y, scale, scale, colour);
        }
    }

    /// <inheritdoc/>
    public OpenIGI.Presentation.Rendering.SpriteResource? Resolve(HudIcon icon) => icon.IsSome ? assets()?.Get(icon.Name) : null;
}
