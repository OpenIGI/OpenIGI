using System.Globalization;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Pickups;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <content>
/// The remastered display: I.G.I. 2's player read into the shared model (<see cref="HudModel"/>) and drawn in I.G.I. 2's theme
/// (<see cref="Igi2HudTheme"/>), in place of the retail status display, inventory, action row and messages. The computer, the
/// cutscene framing, the scopes and the aim's sight stay the original's.
/// </content>
public sealed partial class Igi2HudRenderer
{
    private readonly HudModel _model = new();
    private RemasteredHud? _remastered;
    private Igi2HudIcons? _icons;

    /// <summary>Gets a value indicating whether the remastered display draws this frame: chosen, and able to.</summary>
    private bool RemasteredChosen => HudCVars.RemasteredHud.Value && (_remastered ??= CreateRemastered()).IsReady;

    private RemasteredHud CreateRemastered()
    {
        RemasteredHud hud = new(_device, _logger);
        hud.Load();
        return hud;
    }

    // Draws the remastered display for a frame, the computer not open.
    private void DrawRemastered(SpriteBatch batch, Igi2TitlePlayer title, int screenWidth, int screenHeight, int requestedScale)
    {
        if (_remastered is not { IsReady: true } hud)
        {
            return;
        }

        Build(title, _model);

        // The lines of the other game's mission the player stands in, after its own, told apart from them by their keys.
        _model.AddMissionMessages(_missionMessages, firstKey: MissionMessageKeys);
        _icons ??= new Igi2HudIcons(() => _assets);
        float seconds = title.Human.Ticks / 30f;
        // The player's view as the host draws it: the eye, the view's rows (right, up, forward) and its half width.
        OpenIGI.Core.Numerics.Matrix3f rows = title.ViewOrientation;
        HudProjector projector = new(title.EyePosition, rows.Row1, rows.Row2, rows.Row3, title.FieldOfViewScale, screenWidth, screenHeight);
        hud.Draw(batch, _model, Igi2HudTheme.Theme, _icons, HudFrame.FromSettings(screenWidth, screenHeight, requestedScale, seconds, crosshairSpread: 0f) with
        {
            Projector = projector,
        });
    }

    /// <summary>Reads I.G.I. 2's player into the shared model.</summary>
    /// <param name="title">The player.</param>
    /// <param name="model">The model, refilled.</param>
    internal static void Build(Igi2TitlePlayer title, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(model);
        model.Reset();
        Igi2HumanPlayer human = title.Human;
        bool playing = !title.IsCutscenePlaying && !human.IsDead;
        model.Show = playing
            ? HudParts.Status | HudParts.Weapon | HudParts.Actions | HudParts.Messages | HudParts.Progress | HudParts.Timer
            : HudParts.Messages;

        model.Health = human.Damage.MaxDamage > 0f ? Math.Clamp(1f - (human.Damage.Damage / human.Damage.MaxDamage), 0f, 1f) : 0f;
        model.Stance = human.Pose switch
        {
            OpenIGI.Game.Igi2.Humans.Igi2HumanPose.Crouching => HudStance.Crouching,
            OpenIGI.Game.Igi2.Humans.Igi2HumanPose.LyingFlat => HudStance.Prone,
            _ => human.IsRunning ? HudStance.Running : HudStance.Standing,
        };
        model.Visibility = Math.Clamp(human.VisibilityFactor, 0f, 1f);

        IReadOnlyList<float> flash = human.DamageFlash;
        model.Damage = new HudDamage(flash[0], flash[3], flash[1], flash[2]);

        // Where the view faces, for the sway, and who is noticing the player, by their bearing from it.
        OpenIGI.Core.Numerics.Matrix3f view = human.View.Orientation;
        float forwardX = view.M12, forwardY = view.M22;
        model.HeadingRadians = MathF.Atan2(forwardX, forwardY);
        model.PitchRadians = human.View.IdealPitch;
        if (playing)
        {
            model.AlertStatus = title.AlertStatus();
            foreach ((OpenIGI.Core.Numerics.Vector3d scope, float strength) in title.ScopeGlints())
            {
                model.AddGlint(scope, strength);
            }

            foreach ((OpenIGI.Core.Numerics.Vector3d at, float awareness) in title.Threats())
            {
                double dx = at.X - human.Position.X, dy = at.Y - human.Position.Y;
                float ahead = (float)((dx * forwardX) + (dy * forwardY));
                float across = (float)((dx * view.M11) + (dy * view.M21));
                model.AddThreat(new HudThreat(MathF.Atan2(across, ahead), awareness, at));
            }
        }

        // The crosshair, where the original would draw its sight.
        if (SightShown(title))
        {
            model.Show |= HudParts.Crosshair;
            model.Aim = title.AimFeedback();
        }

        if (title.Inventory is { } inventory)
        {
            BuildWeapon(title, inventory, model);
            BuildWheel(title, inventory, model);
        }

        BuildActions(human, model);
        if (human.IsProgressBar)
        {
            model.Progress = human.TimedActionTime > 0 ? Math.Clamp((float)human.TimedActionTimer / human.TimedActionTime, 0f, 1f) : 1f;
        }

        // The objective being worked on, where it has a place in the level.
        if (playing && title.CurrentObjective() is { Position: { } place } objective)
        {
            model.Objective = objective.Text;
            model.ObjectivePosition = place;
            double dx = place.X - human.Position.X, dy = place.Y - human.Position.Y, dz = place.Z - human.Position.Z;
            model.ObjectiveDistanceMetres = (float)(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / 4096.0);
        }

        if (title.Level?.LevelFlow is { InterfaceTimerEnabled: true, MaxPlayTime: > 0f } flow)
        {
            model.TimerSeconds = Math.Max(0, (int)flow.MaxPlayTime - (flow.Tick / 30));
        }

        int key = 0;
        foreach (OpenIGI.Game.Igi2.Flow.Igi2StatusMessageSlot slot in title.StatusMessages.Slots)
        {
            key++;
            if (!slot.IsUsed || slot.Printed <= 0 || slot.Message?.IsCutsceneMessage != title.IsCutscenePlaying)
            {
                continue;
            }

            model.AddMessage(new HudMessageView(slot.Text[..Math.Min(slot.Printed, slot.Text.Length)], slot.Fade, key));
        }
    }

