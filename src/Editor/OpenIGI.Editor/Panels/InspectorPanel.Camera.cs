using System.Globalization;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor.Panels;

/// <content>
/// A cutscene camera key in words before its raw parameters, as Unreal's details panel shows a camera's lens and transform: which
/// key of which scene it is and when, what it rides or watches (a riding key is written relative to where its ride stood as the
/// level loaded), its lens in degrees, its turn in degrees, how long it holds and whether it glides on; and Pilot, to fly it.
/// </content>
internal sealed partial class InspectorPanel
{
    private const double Degree = Math.PI / 180;

    /// <summary>Gets or sets what pilots a camera key with the viewport, for the Pilot button.</summary>
    public Action<MissionTask>? PilotKey { get; set; }

    private void AddCameraSection(MissionDocument document, MissionTask camera)
    {
        if (camera.TypeName != CutsceneModel.CameraType || Ancestor(camera, CutsceneModel.SceneType) is not { } scene)
        {
            return;
        }

        _grid.Section("Camera Key");
        _grid.Text("Key", () => Where(scene, camera), null, "Which of the scene's keys this is, and when on its clock it starts.");
        _grid.Text("Seen", () => Seen(document, camera), null,
            "A key that rides is written relative to where its ride stood as the level loaded, and carried along with it; one that watches is turned to its target whatever its own turn says.");

        NumberField lens = _grid.Number(
            "Lens (°)",
            () => 2 * Math.Atan(Number(camera, "FOV", 1)) / Degree,
            (value, final) =>
            {
                if (final)
                {
                    _workspace.Execute(SetParameterCommand.Numbers(document, camera, "FOV", [Math.Round(Math.Tan(value * Degree / 2), 4)]));
                }
            },
            "How wide it sees, across the picture: the game's own is 90°. Written as the FOV parameter, the tangent of half of it.");
        lens.Minimum = 5;
        lens.Maximum = 170;
        lens.Decimals = 1;

        foreach ((string label, string parameter, string about) in ((string, string, string)[])[
            ("Turn X (°)", "Alpha", "Its turn about the X axis (Alpha), in degrees."),
            ("Turn Y (°)", "Beta", "Its turn about the Y axis (Beta), in degrees."),
            ("Turn Z (°)", "Gamma", "Its turn about the up axis (Gamma), in degrees: which way it faces."),
        ])
        {
            NumberField turn = _grid.Number(
                label,
                () => Number(camera, parameter, 0) / Degree,
                (value, final) =>
                {
                    if (final)
                    {
                        _workspace.Execute(SetParameterCommand.Numbers(document, camera, parameter, [Math.Round(value * Degree, 6)]));
                    }
                },
                about + " The game blends each angle on its own, so a key turned the long way round turns that way.");
            turn.Decimals = 1;
        }

        NumberField length = _grid.Number(
            "Length (s)",
            () => CutsceneModel.Shots(scene).FirstOrDefault(s => ReferenceEquals(s.Camera, camera))?.Length ?? 0,
            (value, final) =>
            {
                if (final)
                {
                    _workspace.Execute(CutsceneModel.SetLength(document, scene, camera, value));
                }
            },
            "How long until the next key, in scene seconds (the scene's time scale taken into it).");
        length.Minimum = 0;
        length.Decimals = 2;

        _grid.Bool("Glides on", () => CutsceneMoves.Glides(camera), value => _workspace.Execute(CutsceneModel.SetSmooth(document, camera, value)),
            "Whether the camera flies on into the next key, or holds and cuts to it.");

        Button pilot = new(Theme, "Pilot") { Tooltip = "Fly this key with the viewport; every move is written into it, as seen (Shift+P)" };
        pilot.Clicked += () => PilotKey?.Invoke(camera);
        _grid.Add(string.Empty, pilot);
    }

    // The nearest task of a type above another.
    private static MissionTask? Ancestor(MissionTask task, string type)
    {
        for (MissionTask? at = task.Parent; at is not null; at = at.Parent)
        {
            if (at.TypeName == type)
            {
                return at;
            }
        }

        return null;
    }

    private static string Where(MissionTask scene, MissionTask camera)
    {
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        int index = shots.Select((s, i) => (s, i)).FirstOrDefault(p => ReferenceEquals(p.s.Camera, camera)).i;
        string name = scene.Label.Length > 0 ? scene.Label : "CutScene";
        return string.Create(CultureInfo.InvariantCulture, $"{index + 1} of {shots.Count} in {name} ({scene.Id}), at {shots[index].Start:0.##} s");
    }

    private static string Seen(MissionDocument document, MissionTask camera)
    {
        int link = (int)Number(camera, "Link task ID", -1);
        int target = (int)Number(camera, "Target task ID", -1);
        string Named(int id) => document.EnumerateTasks().FirstOrDefault(t => t.Id == id) is { } task
            ? string.Create(CultureInfo.InvariantCulture, $"{(task.Label.Length > 0 ? task.Label : task.TypeName)} ({task.TypeName} {id})")
            : string.Create(CultureInfo.InvariantCulture, $"#{id}");
        return (link, target) switch
        {
            ( >= 0, >= 0) => $"Rides {Named(link)}, watching {Named(target)}",
            ( >= 0, _) => $"Rides {Named(link)}: relative to where it stood as the level loaded",
            (_, >= 0) => $"Watches {Named(target)}: turned to it",
            _ => "Where it is written",
        };
    }

    private static double Number(MissionTask task, string parameter, double fallback) =>
        task.FindParameter(parameter) is { } found && found.TryGetNumbers(out double[] values) && values.Length > 0 ? values[0] : fallback;
}
