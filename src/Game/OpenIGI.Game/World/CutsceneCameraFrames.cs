using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using static OpenIGI.Game.World.DeclaredTaskValues;
using static OpenIGI.Game.World.CutscenePoses;
using static OpenIGI.Game.World.CutsceneCameraMath;

namespace OpenIGI.Game.World;

/// <summary>Reads a scene's EditCamera knots and builds its camera frame at a scene time.</summary>
/// <remarks>
/// The knots are flattened depth-first (<c>0x4F4880</c>) and interpolated with the non-uniform cubic Hermite of
/// <c>0x4F55D0</c>; a shot's Link and Target are resolved through the live poses at the moment the frame is built,
/// and latched by the shot's update flags.
/// </remarks>
internal sealed class CutsceneCameraFrames(CutsceneContext context, CutsceneAttachments attachments)
{
    private readonly CutsceneContext _context = context;
    private readonly CutsceneAttachments _attachments = attachments;

    /// <summary>Gets or sets what supplies live poses for targets owned by another simulation, such as animated humans.</summary>
    public Func<int, CutsceneTargetPose?>? TargetPoseResolver { get; set; }

    /// <summary>Interpolates one scene's authored camera at an arbitrary scene time.</summary>
    public CutsceneFrame? BuildFrame(CutsceneSceneState state, double time)
    {
        CutSceneTask scene = state.Task;
        List<CutsceneTimedShot> timed = state.Timed(scene.TimeScale);
        // IGI1 0x4F4880 retains every authored knot, including duration zero.
        // 0x4F5400 selects the last start <= the scene time without a duration filter.
        // Scene completion/presentation must not be repaired by changing knot ownership.
        int current = 0;
        for (int i = 1; i < timed.Count; i++)
        {
            if (timed[i].Start > time)
            {
                break;
            }
            current = i;
        }

        int i0 = Math.Max(0, current - 1);
        int i1 = current;
        int i2 = Math.Min(timed.Count - 1, current + 1);
        int i3 = Math.Min(timed.Count - 1, current + 2);

        CutsceneTimedShot p0 = timed[i0];
        CutsceneTimedShot p1 = timed[i1];
        CutsceneTimedShot p2 = timed[i2];
        CutsceneTimedShot p3 = timed[i3];
        if (!p0.Shot.SmoothToNext) { p0 = p1; }
        if (!p1.Shot.SmoothToNext) { p2 = p1; }
        if (!p2.Shot.SmoothToNext) { p3 = p2; }

        Vector3d position = new(
            Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Position.X, p1.Shot.Position.X, p2.Shot.Position.X, p3.Shot.Position.X),
            Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Position.Y, p1.Shot.Position.Y, p2.Shot.Position.Y, p3.Shot.Position.Y),
            Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Position.Z, p1.Shot.Position.Z, p2.Shot.Position.Z, p3.Shot.Position.Z));
        float alpha = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Alpha, p1.Shot.Alpha, p2.Shot.Alpha, p3.Shot.Alpha);
        float beta = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Beta, p1.Shot.Beta, p2.Shot.Beta, p3.Shot.Beta);
        float gamma = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Gamma, p1.Shot.Gamma, p2.Shot.Gamma, p3.Shot.Gamma);
        float fov = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.FieldOfView, p1.Shot.FieldOfView, p2.Shot.FieldOfView, p3.Shot.FieldOfView);
        float shake = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Shake, p1.Shot.Shake, p2.Shot.Shake, p3.Shot.Shake);
        float noise = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start,
            p0.Shot.Noise, p1.Shot.Noise, p2.Shot.Noise, p3.Shot.Noise);
        static float ShotTimeOfDay(EditCameraShot shot, CutSceneTask owner) =>
            shot.TimeOfDay < 0f ? owner.TimeOfDay : shot.TimeOfDay;
        float timeOfDay = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start,
            ShotTimeOfDay(p0.Shot, scene), ShotTimeOfDay(p1.Shot, scene),
            ShotTimeOfDay(p2.Shot, scene), ShotTimeOfDay(p3.Shot, scene));
        Matrix3f orientation = Matrix3f.FromEngineEulerAngles(alpha, beta, gamma);

        EditCameraShot active = timed[current].Shot;
        bool shotChanged = state.LastShotIndex != current;
        if (active.LinkTaskId >= 0 && TryPose(active.LinkTaskId, out CutsceneTransformState? link))
        {
            if (shotChanged || active.UpdateLink || state.LinkedPosition is null)
            {
                // Retail sub_508940 first maps the authored world offset through the cached link
                // orientation, then maps that local offset through the transpose of the live link.
                // Matrix3f.Transform is the engine's row-vector multiply, so both transposes here are
                // significant. Reversing them makes a linked cinematic camera orbit on the wrong axes.
                Vector3d relative = active.LinkOriginOrientation.Transform(position - active.LinkOrigin);
                position = link!.Position + link.Orientation.Transposed().Transform(relative);
                orientation = link.Orientation * active.LinkOriginOrientation.Transposed() * orientation;
                state.LinkedPosition = position;
                state.LinkedOrientation = orientation;
            }
            else
            {
                position = state.LinkedPosition.Value;
                orientation = state.LinkedOrientation ?? orientation;
            }
        }
        if (active.TargetTaskId >= 0 && TryPose(active.TargetTaskId, out CutsceneTransformState? target))
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
        float local = p2.Start > p1.Start ? Math.Clamp((float)((time - p1.Start) / (p2.Start - p1.Start)), 0f, 1f) : 0f;
        float colorLocal = local;
        if (noise == 0f && IsBandFilter(active.Filter) && p2.Start - p1.Start > 0.5)
        {
            // DrawBandFilter completes its colour transition half a second before a long zero-noise
            // segment ends (0x4F5E20), leaving the final half-second as a settled hold.
            colorLocal = Math.Clamp(
                (float)((time - p1.Start) / ((p2.Start - p1.Start) - 0.5)), 0f, 1f);
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

    private bool TryPose(int id, out CutsceneTransformState? state)
    {
        if (TargetPoseResolver?.Invoke(id) is { } live)
        {
            if (!_context.Transforms.TryGetValue(id, out state))
            {
                state = new CutsceneTransformState(live.Position, live.Orientation);
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
                BoolStrict(camera, "Smooth to next"), RealStrict(camera, "Time of day", -1f),
                TextOrEmpty(camera, "FILTER"), RealStrict(camera, "Noise"), Vector32(camera, "Filter color RGB"),
                RealStrict(camera, "Camera shake"), linkOrigin, linkOrientation));
        }
        return shots;
    }

    private (Vector3d Position, Matrix3f Orientation) InitialPose(GameTask? task)
    {
        if (task is not null && _context.TaskTransforms.TryGetValue(task, out CutsceneTransformState? transform))
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
