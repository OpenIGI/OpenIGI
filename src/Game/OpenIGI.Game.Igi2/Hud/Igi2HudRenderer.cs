using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.Hud;

/// <summary>The weapon sight for the title-owned player, independent of the host's IGI1 HUD state.</summary>
public sealed class Igi2HudRenderer : ITitleHudPresenter
{
    /// <inheritdoc/>
    public void Draw(SpriteBatch batch, ITitlePlayer player, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (player is not Igi2TitlePlayer title || title.IsCutscenePlaying || title.Human.IsDead
            || title.Human.IsThirdPerson || title.Human.View.IsOverride || title.Human.CameraMode != 1
            || title.Weapons is not { Inventory.Current: { } held } weapons || weapons.IsZoomFireModeActive)
        {
            return;
        }
        Config.Igi2PlayerProfile? profile = title.GameConfig?.Active;
        float alpha = profile?.CrossHairAlpha ?? 1f;
        if (alpha <= 0f) { return; }
        (float r, float g, float b) = profile?.CrossHairColour ?? (1f, 0.25f, 0f);
        bool thick = profile?.ThickCrossHair == true;
        Span<Igi2SightLine> lines = stackalloc Igi2SightLine[Igi2WeaponSight.MaximumLines];
        // humanplayer.c HumanPlayer_GetWeaponPrecisionHandler supplies both settled precision terms.
        float precision = ((Fpu)title.Human.ActualWeaponPrecision + title.Human.ActualModifiedPrecision).F;
        int count = Igi2WeaponSight.Write(lines, held.WeaponType.SightDisplayType, precision,
            title.Human.View.FieldOfView, screenWidth, screenHeight, thick);
        for (int i = 0; i < count; i++)
        {
            Igi2SightLine line = lines[i];
            Bgra32 colour = Bgra32.FromRgba(Channel(r), Channel(g), Channel(b), Channel(alpha * line.Alpha));
            Stroke(batch, line, thick ? 2f : 1f, colour);
        }
    }

    private static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);

    private static void Stroke(SpriteBatch batch, Igi2SightLine line, float thickness, Bgra32 colour)
    {
        float dx = line.X2 - line.X1, dy = line.Y2 - line.Y1;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0f) { return; }
        float nx = -dy / length * thickness * 0.5f, ny = dx / length * thickness * 0.5f;
        batch.DrawQuad(TextureHandle.None,
            new Vertex2D(line.X1 + nx, line.Y1 + ny, 0f, 0f, colour),
            new Vertex2D(line.X2 + nx, line.Y2 + ny, 1f, 0f, colour),
            new Vertex2D(line.X2 - nx, line.Y2 - ny, 1f, 1f, colour),
            new Vertex2D(line.X1 - nx, line.Y1 - ny, 0f, 1f, colour));
    }
}
