using OpenIGI.Core.Graphics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.Hud;

/// <summary>
/// Project I.G.I.'s retail HUD as its player sees it, wherever that player stands: the weapon readout or the optics that replace
/// it, the player's status, and the washes over the screen. Each draws into a batch already begun under the HUD's projection, so
/// the game's own mission (<see cref="Igi1HudOverlay"/>) and the player standing in another game's level (<see cref="Igi1TitleHud"/>)
/// draw the same.
/// </summary>
internal static class Igi1HudSections
{
    /// <summary>The ammunition an M16A2's grenade launcher fires.</summary>
    private const string M203Ammo = "AMMO_ID_M203";

    /// <summary>The weapon that carries the launcher.</summary>
    private const string M203Weapon = "WEAPON_ID_M16A2";

    /// <summary>
    /// Draws the weapon readout -- or, with the binoculars up or the Dragunov scoped, the optics' overlay, which replaces it
    /// (<c>0x473040</c>'s latch branch never calls the parent handler).
    /// </summary>
    /// <param name="batch">The batch, begun.</param>
    /// <param name="hud">The retail HUD.</param>
    /// <param name="player">The player.</param>
    /// <param name="carried">What it carries.</param>
    /// <param name="ammoTypes">The ammunition table, which names the launcher's grenades; <see langword="null"/> for none.</param>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <param name="scale">The interface scale.</param>
    /// <param name="tick">The tick, for what blinks.</param>
    /// <param name="binocularTargets">The boxes over what the binoculars picked out.</param>
    /// <param name="binocularRangeMetres">The range to what is centred, or <see langword="null"/>.</param>
    public static void Weapon(
        SpriteBatch batch,
        IHudPresenter hud,
        PlayerCamera player,
        PlayerWeapons carried,
        AmmoTypeRegistry? ammoTypes,
        int width,
        int height,
        int scale,
        int tick,
        IReadOnlyList<EnemyOverlayBox> binocularTargets,
        float? binocularRangeMetres)
    {
        if (player.IsBinocularsOpen)
        {
            hud.DrawBinocularOverlay(batch, width, height, binocularTargets, player.Yaw, tick, binocularRangeMetres);
            return;
        }

        if (player.IsSniperScoped)
        {
            hud.DrawSniperScope(batch, width, height);
            return;
        }

        CarriedWeapon? held = carried.InHand;
        int m203Reserve = held is { Weapon.ScriptId: M203Weapon } && ammoTypes?.Types.FirstOrDefault(static ammo => ammo.ScriptId == M203Ammo) is { } m203
            ? carried.Ammunition.Count(m203.AmmoId)
            : 0;
        hud.Draw(
            batch,
            held?.Weapon,
            held?.Gun.RoundsInClip ?? 0,
            held is { } primary ? primary.Gun.Reserve(carried.Ammunition) : 0,
            held?.Gun.State.SecondSubCounter ?? 0,
            m203Reserve,
            width,
            height,
            scale);
    }

    /// <summary>Draws the health bar, the timed-use ring and the mission's clock.</summary>
    /// <param name="batch">The batch, begun.</param>
    /// <param name="hud">The retail HUD.</param>
    /// <param name="player">The player.</param>
    /// <param name="healthFraction">How much health is left.</param>
    /// <param name="timerRemainingTicks">Ticks left on a timed mission, or <see langword="null"/>.</param>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <param name="scale">The interface scale.</param>
    /// <remarks>
    /// The human-owned display stays when a full-body interaction hides the hands and weapon: that is precisely when the timed-use
    /// bar is needed. In the original's order it follows the weapon handler and precedes StatusScreen's timer.
    /// </remarks>
    public static void Status(
        SpriteBatch batch, IHudPresenter hud, PlayerCamera player, float healthFraction, int? timerRemainingTicks, int width, int height, int scale) =>
        hud.DrawPlayerStatus(batch, healthFraction, player.InteractionProgress, timerRemainingTicks, width, height, scale);

    /// <summary>Draws the damage wash and the flashbang's white over the screen.</summary>
    /// <param name="batch">The batch, begun.</param>
    /// <param name="life">The player's life, whose washes they are.</param>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <param name="flashStrength">How strong the damage wash is drawn, nought to one.</param>
    /// <remarks>
    /// The damage wash is a screen-filling red quad whose four vertex alphas are the four corners <c>sub_411300</c> fades
    /// (<c>HumanPlayer_DrawDamageFlash</c>, <c>0x4bb770</c>): the strength scales the alpha only, so a hit from the left is still
    /// redder on the left and still fades over the same ticks. The native flash handler writes a uniform white over it, stronger
    /// inside the grenade's radius, so the retail flash wins.
    /// </remarks>
    public static void Washes(SpriteBatch batch, Igi1PlayerLife life, float width, float height, float flashStrength)
    {
        if (life.DamageFlash.IsActive && flashStrength > 0f)
        {
            Bgra32 Red(float alpha) => new(0, 0, 255, (byte)Math.Clamp((int)(alpha * flashStrength * 255f), 0, 255));
            batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(0f, 0f, 0f, 0f, Red(life.DamageFlash.TopLeft)),
                new Vertex2D(width, 0f, 1f, 0f, Red(life.DamageFlash.TopRight)),
                new Vertex2D(width, height, 1f, 1f, Red(life.DamageFlash.BottomRight)),
                new Vertex2D(0f, height, 0f, 1f, Red(life.DamageFlash.BottomLeft)));
        }

        if (life.FlashbangFlash.IsActive)
        {
            Bgra32 white = new(255, 255, 255, (byte)Math.Clamp((int)(Math.Min(life.FlashbangFlash.Strength, 1f) * 255f), 0, 255));
            batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(0f, 0f, 0f, 0f, white),
                new Vertex2D(width, 0f, 1f, 0f, white),
                new Vertex2D(width, height, 1f, 1f, white),
                new Vertex2D(0f, height, 0f, 1f, white));
        }
    }
}
