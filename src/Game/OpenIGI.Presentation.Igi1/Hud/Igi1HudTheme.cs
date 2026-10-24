using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.Hud;

/// <summary>
/// Project I.G.I.'s look for the remastered display: the phosphor green of its status screen on dark green glass, corners cut
/// square at forty-five degrees, hard brackets, amber only for what is passing, and a heavier CRT visor. Every part where the
/// original put it: the health at the lower left, the weapon at the lower right.
/// </summary>
public static class Igi1HudTheme
{
    private static readonly Bgra32 Phosphor = Bgra32.FromRgba(78, 255, 120, 255);

    /// <summary>How far over a soldier's feet his eye is marked, in world units: 2.15 m, a hand over a standing head.</summary>
    public const double OverHead = 8806.4;

    /// <summary>Gets the theme.</summary>
    public static HudTheme Theme { get; } = new(
        "Project I.G.I.",
        UiScale.AuthoredWidth,
        UiScale.AuthoredHeight,
        new HudPalette(
            Primary: Phosphor,
            Dim: Bgra32.FromRgba(26, 120, 52, 210),
            Highlight: Bgra32.FromRgba(205, 255, 214, 255),
            Accent: Bgra32.FromRgba(255, 182, 64, 255),
            Danger: Bgra32.FromRgba(255, 62, 48, 255),
            Glass: Bgra32.FromRgba(4, 30, 12, 70),
            Health: Health,
            Visibility: static _ => Phosphor),
        new HudMotifs(HudCorner.Cut, CornerSize: 8f, Hairline: 1f, BracketLength: 8f, Shimmer: false, DoubleRules: false),
        new HudFxSettings
        {
            Intensity = 1f,
            Curvature = 0.01f,
            FringePixels = 1.2f,
            ScanlineDensity = 480f,
            ScanlineStrength = 0.22f,
            Glow = 0.85f,
            DamageColour = (1f, 0.1f, 0.05f),
        },
        new Dictionary<HudElement, HudAnchor>
        {
            // StatusScreenLayout: the health bar about the lower left, the weapon readout right-aligned on the same baseline,
            // the action badge low in the middle.
            [HudElement.Vitals] = new(HudEdgeX.Left, 10f, HudEdgeY.Bottom, 10f),
            [HudElement.Weapon] = new(HudEdgeX.Right, 10f, HudEdgeY.Bottom, 10f),
            [HudElement.Actions] = new(HudEdgeX.Centre, 0f, HudEdgeY.Bottom, 72f),
            [HudElement.Progress] = new(HudEdgeX.Centre, 0f, HudEdgeY.Bottom, 92f),
        });

    /// <summary>The health gauge's colour: phosphor green when whole, through amber, to red when spent.</summary>
    /// <param name="health">The health, nought to one.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 Health(float health)
    {
        float h = Math.Clamp(health, 0f, 1f);
        return h > 0.5f ? Phosphor
            : h > 0.25f ? Bgra32.FromRgba(255, 182, 64, 255)
            : Bgra32.FromRgba(255, 62, 48, 255);
    }

