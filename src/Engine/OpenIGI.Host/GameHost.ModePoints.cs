using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation;

namespace OpenIGI.Host;

/// <content>The places a multiplayer mode plays for, marked in the world: search and destroy's sites and its planted bomb.</content>
public sealed partial class GameHost
{
    // The marks' renderer, made on the first frame that draws one.
    private ModePointRenderer? _modePoints;

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
    }

    // The match's HUD as the local player sees it, where it stands.
    private Game.Multiplayer.Modes.MatchHud? CurrentMatchHud() =>
        (TickObserver as ISessionScoreboard)?.MatchHud(TitlePlayer is { } own ? own.Position : (Vector3d?)null);
}
