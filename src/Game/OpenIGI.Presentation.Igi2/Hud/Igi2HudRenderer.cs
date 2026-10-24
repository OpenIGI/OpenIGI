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
    /// <summary>The display the status art is laid out for: 1280×720, so a 1440-line display draws it twice over.</summary>
    public const int AuthoredWidth = 1280;

    /// <summary>The height of the display the status art is laid out for.</summary>
    public const int AuthoredHeight = 720;

    /// <inheritdoc/>
    public void Draw(SpriteBatch batch, ITitlePlayer player, int screenWidth, int screenHeight, int requestedScale = UiScale.Automatic)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (player is not Igi2TitlePlayer title || screenWidth <= 0 || screenHeight <= 0) { return; }
        // The status art by the interface scale every title's is drawn at (UiScale), for the display I.G.I. 2's was laid out at:
        // retail places it in the display's own pixels, so it lays out here in the pixels of a display that many times smaller.
        // World-space sights and view flashes stay in pixels.
        _scale = UiScale.Resolve(requestedScale, screenWidth, screenHeight, AuthoredWidth, AuthoredHeight);
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
        // The remastered display in place of the status display, inventory, action row and messages, where it is chosen.
        bool remastered = RemasteredChosen;
        if (remastered)
        {
            DrawRemastered(batch, title, screenWidth, screenHeight, requestedScale);
        }
        else
        {
            DrawMessages(batch, title, width, height);
            if (!title.IsCutscenePlaying && !title.Human.IsDead)
            {
                DrawStatus(batch, title, width, height);
                DrawInventory(batch, title);
                DrawInteraction(batch, title.Human, width, height);
            }
        }

        // The remastered crosshair stands in for the original's sight where it is chosen; scopes keep their own.
        if (!(remastered && HudCVars.Crosshair.Value))
        {
            DrawSight(batch, title, screenWidth, screenHeight);
        }

        // Through the visor the hit washes the corners itself; otherwise the original's red corners.
        if (!title.IsCutscenePlaying) { DrawEffects(batch, title.Human, screenWidth, screenHeight, damage: !(remastered && HudCVars.HudFx.Value)); }
    }

    /// <summary>
    /// Whether the player aims with a sight now: not in a cutscene, alive, at no timed action, in the first-person camera with a
    /// weapon in hand and no scope or zoom on -- as the original draws its sight.
    /// </summary>
    /// <param name="title">The player.</param>
    /// <returns>Whether a sight shows.</returns>
    internal static bool SightShown(Igi2TitlePlayer title) =>
        !(title.IsCutscenePlaying || title.Human.IsDead || title.Human.IsProgressBar
            || title.Human.IsThirdPerson || title.Human.View.IsOverride || title.Human.CameraMode != 1
            || title.Weapons is not { Inventory.Current: not null } weapons || weapons.IsZoomFireModeActive);

    private static void DrawSight(SpriteBatch batch, Igi2TitlePlayer title, int screenWidth, int screenHeight)
    {
        if (!SightShown(title) || title.Weapons is not { Inventory.Current: { } held })
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