    // The weapon in hand: its name and fire mode, its picture, its rounds loaded and carried, and what it is doing.
    private static void BuildWeapon(Igi2TitlePlayer title, Igi2Inventory inventory, HudModel model)
    {
        if (inventory.Current is not { } held)
        {
            return;
        }

        Igi2WeaponTypeTask type = held.WeaponType;
        string mode = held.ActiveFireMode switch { 1 => type.FiremodeName1, 2 => type.FiremodeName2, _ => type.FiremodeName };
        Igi2FireTypeRounds rounds = inventory.FireTypeRounds(held, 0);
        bool counted = rounds.AmmoType is not null;
        int? secondary = type.WeaponId == "WEAPON_ID_M16A2" ? inventory.FireTypeRounds(held, 1).Available : null;
        int action = title.Human.ActionState;
        HudWeaponState state = action == Igi2PlayerActionState.Reload ? HudWeaponState.Reloading
            : action == Igi2PlayerActionState.ChangeWeapon ? HudWeaponState.Switching
            : counted && rounds.Loaded == 0 ? HudWeaponState.Empty
            : HudWeaponState.Ready;
        model.Weapon = new HudWeaponView(
            type.WeaponId,
            type.WeaponName,
            mode,
            new HudIcon(type.WeaponSprite),
            counted ? rounds.Loaded : null,
            counted ? rounds.RoundsPerClip : 0,
            counted ? rounds.Available : null,
            secondary,
            counted ? HudGauge.Clip : HudGauge.None,
            state);
    }

    // The weapon wheel while it is open: each weapon it offers with its picture, its short name and its rounds, loaded over
    // carried, as the inventory counts them.
    private static void BuildWheel(Igi2TitlePlayer title, Igi2Inventory inventory, HudModel model)
    {
        if (title.Wheel is not { Openness: > 0f } wheel)
        {
            return;
        }

        model.Wheel.From(wheel);
        (IReadOnlyList<string> choices, _) = title.WheelChoices();
        foreach (string choice in choices)
        {
            Igi2WeaponItem? item = null;
            foreach (Igi2WeaponItem candidate in inventory.Weapons)
            {
                if (string.Equals(candidate.Number.ToString(CultureInfo.InvariantCulture), choice, StringComparison.Ordinal))
                {
                    item = candidate;
                    break;
                }
            }

            if (item is null)
            {
                model.Wheel.Add(new HudWheelEntry(HudIcon.None, string.Empty, string.Empty));
                continue;
            }

            Igi2FireTypeRounds rounds = inventory.FireTypeRounds(item, 0);
            string count = rounds.AmmoType is null ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $"{rounds.Loaded}/{rounds.Available}");
            string name = title.Strings?.WeaponShortName(item.WeaponType.WeaponName) ?? item.WeaponType.WeaponName;
            model.Wheel.Add(new HudWheelEntry(new HudIcon(item.WeaponType.WeaponSprite), name, count));
        }
    }

    // What may be done: the action menu's items, a thing's badge, or a swap's two weapons under the throw-away badge.
    private static void BuildActions(Igi2HumanPlayer human, HudModel model)
    {
        if (human.ActionMenu.NoDrawTimeout != 0)
        {
            return;
        }

        foreach ((object item, object? context) in human.ActionMenu.Items)
        {
            if (item is Igi2GunPickup pickup)
            {
                HudIcon second = context is Igi2WeaponTypeTask give ? new HudIcon(give.PickupSprite) : HudIcon.None;
                model.AddAction(new HudActionView(new HudIcon(pickup.WeaponType.PickupSprite), second, second.IsSome ? new HudIcon(ThrowAwaySprite) : HudIcon.None, pickup.WeaponType.WeaponId));
            }
            else if (item is IIgi2Activatable target && InteractionIcon(target) is { } icon)
            {
                model.AddAction(new HudActionView(new HudIcon(icon), HudIcon.None, HudIcon.None, icon));
            }
        }
    }
}
