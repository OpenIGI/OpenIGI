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

    /// <summary>Gets or sets the open mission's camera rigs.</summary>
    public CutsceneRigStore? Rigs { get; set; }

    /// <summary>Gets or sets what makes a rig's keys again from changed settings: its scene, its new settings.</summary>
    public Action<MissionTask, CutsceneRig>? RemakeRig { get; set; }

    /// <summary>Gets or sets what lets a rig's keys go as plain keys.</summary>
    public Action<CutsceneRig>? Unrig { get; set; }

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
                    _workspace.Execute(CutsceneEasing.WithFollow(document, scene, camera, SetParameterCommand.Numbers(document, camera, "FOV", [Math.Round(Math.Tan(value * Degree / 2), 4)])));
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
                        _workspace.Execute(CutsceneEasing.WithFollow(document, scene, camera, SetParameterCommand.Numbers(document, camera, parameter, [Math.Round(value * Degree, 6)])));
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

        if (CutsceneMoves.Glides(camera))
        {
            CutsceneEase[] eases = [CutsceneEase.None, CutsceneEase.In, CutsceneEase.Out, CutsceneEase.InOut, CutsceneEase.Even];
            _grid.Choice(
                "Ease",
                ["Game's curve", "Ease in", "Ease out", "Ease in and out", "Even speed"],
                () => Array.IndexOf(eases, CutsceneEasing.Of(scene, camera)),
                index =>
                {
                    if (CutsceneEasing.Set(document, scene, camera, eases[index]) is { } step)
                    {
                        _workspace.Execute(step);
                    }
                },
                "How the camera runs to the next key, baked into helper keys the game reads (the path kept, its timing changed); the game's own curve takes them out again.");
        }

        AddRigSection(document, scene, camera);

        Button pilot = new(Theme, "Pilot") { Tooltip = "Fly this key with the viewport; every move is written into it, as seen (Shift+P)" };
        pilot.Clicked += () => PilotKey?.Invoke(camera);
        _grid.Add(string.Empty, pilot);
    }

    // The rig a key belongs to: what it is, its settings (each change makes its keys again), and letting it go as plain keys.
    private void AddRigSection(MissionDocument document, MissionTask scene, MissionTask camera)
    {
        if (Rigs?.Of(scene, camera) is not { } rig)
        {
            return;
        }

        _grid.Section("Rig: " + rig.Name);
        string subject = rig.SubjectId >= 0 ? Seen(document, camera) is var _ && document.EnumerateTasks().FirstOrDefault(t => t.Id == rig.SubjectId) is { } s ? TaskPresentation.Name(s) : $"#{rig.SubjectId}" : string.Empty;
        string what = rig.Kind switch
        {
            CutsceneRigKind.Rail => string.Create(CultureInfo.InvariantCulture, $"A rail through {rig.Points.Count} points{(subject.Length > 0 ? ", watching " + subject : ", looking along the way")}"),
            CutsceneRigKind.Crane => "A crane swung round " + subject,
            _ => "An orbit round " + subject,
        };
        _grid.Text("Rig", () => what, null, "Its keys are plain keys the game reads; its settings are the editor's own, kept beside the mission, so it can be changed and its keys made again.");
        bool intact = CutsceneRigs.IsIntact(scene, rig);
        if (!intact)
        {
            _grid.Text("Edited", () => "Its keys were changed by hand", null, "Changing a setting makes its keys again from the rig, losing what was changed by hand; Plain Keys keeps them and forgets the rig.");
        }

        void Number(string label, double value, Func<double, CutsceneRig> with, string about, double low, double high)
        {
            NumberField field = _grid.Number(label, () => value, (v, final) =>
            {
                if (final && Math.Abs(v - value) > 1e-9)
                {
                    RemakeRig?.Invoke(scene, with(v));
                }
            }, about);
            field.Minimum = low;
            field.Maximum = high;
            field.Decimals = 1;
        }

        Number("Length (s)", rig.Seconds, v => rig with { Seconds = v }, "How long the whole rig runs.", 0.5, 600);
        switch (rig.Kind)
        {
            case CutsceneRigKind.Rail:
                Number("Height (m)", rig.Height, v => rig with { Height = v }, "How high over the clicked ground the camera rides.", 0, 500);
                break;
            default:
                Number(rig.Kind == CutsceneRigKind.Crane ? "Reach (m)" : "Distance (m)", rig.Radius, v => rig with { Radius = v }, "How far out from its subject.", 1, 1000);
                Number(rig.Kind == CutsceneRigKind.Crane ? "Rise (m)" : "Height (m)", rig.Height, v => rig with { Height = v }, rig.Kind == CutsceneRigKind.Crane ? "How far the arm rises as it swings." : "How high over its subject.", -100, 1000);
                Number("Swing (°)", rig.Sweep, v => rig with { Sweep = v }, "How far round it goes; less than nought the other way.", -720, 720);
                Number("Start (°)", rig.Heading, v => rig with { Heading = v }, "Where round its subject it starts, from +X.", -360, 360);
                break;
        }

        if (!intact)
        {
            Button again = new(Theme, "Make Again from the Rig") { Tooltip = "Its keys made again from its settings: the hand edits lost" };
            again.Clicked += () => RemakeRig?.Invoke(scene, rig);
            _grid.Add(string.Empty, again);
        }

        Button plain = new(Theme, "Plain Keys") { Tooltip = "Keep its keys as they are and forget the rig" };
        plain.Clicked += () => Unrig?.Invoke(rig);
        _grid.Add(string.Empty, plain);
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