    /// <summary>Reads Project I.G.I.'s player into the shared model.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="worldPrompt">Whether the world-anchored prompt shows what may be done, so the row does not.</param>
    /// <param name="iconFor">The status screen's picture for a weapon.</param>
    /// <param name="model">The model, refilled.</param>
    public static void Build(MissionWorld mission, bool worldPrompt, Func<WeaponType, string> iconFor, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(iconFor);
        ArgumentNullException.ThrowIfNull(model);
        model.Reset();
        if (mission.Player is not { } player)
        {
            return;
        }

        model.Show = HudParts.Status | HudParts.Weapon | HudParts.Actions | HudParts.Progress | HudParts.Crosshair;
        model.Health = mission.PlayerHealth.RemainingFraction;
        model.HeadingRadians = player.Yaw;
        model.PitchRadians = player.Pitch;

        // Who has the player in sight, by their bearing from where the camera faces -- forward is (sin yaw, cos yaw) -- and the
        // point over their heads the eye is drawn at.
        if (mission.Soldiers is { } soldiers)
        {
            float forwardX = MathF.Sin(player.Yaw), forwardY = MathF.Cos(player.Yaw);
            model.AlertStatus = soldiers.AlertStatus();
            foreach ((OpenIGI.Core.Numerics.Vector3d scope, float strength) in soldiers.ScopeGlints())
            {
                model.AddGlint(scope, strength);
            }

            foreach ((OpenIGI.Core.Numerics.Vector3d at, float awareness) in soldiers.Threats())
            {
                double dx = at.X - player.Body.Position.X, dy = at.Y - player.Body.Position.Y;
                float ahead = (float)((dx * forwardX) + (dy * forwardY));
                float across = (float)((dx * forwardY) - (dy * forwardX));
                model.AddThreat(new HudThreat(MathF.Atan2(across, ahead), awareness, at + new OpenIGI.Core.Numerics.Vector3d(0.0, 0.0, OverHead)));
            }
        }

        if (player.Weapons is { InHand: { } held } carried)
        {
            WeaponType weapon = held.Weapon;
            bool counted = weapon.AmmoDisplay != AmmoDisplayType.None || weapon.Category == WeaponCategory.Medipack;
            bool loads = counted && weapon.Category != WeaponCategory.Medipack && held.Gun.RoundsInClip != GunTiming.UnloadedClipSentinel;
            HudWeaponState state = carried.State switch
            {
                HumanWeaponState.Reloading => HudWeaponState.Reloading,
                HumanWeaponState.ChangingWeapon => HudWeaponState.Switching,
                _ => loads && held.Gun.RoundsInClip == 0 ? HudWeaponState.Empty : HudWeaponState.Ready,
            };
            model.Weapon = new HudWeaponView(
                weapon.ScriptId,
                weapon.DisplayName,
                string.Empty,
                new HudIcon(iconFor(weapon)),
                loads ? held.Gun.RoundsInClip : null,
                loads ? weapon.RoundsPerClip : 0,
                counted ? held.Gun.Reserve(carried.Ammunition) : null,
                null,
                loads ? HudGauge.Clip : HudGauge.None,
                state);
        }

        BuildWheel(player, iconFor, model);

        if (!worldPrompt)
        {
            if (player.ActionSprite is { Length: > 0 } action)
            {
                model.AddAction(new HudActionView(new HudIcon(action), HudIcon.None, HudIcon.None, action));
            }

            model.Progress = player.InteractionProgress;
        }

        if (player.IsSniperScoped)
        {
            model.Show &= ~HudParts.Crosshair;
        }

        // The objective being worked on: the first not yet done, where it has a place in the level.
        if (mission.Objectives?.Objectives is { } objectives)
        {
            foreach (OpenIGI.Game.World.MissionObjective objective in objectives)
            {
                if (objective.State != OpenIGI.Game.World.MissionObjectiveState.Incomplete || objective.Position is not { } place)
                {
                    continue;
                }

                model.Objective = objective.Text;
                model.ObjectivePosition = place;
                OpenIGI.Core.Numerics.Vector3d feet = player.Body.Position;
                double dx = place.X - feet.X, dy = place.Y - feet.Y, dz = place.Z - feet.Z;
                model.ObjectiveDistanceMetres = (float)(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / 4096.0);
                break;
            }
        }
    }

    // The weapon wheel while it is open: each weapon it offers with its picture, its name and its rounds, loaded over carried.
    private static void BuildWheel(OpenIGI.Game.Igi1.Player.PlayerCamera player, Func<WeaponType, string> iconFor, HudModel model)
    {
        if (player.Wheel is not { Openness: > 0f } wheel || player.Weapons is not { } carried)
        {
            return;
        }

        model.Wheel.From(wheel);
        (IReadOnlyList<string> choices, _) = player.WheelChoices();
        foreach (string choice in choices)
        {
            CarriedWeapon? found = null;
            foreach (CarriedWeapon weapon in carried.Carried)
            {
                if (string.Equals(weapon.Weapon.WeaponId.ToString(CultureInfo.InvariantCulture), choice, StringComparison.Ordinal))
                {
                    found = weapon;
                    break;
                }
            }

            if (found is not { } one)
            {
                model.Wheel.Add(new HudWheelEntry(HudIcon.None, string.Empty, string.Empty));
                continue;
            }

            WeaponType type = one.Weapon;
            bool counted = type.AmmoDisplay != AmmoDisplayType.None || type.Category == WeaponCategory.Medipack;
            bool loads = counted && type.Category != WeaponCategory.Medipack && one.Gun.RoundsInClip != GunTiming.UnloadedClipSentinel;
            int reserve = one.Gun.Reserve(carried.Ammunition);
            string count = !counted ? string.Empty
                : loads ? string.Create(CultureInfo.InvariantCulture, $"{one.Gun.RoundsInClip}/{reserve}")
                : reserve.ToString(CultureInfo.InvariantCulture);
            model.Wheel.Add(new HudWheelEntry(new HudIcon(iconFor(type)), type.DisplayName, count));
        }
    }
}
