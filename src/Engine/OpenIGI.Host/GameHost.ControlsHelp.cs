using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Game.Config;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Game.Player;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;
using OpenIGI.Presentation.Hud;

namespace OpenIGI.Host;

/// <summary>
/// A multiplayer match's controls panel: a key held (H unless rebound) shows what every key does as the player has them bound,
/// and a small reminder in the corner says which key that is.
/// </summary>
/// <remarks>
/// A newcomer to a shooter cannot guess the shop is G, the drop X and the team menu M. The panel reads the keys from the
/// player's profile and the console's binds each time it is drawn, so a key rebound shows rebound, and an action bound to nothing
/// is left off rather than shown wrong. Not on a touch screen, whose buttons say what they do.
/// </remarks>
public sealed partial class GameHost
{
    // How long the reminder stands out as a match begins, before it fades to a quiet corner mark.
    private const double ControlsHintBrightSeconds = 15;

    private bool _controlsUp;
    private double? _controlsHintSince;

    // Once a frame, with the scoreboard's key: whether the panel's key is held.
    private void UpdateControlsHelp()
    {
        bool session = TickObserver is ISessionScoreboard && !_menuFlow.MenuMode;
        if (!session)
        {
            _controlsUp = false;
            _controlsHintSince = null;
            return;
        }

        _controlsHintSince ??= _loop.TimeSource.Milliseconds / 1000.0;
        _controlsUp = !_window.Input.Touch.InUse
            && _chatEntry is null
            && BindKeyNames.TryParse(ControlsHelpCVars.Key.Value, out KeyCode key)
            && _window.Input.Keyboard.IsDown(key);
    }

    // The panel, or the reminder of its key: over play, under the console.
    private void DrawControlsHelp(FrameLoopState state)
    {
        if (TickObserver is not ISessionScoreboard || _remasteredHud is not { IsReady: true } hud || _window.Input.Touch.InUse || _menuFlow.MenuMode)
        {
            return;
        }

        string holdKey = KeyLabel(ControlsHelpCVars.Key.Value) ?? "H";
        if (_controlsUp)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            hud.DrawControls(state.Batch, CurrentControlsHelp(state.Console.System.Binds, holdKey), SafeWindowWidth, SafeWindowHeight);
            state.Batch.End();
            return;
        }

        if (!ControlsHelpCVars.Hint.Value || _scoreboardUp || _buyMenuUp || _teamMenuUp || _mapVoteUp || _chatEntry is not null)
        {
            return;
        }

