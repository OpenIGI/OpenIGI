using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using static OpenIGI.Game.World.CutsceneCameraMath;
using static OpenIGI.Game.World.CutscenePoses;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>Reads a scene's EditCamera knots and builds its camera frame at a scene time.</summary>
/// <remarks>
/// The knots are flattened depth-first (<c>0x4F4880</c>) and interpolated with the non-uniform cubic Hermite of
/// <c>0x4F55D0</c>; a shot's Link and Target are resolved through the live poses at the moment the frame is built,
/// and latched by the shot's update flags.
/// </remarks>
internal sealed class CutsceneCameraFrames(LevelMotionContext context, CutsceneAttachments attachments)
{
    private readonly LevelMotionContext _context = context;
    private readonly CutsceneAttachments _attachments = attachments;

    /// <summary>Gets or sets what supplies live poses for targets owned by another simulation, such as animated humans.</summary>
    public Func<int, CutsceneTargetPose?>? TargetPoseResolver { get; set; }

    /// <summary>Interpolates one scene's authored camera at an arbitrary scene time.</summary>
    public CutsceneFrame? BuildFrame(CutsceneSceneState state, double time)
    {
        CutSceneTask scene = state.Task;
        List<CutsceneTimedShot> timed = state.Timed(scene.TimeScale);

        // The path through the knots is the shared one the editor previews with (CutsceneCameraMath.Evaluate): every
        // authored knot kept, the last start not after the time chosen, each channel on 0x4F55D0's Hermite.
        CutscenePathPoint point = CutsceneCameraMath.Evaluate(state.Keys, scene.TimeScale, time, scene.TimeOfDay);
        int current = point.Current;
        int i2 = point.Next;
        Vector3d position = point.Position;
        Matrix3f orientation = point.Orientation;
        float fov = point.FieldOfView;
        float noise = point.Noise;
        float shake = point.Shake;
        float timeOfDay = point.TimeOfDay;
        (double spanStart, double spanEnd) = (point.SpanStart, point.SpanEnd);

        EditCameraShot active = timed[current].Shot;
        bool shotChanged = state.LastShotIndex != current;
        if (shotChanged || active.LinkTaskId < 0)
        {
            state.ShotSpace = active.LinkTaskId < 0 ? CutsceneShotSpace.World : null;
        }

        if (active.LinkTaskId >= 0 && TryPose(active.LinkTaskId, out TaskTransformState? link))
        {
            if (shotChanged || active.UpdateLink || state.LinkedPosition is null)
            {
                // Retail sub_508940: the authored offset through the link's pose as loaded, then through its live pose
                // (CutsceneShotSpace, which the editor writes a view back through). The space is kept with the frame, so an
                // edit made on a shot that took its link once is written through the pose it took.
                CutsceneShotSpace space = new(active.LinkOrigin, active.LinkOriginOrientation, link!.Position, link.Orientation);
                (position, orientation) = space.ToWorld(position, orientation);
                state.ShotSpace = space;
                state.LinkedPosition = position;
                state.LinkedOrientation = orientation;
            }
            else
            {
                position = state.LinkedPosition.Value;
                orientation = state.LinkedOrientation ?? orientation;
            }
        }
        if (active.TargetTaskId >= 0 && TryPose(active.TargetTaskId, out TaskTransformState? target))
        {
            if (shotChanged || active.UpdateTarget || state.TargetOrientation is null)
            {
                orientation = LookAt(position, target!.Position);
                state.TargetOrientation = orientation;
            }
            else
            {
                orientation = state.TargetOrientation.Value;
            }
        }
        state.LastShotIndex = current;

        // Retail uses its deterministic RNG here. A tick-derived hash gives the same important property:
        // every replay of a scene shakes identically and rendering the same frame never mutates logic.
        if (shake != 0f)
        {
            uint seed = unchecked((uint)((scene.Id * 397) ^ scene.Ticks));
            position += new Vector3d(Noise(ref seed), Noise(ref seed), Noise(ref seed)) * (shake * WorldScene.UnitsPerMetre);
            orientation = Matrix3f.FromEngineEulerAngles(
                Noise(ref seed) * shake * 0.53407079f,
                Noise(ref seed) * shake * 0.53407079f,
                Noise(ref seed) * shake * 0.53407079f) * orientation;
        }

        double total = state.TotalTime(scene.TimeScale);
        float fadeIn = scene.ViewportFadeIn > 0f ? Math.Clamp((float)(time / scene.ViewportFadeIn), 0f, 1f) : 1f;
        float fadeOut = scene.ViewportFadeOut > 0f ? Math.Clamp((float)((total - time) / scene.ViewportFadeOut), 0f, 1f) : 1f;
        float fade = Math.Min(fadeIn, fadeOut);
        float viewportHeight = 1f + ((scene.ViewportHeightFactor - 1f) * fade);
        float local = spanEnd > spanStart ? Math.Clamp((float)((time - spanStart) / (spanEnd - spanStart)), 0f, 1f) : 0f;
        float colorLocal = local;
        if (noise == 0f && IsBandFilter(active.Filter) && spanEnd - spanStart > 0.5)
        {
            // DrawBandFilter completes its colour transition half a second before a long zero-noise
            // segment ends (0x4F5E20), leaving the final half-second as a settled hold.
            colorLocal = Math.Clamp(
                (float)((time - spanStart) / ((spanEnd - spanStart) - 0.5)), 0f, 1f);
        }

        return new CutsceneFrame(
            // EditCamera keeps a proper engine rotation (determinant +1), whereas Camera3D's left-handed
            // right/up/forward view basis has determinant -1. Its columns become our rows and the up row
            // changes sign at that boundary. A transpose alone points the cinematic camera's up axis down,
            // putting the landscape overhead and exposing the terrain skirts seen in the broken intro.
            new Camera3D(position / WorldScene.UnitsPerMetre, ToCameraOrientation(orientation)),
            fov,
            Math.Clamp(viewportHeight, 0f, 1f),
            active.Filter,
            noise,
            Vector3f.Lerp(active.FilterColor, timed[i2].Shot.FilterColor, colorLocal),
            timeOfDay,
            scene,
            current);
    }

