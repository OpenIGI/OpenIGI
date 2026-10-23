using OpenIGI.Core.Graphics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Igi1.Hud;
using OpenIGI.Presentation.Igi1.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Project I.G.I.'s mission HUD over the finished frame: the weapon readout or the optics, the status, the washes and the remaster's HUD.</summary>
/// <param name="device">The device it draws with.</param>
/// <param name="gameplay">The first game's mission, whose player and objectives it reads.</param>
internal sealed class Igi1HudOverlay(IGraphicsDevice device, Igi1Gameplay gameplay) : IMissionHudOverlay
{
    private readonly IGraphicsDevice _device = device;
    private readonly Igi1Gameplay _gameplay = gameplay;
    private readonly HudModel _model = new();
    private RemasteredHud? _remastered;

    /// <inheritdoc/>
    public void DrawWeaponHud(SpriteBatch batch, in MissionHudFrame frame)
    {
        ArgumentNullException.ThrowIfNull(batch);
        MissionWorld mission = _gameplay.World;
    if (RemasteredHudPresentation.UsesRetailWeaponHud(
            frame.RemasterHud,
            mission.Player?.IsBinocularsOpen == true,
            frame.RemasteredOptics)
        && frame.ShowFirstPersonHud
        && mission.Player is { Weapons: { } carried }
        && frame.Hud is { IsReady: true } retailHud)
    {
        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        // Scoped, the Dragunov's overlay REPLACES the weapon HUD -- 0x473040's latch branch
        // never calls the parent handler.
        if (mission.Player.IsBinocularsOpen)
        {
            retailHud.DrawBinocularOverlay(
                batch,
                frame.Width,
                frame.Height,
                frame.BinocularTargets,
                mission.Player.Yaw,
                frame.TickCount,
                frame.BinocularRangeMetres);
        }
        else if (mission.Player.IsSniperScoped)
        {
            retailHud.DrawSniperScope(batch, frame.Width, frame.Height);
        }
        else
        {
            CarriedWeapon? held = carried.InHand;
            int m203Reserve = held is { Weapon.ScriptId: "WEAPON_ID_M16A2" }
                && mission.AmmoTypes?.Types.FirstOrDefault(
                    ammo => ammo.ScriptId == "AMMO_ID_M203") is { } m203
                    ? carried.Ammunition.Count(m203.AmmoId)
                    : 0;
            retailHud.Draw(
                batch,
                held?.Weapon,
                held?.Gun.RoundsInClip ?? 0,
                held is { } primary ? primary.Gun.Reserve(carried.Ammunition) : 0,
                held?.Gun.State.SecondSubCounter ?? 0,
                m203Reserve,
                frame.Width,
                frame.Height,
                frame.UiScale);
        }

        batch.End();
    }
    }

    /// <inheritdoc/>
    public void DrawRetailStatus(SpriteBatch batch, in MissionHudFrame frame)
    {
        ArgumentNullException.ThrowIfNull(batch);
        MissionWorld mission = _gameplay.World;
    // Retail action sprites are deliberately retained for the faithful UI. The remaster's
    // corresponding context label is part of the single modern HUD draw below.
    if (!frame.RemasterHud
        && frame.ShowFirstPersonHud
        && frame.Hud is { IsReady: true } retailActions
        && mission.Player?.ActionSprite is { } actionSprite)
    {
        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        retailActions.DrawAction(
            batch,
            actionSprite,
            frame.Width,
            frame.Height,
            frame.UiScale);
        batch.End();
    }

    // The mission's status messages. Kept off screen while the computer is up, as the rest
    // of the HUD is: the map is a full-screen readout of the same mission and a comms card
    // over it would be the same information twice.
    if (!frame.TacticalMessages
        && frame.ShowFirstPersonHud
        && mission.Objectives is { } messageRuntime
        && frame.Hud is { IsReady: true } retailMessages)
    {
        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        retailMessages.DrawStatusMessages(
            batch,
            messageRuntime.DisplayedMessages.Where(static message => !message.IsCutscene),
            frame.Width,
            frame.Height,
            frame.UiScale);
        batch.End();
    }

    // The human-owned retail display remains alive when a full-body interaction hides the
    // hands and weapon: that is precisely when HumanPlayer's timed-use bar is needed. In the
    // original dispatch order this follows the weapon handler and precedes StatusScreen's timer.
    if (!frame.RemasterHud
        && frame.ShowPlayerHud
        && mission.Player is { } retailPlayer
        && frame.Hud is { IsReady: true } retailStatus)
    {
        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        retailStatus.DrawPlayerStatus(
            batch,
            mission.PlayerHealth.RemainingFraction,
            retailPlayer.InteractionProgress,
            mission.Objectives?.InterfaceTimerRemainingTicks,
            frame.Width,
            frame.Height,
            frame.UiScale);
        batch.End();
    }
    }

