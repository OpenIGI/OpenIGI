using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <summary>The retail status display for the title-owned player.</summary>
public sealed partial class Igi2HudRenderer : ITitleHudPresenter
{
    /// <inheritdoc/>
    public void Draw(SpriteBatch batch, ITitlePlayer player, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (player is not Igi2TitlePlayer title || screenWidth <= 0 || screenHeight <= 0) { return; }
        // Keep the authored status art readable at modern resolutions; world-space sights and view flashes stay in pixels.
        _scale = screenHeight / 720f;
        float width = screenWidth / _scale, height = screenHeight / _scale;
        // The open computer covers the view and the status display.
        MeasureComputer(title, screenWidth, screenHeight);
        if (title.IsComputerShown)
        {
            DrawComputer(batch, title, screenWidth, screenHeight);
            return;
        }

        DrawCutsceneFraming(batch, title, screenWidth, screenHeight);
        if (!title.IsCutscenePlaying) { DrawZoomOverlay(batch, title, width, height); }
        DrawMessages(batch, title, width, height);
        if (!title.IsCutscenePlaying && !title.Human.IsDead)
        {
            DrawStatus(batch, title, width, height);
            DrawInteraction(batch, title.Human, width, height);
        }
        DrawSight(batch, title, screenWidth, screenHeight);
        if (!title.IsCutscenePlaying) { DrawEffects(batch, title.Human, screenWidth, screenHeight); }
    }

    private static void DrawSight(SpriteBatch batch, Igi2TitlePlayer title, int screenWidth, int screenHeight)
    {
        if (title.IsCutscenePlaying || title.Human.IsDead || title.Human.IsProgressBar
            || title.Human.IsThirdPerson || title.Human.View.IsOverride || title.Human.CameraMode != 1
            || title.Weapons is not { Inventory.Current: { } held } weapons || weapons.IsZoomFireModeActive)
        {
            return;
        }
        OpenIGI.Game.Igi2.Config.Igi2PlayerProfile? profile = title.GameConfig?.Active;
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
