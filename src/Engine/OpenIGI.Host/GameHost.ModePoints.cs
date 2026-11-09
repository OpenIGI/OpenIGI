using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation;

namespace OpenIGI.Host;

/// <content>The places a multiplayer mode plays for, marked in the world: search and destroy's sites and its planted bomb.</content>
public sealed partial class GameHost
{
    // The marks' renderer, made on the first frame that draws one.
    private ModePointRenderer? _modePoints;

    // The bomb's renderer, made on the first frame a bomb lies anywhere, and the light it was last given.
    private BombRenderer? _bombs;
    private FixedFunctionSun? _bombSun;

    // The bomb where it lies, planted at a site or dropped where its carrier fell, among the world's solid geometry -- unless it
    // was thrown, when the world draws it as it flies and where it lands.
    // Whether this player has just taken the bomb up from where it lay.
    private readonly Game.Multiplayer.Modes.BombPickupWatch _bombPickup = new();

    // Once a frame: the bomb taken from the ground told as a gun's pickup is.
    private void UpdateBombPickup()
    {
        if (_bombPickup.Saw(CurrentMatchHud()))
        {
            _mission.Objectives?.ShowTransientMessage(Game.Multiplayer.Modes.BombPickupWatch.Message);
        }
    }

    private void DrawBombs(in Camera3D eye, in Matrix4f projection)
    {
        if (TickObserver is not Multiplayer.ISessionMode { Mode: Game.Multiplayer.Modes.GameModes.SearchAndDestroy }
            || (TickObserver as ISessionScoreboard)?.ModeReport is not { } report
            || (report.BombAt ?? (report.BombFlies ? null : report.DroppedAt)) is not { } at)
        {
            return;
        }

        _bombs ??= new BombRenderer(_device, _logger);
        _bombs.Sun = _bombSun;
        _bombs.Draw([(at, report.BombYaw)], eye, projection);
    }

    // The session's mode points over the lit scene, after the tracers: a lettered ring on the ground and a low wall of light.
    private void DrawModePoints(in Camera3D eye, in Matrix4f projection)
    {
        if (TickObserver is not Multiplayer.ISessionMode { Mode: Game.Multiplayer.Modes.GameModes.SearchAndDestroy, Layout: { } layout })
        {
            return;
        }

        Game.Multiplayer.Modes.ModeReport? report = (TickObserver as ISessionScoreboard)?.ModeReport;
        IReadOnlyList<ModePointMark> marks = ModePointRenderer.BombSites(layout, report, (float)(_loop.TimeSource.Milliseconds / 1000.0 % 3600.0));
        if (marks.Count == 0)
        {
            return;
        }

        _modePoints ??= new ModePointRenderer(_device);
        _modePoints.Draw(marks, eye, projection);
    }

    private void DisposeModePoints()
    {
        _modePoints?.Dispose();
        _modePoints = null;
        _bombs?.Dispose();
        _bombs = null;
    }

    // The match's HUD as the local player sees it, where it stands.
    private Game.Multiplayer.Modes.MatchHud? CurrentMatchHud()
    {
        Game.Multiplayer.Modes.MatchHud? hud = (TickObserver as ISessionScoreboard)?.MatchHud(TitlePlayer is { } own ? own.Position : (Vector3d?)null);
        if (hud?.Money is not { } money)
        {
            _moneySeen = null;
            return hud;
        }

        // What the money changed by, shown beside it a few seconds: a change on top of one still shown adds to it.
        int now = _loop.TimeSource.Milliseconds;
        if (_moneySeen is { } seen && money != seen)
        {
            _moneyChange = (now - _moneyChangedAt <= MoneyChangeMilliseconds ? _moneyChange : 0) + (money - seen);
            _moneyChangedAt = now;
        }

        _moneySeen = money;
        return _moneyChange != 0 && now - _moneyChangedAt <= MoneyChangeMilliseconds ? hud with { MoneyChange = _moneyChange } : hud;
    }

    // How long a change of the money is shown beside it.
    private const int MoneyChangeMilliseconds = 3000;

    // The money as last seen, what it last changed by and when.
    private int? _moneySeen;
    private int _moneyChange;
    private int _moneyChangedAt;
}