    /// <inheritdoc/>
    public void DrawWashes(SpriteBatch batch, in MissionHudFrame frame)
    {
        ArgumentNullException.ThrowIfNull(batch);
        MissionWorld mission = _gameplay.World;
    // The damage wash goes over everything, in its own 2D pass: a screen-filling red quad whose
    // four vertex alphas are the four corners sub_411300 fades (HumanPlayer_DrawDamageFlash,
    // 0x4bb770). Outside the player-mode block on purpose -- it has to survive the player's death,
    // when the arms and the HUD are gone but the last thing the screen showed is still red.
    float flashStrength = Math.Clamp(HudCVars.DamageFlash.Value, 0f, 1f);
    if (mission.DamageFlash.IsActive && flashStrength > 0f)
    {
        float flashWidth = frame.Width;
        float flashHeight = frame.Height;

        // The strength scales the alpha only. The four corners still carry their own values
        // and still decay on the original's schedule, so a hit from the left is still redder
        // on the left and still fades over the same number of ticks -- it is the peak that
        // comes down, not the cue.
        Bgra32 Red(float alpha) =>
            new(0, 0, 255, (byte)Math.Clamp((int)(alpha * flashStrength * 255f), 0, 255));

        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(0f, 0f, 0f, 0f, Red(mission.DamageFlash.TopLeft)),
            new Vertex2D(flashWidth, 0f, 1f, 0f, Red(mission.DamageFlash.TopRight)),
            new Vertex2D(flashWidth, flashHeight, 1f, 1f, Red(mission.DamageFlash.BottomRight)),
            new Vertex2D(0f, flashHeight, 0f, 1f, Red(mission.DamageFlash.BottomLeft)));
        batch.End();
    }

    // The native player flash handler writes a uniform white screen field. It is stronger
    // when the eye is inside the grenade radius (2) and still fully white at the first
    // frame of the 25 m visible effect (1); the close field simply keeps the quad opaque
    // for longer. This pass follows the red damage wash so the retail flash wins.
    if (mission.FlashbangFlash.IsActive)
    {
        byte whiteAlpha = (byte)Math.Clamp(
            (int)(Math.Min(mission.FlashbangFlash.Strength, 1f) * 255f),
            0,
            255);
        Bgra32 white = new(255, 255, 255, whiteAlpha);
        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(0f, 0f, 0f, 0f, white),
            new Vertex2D(frame.Width, 0f, 1f, 0f, white),
            new Vertex2D(frame.Width, frame.Height, 1f, 1f, white),
            new Vertex2D(0f, frame.Height, 0f, 1f, white));
        batch.End();
    }
    }

    /// <inheritdoc/>
    public void DrawRemasteredHud(SpriteBatch batch, in MissionHudFrame frame)
    {
        ArgumentNullException.ThrowIfNull(batch);
        MissionWorld mission = _gameplay.World;
    // HumanPlayer_DrawStatus keeps drawing states 30/31/32/35/36 in third person and adds
    // the current/total bar at 0x413120. It must therefore sit outside the first-person arms
    // block, which interactions deliberately suppress.
    // The world-anchored marker carries the same progress on its own ring, at the object,
    // so the centred bar would be the same reading twice.
    if (frame.RemasterHud
        && !frame.WorldInteractionPrompt
        && mission.Player?.InteractionProgress is float interactionProgress
        && frame.RemasteredHud is { IsReady: true } interactionHud)
    {
        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        interactionHud.DrawInteractionProgress(
            batch, interactionProgress, frame.Width, frame.Height);
        batch.End();
    }

    // The modern HUD is intentionally the last gameplay overlay. Rendering it only after the
    // scene resolve, close foreground device and damage wash means no hand, weapon, world mesh
    // or post-process pass can cover it.
    // The scope overlay under the modern HUD: the retail sprite machinery draws it in
    // both HUD flavours, so the scope is the scope whatever the player's UI taste.
    if (frame.RemasterHud
        && frame.ShowFirstPersonHud
        && mission.Player is { IsSniperScoped: true }
        && frame.Hud is { IsReady: true } scopeHud)
    {
        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        scopeHud.DrawSniperScope(batch, frame.Width, frame.Height);
        batch.End();
    }

    if (frame.RemasteredOptics
        && frame.ShowFirstPersonHud
        && mission.Player is { IsBinocularsOpen: true }
        && mission.Player.View is { } binocularView
        && frame.RemasteredHud is { IsReady: true } binocularHud)
    {
        _device.Set2DProjection(frame.Width, frame.Height);
        batch.Begin();
        binocularHud.DrawBinocularOverlay(
            batch,
            mission.Player.Yaw,
            binocularView.TanX,
            frame.Width,
            frame.Height,
            frame.BinocularTargets,
            frame.BinocularSeconds,
            frame.BinocularRangeMetres);
        batch.End();
    }

    // The remastered display, shared with I.G.I. 2's and drawn in Project I.G.I.'s theme, its pictures the status archive's.
    if (frame.RemasterHud
        && frame.ShowFirstPersonHud
        && mission.Player is { } modernPlayer
        && !modernPlayer.IsBinocularsOpen
        && frame.Hud is IHudIconSource icons
        && (_remastered ??= CreateRemastered()).IsReady)
    {
        Igi1HudTheme.Build(mission, frame.WorldInteractionPrompt, StatusScreenRenderer.IconFor, _model);
        _device.Set2DProjection(frame.SafeWidth, frame.SafeHeight);
        batch.Begin();
        HudProjector? projector = modernPlayer.View is { } view
            ? HudProjector.FromYawPitch(modernPlayer.Body.Eye, modernPlayer.Yaw, modernPlayer.Pitch, view.TanX, frame.Width, frame.Height)
            : null;
        _remastered.Draw(batch, _model, Igi1HudTheme.Theme, icons, HudFrame.FromSettings(frame.Width, frame.Height, frame.UiScale, frame.TickCount / 30f, crosshairSpread: 8f) with
        {
            Projector = projector,
        });
        batch.End();
    }
    }

    private RemasteredHud CreateRemastered()
    {
        RemasteredHud hud = new(_device, NullLogger.Instance);
        hud.Load();
        return hud;
    }
}