        double since = (_loop.TimeSource.Milliseconds / 1000.0) - (_controlsHintSince ?? 0);
        float opacity = since < ControlsHintBrightSeconds ? 1f : 0.55f;
        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        hud.DrawControlsHint(state.Batch, holdKey, opacity, SafeWindowWidth, SafeWindowHeight);
        state.Batch.End();
    }

    /// <summary>What the panel shows now: the keys as bound, the match's own only where the match has them.</summary>
    /// <param name="binds">The console's binds, key to statement.</param>
    /// <param name="holdKey">The panel's own key, as its cap shows it.</param>
    /// <returns>The panel.</returns>
    private ControlsHelp CurrentControlsHelp(IReadOnlyDictionary<string, string> binds, string holdKey)
    {
        PlayerProfile? profile = _configuration.Active;
        string mode = TickObserver is ISessionMode { Mode: { Length: > 0 } id } ? id : string.Empty;
        MatchHud? hud = CurrentMatchHud();

        string? Bound(PlayerAction action, string? fallback = null) =>
            KeyLabel(PlayerProfileInputBindings.BoundKeyName(profile, PlayerInputBindings.ConfigName(action))) ?? fallback;

        // The first key a console statement is bound to, as the player bound it.
        string? BoundCommand(string statement) => binds
            .Where(b => string.Equals(b.Value.Trim(), statement, StringComparison.OrdinalIgnoreCase))
            .Select(b => KeyLabel(b.Key))
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        List<ControlsLine> move = [];
        string?[] walk = [Bound(PlayerAction.MoveUp), Bound(PlayerAction.MoveLeft), Bound(PlayerAction.MoveDown), Bound(PlayerAction.MoveRight)];
        if (walk.All(static k => k is not null))
        {
            move.Add(new ControlsLine([.. walk.OfType<string>()], "Move"));
        }

        Add(move, Bound(PlayerAction.Jump), "Jump");
        Add(move, Bound(PlayerAction.Crouch), "Crouch");
        Add(move, Bound(PlayerAction.Peek), "Lean out");
        Add(move, Bound(PlayerAction.ToggleWalkRun), "Walk / run");
        Add(move, BoundCommand("toggle g_thirdperson"), "Third-person view");
        Add(move, Bound(PlayerAction.Binoculars), "Binoculars");
        if (Bound(PlayerAction.ZoomIn) is { } zoomIn && Bound(PlayerAction.ZoomOut) is { } zoomOut)
        {
            move.Add(new ControlsLine([zoomIn, zoomOut], "Zoom in / out"));
        }

        List<ControlsLine> combat = [];
        Add(combat, Bound(PlayerAction.Fire), "Fire");
        Add(combat, Bound(PlayerAction.AlternateFire), "Aim / second fire");
        Add(combat, Bound(PlayerAction.Reload), "Reload");
        Add(combat, Bound(PlayerAction.NextWeapon), "Next weapon");
        Add(combat, Bound(PlayerAction.PreviousWeapon), "Previous weapon");
        Add(combat, Bound(PlayerAction.WeaponWheel), "Weapon wheel (hold)");
        if (Bound(PlayerAction.WeaponCategory1) is { } first && Bound(PlayerAction.WeaponCategory9) is { } last)
        {
            combat.Add(new ControlsLine([$"{first}–{last}"], "Weapon by slot"));
        }

        Add(combat, Bound(PlayerAction.Activate), "Use: doors, switches, ladders");
        Add(combat, Bound(PlayerAction.Flashlight), "Flashlight");

        List<ControlsLine> match = [];
        Add(match, Bound(PlayerAction.Scoreboard, "TAB"), "Scoreboard (hold)");
        bool versus = ArenaLevel.IsVersus(mode);
        if (hud is { Money: not null } || versus)
        {
            Add(match, Bound(PlayerAction.BuyMenu, "G"), hud is { FreeLoadout: true } ? "Loadout" : "Shop (in buy time)");
        }

        if (versus)
        {
            Add(match, Bound(PlayerAction.DropWeapon, "X"), "Drop weapon / bomb");
        }

        if (hud is { Teams: not null } || (versus && GameModes.HasTeams(mode)))
        {
            Add(match, KeyLabel(TeamMenuCVars.Key.Value), "Team menu");
        }

        Add(match, BoundCommand("coop_chat"), "Chat to everyone");
        Add(match, BoundCommand("coop_chat team"), "Chat to your team");
        if (CoopVoiceCVars.Mode.Value == 1)
        {
            Add(match, KeyLabel(CoopVoiceCVars.Key.Value), "Push to talk");
        }

        Add(match, BoundCommand("coop_mark"), "Mark a spot");
        string[] callouts = [.. binds
            .Where(static b => b.Value.TrimStart().StartsWith("coop_say ", StringComparison.OrdinalIgnoreCase))
            .Select(static b => KeyLabel(b.Key))
            .OfType<string>()
            .Order(StringComparer.Ordinal)];
        if (callouts.Length > 0)
        {
            match.Add(new ControlsLine([callouts.Length == 1 ? callouts[0] : $"{callouts[0]}–{callouts[^1]}"], "Quick callouts"));
        }

        return new ControlsHelp(
            [new ControlsSection("MOVEMENT", move), new ControlsSection("COMBAT", combat), new ControlsSection("MATCH", match)],
            holdKey,
            mode.Length > 0 ? GameModes.Title(mode) : string.Empty);

        static void Add(List<ControlsLine> lines, string? key, string action)
        {
            if (key is not null)
            {
                lines.Add(new ControlsLine([key], action));
            }
        }
    }

    /// <summary>A session's map as players know it: the mission's own name (Trainyard), not its number.</summary>
    /// <param name="place">The session's place, as <c>Mission 1</c>, or a level's own name.</param>
    /// <returns>The mission's name from the title's catalogue, or the place as it was given.</returns>
    private string MissionPlaceName(string place)
    {
        const string Prefix = "Mission ";
        return place.StartsWith(Prefix, StringComparison.Ordinal)
            && int.TryParse(place.AsSpan(Prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            && _missions.TryGet(id, out Game.World.MissionDescriptor? mission)
            && mission is { Name.Length: > 0 }
                ? mission.Name
                : place;
    }

    /// <summary>A key's name as its cap shows it: short, upper case, the mouse's buttons by what they are.</summary>
    /// <param name="name">The key's name, as a profile or a bind names it; null or blank for none.</param>
    /// <returns>The cap's text, or <see langword="null"/> for no key.</returns>
    internal static string? KeyLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string trimmed = name.Trim();
        string squashed = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (squashed.StartsWith("MOUSEBUTTON", StringComparison.Ordinal) || squashed.StartsWith("MOUSE", StringComparison.Ordinal))
        {
            string number = new([.. squashed.Where(char.IsAsciiDigit)]);
            return number switch
            {
                "1" => "LMB",
                "2" => "RMB",
                "3" => "MMB",
                "" when squashed.Contains("UP", StringComparison.Ordinal) => "WHEEL UP",
                "" when squashed.Contains("DOWN", StringComparison.Ordinal) => "WHEEL DOWN",
                _ => string.Create(CultureInfo.InvariantCulture, $"MOUSE {number}"),
            };
        }

        string label = squashed switch
        {
            "SPACE" or "SPACEBAR" => "SPACE",
            "RETURN" or "ENTER" => "ENTER",
            "NUMPADENTER" => "NUM ENTER",
            "LEFTCONTROL" or "RIGHTCONTROL" or "CONTROL" or "LCONTROL" or "RCONTROL" => "CTRL",
            "LEFTSHIFT" or "RIGHTSHIFT" or "SHIFT" or "LSHIFT" or "RSHIFT" => "SHIFT",
            "LEFTALT" or "RIGHTALT" or "ALT" or "LALT" or "RALT" => "ALT",
            "BACKSPACE" => "BKSP",
            "ESCAPE" => "ESC",
            "PAGEUP" or "PRIOR" => "PG UP",
            "PAGEDOWN" or "NEXT" => "PG DN",
            "CAPSLOCK" or "CAPITAL" => "CAPS",
            "UP" or "UPARROW" => "↑",
            "DOWN" or "DOWNARROW" => "↓",
            "LEFT" or "LEFTARROW" => "←",
            "RIGHT" or "RIGHTARROW" => "→",
            _ => squashed,
        };

        if (label.StartsWith("NUMPAD", StringComparison.Ordinal) && label.Length > "NUMPAD".Length)
        {
            label = "NUM " + label["NUMPAD".Length..];
        }

        if (label.Length == 2 && label[0] == 'D' && char.IsAsciiDigit(label[1]))
        {
            label = label[1..];
        }

        return label;
    }
}

/// <summary>The controls panel's settings.</summary>
public static class ControlsHelpCVars
{
    /// <summary>The key held to show the controls panel in a match, by its bind name.</summary>
    public static readonly StringCVar Key = CVar.Text(
        "mp_controls_key", "h", CVarOptions.Archive, "The key held in a multiplayer match to show what every key does, by its bind name: H.");

    /// <summary>Whether the corner's reminder of that key is shown.</summary>
    public static readonly BoolCVar Hint = CVar.Bool(
        "mp_controls_hint", true, CVarOptions.Archive, "Whether a multiplayer match shows the small reminder of the key held for the controls panel.");
}
