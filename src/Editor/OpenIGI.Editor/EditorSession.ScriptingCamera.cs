using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;

namespace OpenIGI.Editor;

/// <summary>
/// The console verbs for the cutscene camera's keys, eases, curves and rigs, so a scripted run can set each up as a hand would:
/// <c>editor key &lt;n&gt;</c>, <c>editor ease &lt;n&gt; &lt;in|out|inout|even|none&gt;</c>, <c>editor curves &lt;channel|off&gt;</c>,
/// <c>editor camera</c> (the viewport's camera and the frame shown, logged), <c>editor rail #&lt;id&gt; &lt;east,north&gt;... [make]</c> (points in metres from an object, on the ground; watching it with
/// <c>watch</c>), <c>editor rig &lt;crane|orbit&gt; #&lt;id&gt;</c>.
/// </summary>
internal sealed partial class EditorSession
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // One of the verbs above, queued to run once the mission is open; null for any other.
    private string? RunCameraCommand(IReadOnlyList<string> arguments)
    {
        if (arguments is not [string verb, ..])
        {
            return null;
        }

        switch (verb.ToUpperInvariant())
        {
            case "KEY" when arguments.Count == 2 && int.TryParse(arguments[1], NumberStyles.Integer, Invariant, out int key):
                return Queue(() => _sequencer.PickOwnKey(key - 1), "Selecting the key.");

            case "EASE" when arguments.Count == 3 && int.TryParse(arguments[1], NumberStyles.Integer, Invariant, out int eased)
                && Enum.TryParse(arguments[2], ignoreCase: true, out CutsceneEase ease):
                return Queue(() => _sequencer.EaseKey(eased - 1, ease), "Easing the key.");

            case "CURVES" when arguments.Count == 2:
                CutsceneChannel? channel = arguments[1].Equals("off", StringComparison.OrdinalIgnoreCase) ? null
                    : Enum.TryParse(arguments[1], ignoreCase: true, out CutsceneChannel shown) ? shown : CutsceneChannel.Speed;
                return Queue(() => _sequencer.ShowCurves(channel), "Showing the curves.");

            case "RAIL" when arguments.Count >= 3 && arguments[1].Equals("selected", StringComparison.OrdinalIgnoreCase):
                return Queue(() => LayRail(_workspace.Selection.Primary, arguments.Skip(2).ToList()), "Laying the rail.");

            case "RAIL" when arguments.Count >= 3 && arguments[1].StartsWith('#') && int.TryParse(arguments[1].AsSpan(1), NumberStyles.Integer, Invariant, out int from):
                return Queue(() => LayRail(_workspace.Document?.EnumerateTasks().FirstOrDefault(t => t.Id == from), arguments.Skip(2).ToList()), "Laying the rail.");

            case "RIG" when arguments.Count == 3 && Enum.TryParse(arguments[1], ignoreCase: true, out CutsceneRigKind kind) && kind != CutsceneRigKind.Rail
                && arguments[2].StartsWith('#') && int.TryParse(arguments[2].AsSpan(1), NumberStyles.Integer, Invariant, out int subject):
                return Queue(() =>
                {
                    if (_workspace.Document?.EnumerateTasks().FirstOrDefault(t => t.Id == subject) is { } task)
                    {
                        _workspace.Selection.Set(task);
                        _sequencer.AddSwing(kind);
                    }
                }, "Adding the rig.");

            case "CAMERA" when arguments.Count == 1:
                return Queue(() => _host.Logger.Information("Editor", _sequencer.CameraReport()), "Reporting the camera.");

            default:
                return null;
        }
    }

    // A rail laid from an object: each point so many metres east and north of it, on the ground there; made with "make", watching
    // the object with "watch", else left laid for the viewport to show.
    private void LayRail(MissionTask? origin, List<string> points)
    {
        if (origin is null || TaskPresentation.PositionMetres(origin) is not { } at)
        {
            return;
        }

        if (points.Contains("watch", StringComparer.OrdinalIgnoreCase))
        {
            _workspace.Selection.Set(origin);
        }
        else
        {
            _workspace.Selection.Clear();
        }

        _sequencer.StartRail();
        foreach (string point in points.Where(static p => p.Contains(',', StringComparison.Ordinal)))
        {
            string[] parts = point.Split(',');
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, Invariant, out double east) && double.TryParse(parts[1], NumberStyles.Float, Invariant, out double north))
            {
                double x = at.X + east, y = at.Y + north;
                _sequencer.AddRailPoint(new Vector3d(x, y, _host.SurfaceBelow(new Vector3d(x, y, at.Z + 300), 600) ?? at.Z));
            }
        }

        if (points.Contains("make", StringComparer.OrdinalIgnoreCase))
        {
            _sequencer.EndRail(true);
        }
    }

    // A step run once the mission is open and warm.
    private string Queue(Action step, string said)
    {
        _pendingSteps.Enqueue(() =>
        {
            if (_workspace.Document is null || _host.IsWarmingUp)
            {
                return false;
            }

            step();
            return true;
        });
        return said;
    }
}