    private bool TryPose(int id, out TaskTransformState? state)
    {
        if (TargetPoseResolver?.Invoke(id) is { } live)
        {
            if (!_context.Transforms.TryGetValue(id, out state))
            {
                state = new TaskTransformState(live.Position, live.Orientation);
                _context.Transforms[id] = state;
            }
            else
            {
                state.Position = live.Position;
                state.Orientation = live.Orientation;
            }
            return true;
        }

        if (!_context.Transforms.TryGetValue(id, out state))
        {
            return false;
        }

        if (_context.Root.Tree.FromId(id) is { } task
            && _attachments.TryEvaluateAttachment(task, out Vector3d attachedPosition, out Matrix3f attachedOrientation))
        {
            state.Position = attachedPosition;
            state.Orientation = attachedOrientation;
            return true;
        }

        // A consist child has no independent AnimTask.  Retail asks its railroad assembly for the
        // current frame whenever EditCamera resolves a Link, not only later when the model is drawn.
        // Leaving this update in the renderer made linked cameras follow the car's spawn pose forever.
        UpdateDerivedPose(state);
        return true;
    }

    /// <summary>Reads a scene's EditCamera knots, each with its Link task's initial pose cached as <c>UpdateInternalData</c> caches it.</summary>
    public List<EditCameraShot> ReadShots(CutSceneTask scene)
    {
        List<EditCameraShot> shots = [];
        // IGI1 0x4F55D0 interpolates EditCamera's authored XYZ fields directly.
        // Small coordinates can belong to a separate cinematic set (Mission 4's Anya HQ);
        // they are not evidence of a vehicle-local offset. Only explicit Link IDs transform
        // the evaluated camera through 0x508940. See the MVP retail audit for both listings.
        foreach (GameTask task in scene.Descendants())
        {
            if (task is not DeclaredTask camera || !string.Equals(task.Type.Name, "EditCamera", StringComparison.Ordinal))
            {
                continue;
            }
            int link = IntStrict(camera, "Link task ID", -1);
            int target = IntStrict(camera, "Target task ID", -1);
            (Vector3d linkOrigin, Matrix3f linkOrientation) = InitialPose(scene.Tree.FromId(link));
            Vector3d position = Vector(camera, "Position");
            shots.Add(new EditCameraShot(
                position,
                RealStrict(camera, "Alpha"), RealStrict(camera, "Beta"), RealStrict(camera, "Gamma"),
                Math.Clamp(RealStrict(camera, "FOV", 1f), 0.001f, 3f), RealStrict(camera, "Duration"),
                link, BoolStrict(camera, "Update link continously"), target, BoolStrict(camera, "Update target continously"),
                // The names the levels declare (Task_DeclareParameters("EditCamera", ...)): the original reads both by their
                // place in its table (editcamera.c), so a name that matches nothing here read the shot's hour and colour as
                // nought -- every band and line filter of every level came out black instead of the colour it was given.
                BoolStrict(camera, "Smooth to next"), RealStrict(camera, "Time of day (-1 means use default)", -1f),
                TextOrEmpty(camera, "FILTER"), RealStrict(camera, "Noise"), Vector32(camera, "Filter color"),
                RealStrict(camera, "Camera shake"), linkOrigin, linkOrientation));
        }
        return shots;
    }

    /// <summary>
    /// Keeps every task's pose as the level built it, before anything moves: what a shot's link starts from when an editor
    /// changes the shot -- or links it to another task -- in a level that has run since.
    /// </summary>
    public void RememberInitialPoses()
    {
        foreach ((GameTask task, TaskTransformState transform) in _context.TaskTransforms)
        {
            _initialPoses[task] = (transform.Position, transform.Orientation);
        }
    }

    private readonly Dictionary<GameTask, (Vector3d Position, Matrix3f Orientation)> _initialPoses = [];

    private (Vector3d Position, Matrix3f Orientation) InitialPose(GameTask? task)
    {
        if (task is not null && _initialPoses.TryGetValue(task, out (Vector3d Position, Matrix3f Orientation) built))
        {
            return built;
        }

        if (task is not null && _context.TaskTransforms.TryGetValue(task, out TaskTransformState? transform))
        {
            return (transform.Position, transform.Orientation);
        }
        if (task is HumanSoldierTask soldier)
        {
            return (soldier.Position, Matrix3f.FromEngineEulerAngles(0f, 0f, soldier.Gamma));
        }
        if (task is DeclaredTask declared)
        {
            Vector3f angles = Vector32(declared, "Orientation");
            return (Vector(declared, "Position"), Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z));
        }
        return (default, Matrix3f.Identity);
    }
}
