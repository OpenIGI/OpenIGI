using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>
/// The orientation a placing task's model is drawn with, and where a security camera's lens sits on
/// its holder.
/// </summary>
/// <remarks>
/// Shared by the scene walk, which places the model, and by <see cref="WorldSceneDiagnostics"/>, which
/// checks the placement against what the task authored: both must read the same angles the same way.
/// </remarks>
internal static class WorldPlacementOrientation
{
    /// <summary>Builds the orientation a placing task's model is drawn with.</summary>
    /// <param name="task">The placing task.</param>
    /// <param name="modelSlot">Which of the task's models, in <see cref="WorldScene.PlacedModelNames"/> order.</param>
    /// <returns>The rotation.</returns>
    internal static Matrix3f PlacementOrientation(DeclaredTask task, int modelSlot)
    {
        if (string.Equals(task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal))
        {
            if (modelSlot == 0)
            {
                return CameraHolderOrientation(task);
            }

            float holderGamma = RealStrict(task, "Holder Gamma");
            float cameraAlpha = RealStrict(task, "Camera Alpha");
            float cameraGamma = RealStrict(task, "Camera Gamma");
            return Matrix3f.FromEngineEulerAngles(cameraAlpha, 0f, holderGamma + cameraGamma);
        }

        // A door's base orientation (+272) and a fence's turn are built by 0x4B38E0 exactly: the door's pose is
        // recomputed from it every tick it moves, and must start where the drawn placement stands.
        if (string.Equals(task.Type.Name, "Door", StringComparison.Ordinal)
            && task.TryGetValue(WorldScene.OrientationParameter, out object? doorAngles)
            && doorAngles is Vector3f door)
        {
            return RetailOrientation.EulerMatrix(door.X, door.Y, door.Z);
        }

        if (string.Equals(task.Type.Name, "Fence", StringComparison.Ordinal)
            && task.TryGetValue("Gamma", out object? fenceGamma)
            && fenceGamma is float fence)
        {
            return RetailOrientation.EulerMatrix(0f, 0f, fence);
        }

        if (task.TryGetValue(WorldScene.OrientationParameter, out object? rawOrientation))
        {
            return rawOrientation switch
            {
                Vector3f angles => WorldScene.ToOrientation(angles),
                float gamma => Matrix3f.RotationZ(gamma),
                _ => Matrix3f.Identity,
            };
        }

        // Point objects may expose their heading as a standalone Gamma instead of the three-angle
        // Orientation value. Fence does exactly that: its interaction path already consumes Gamma,
        // and drawing the model at identity left the visible mesh on a different plane from the vault.
        return task.TryGetValue("Gamma", out object? rawGamma) && rawGamma is float value
            ? Matrix3f.RotationZ(value)
            : Matrix3f.Identity;
    }

    private static Matrix3f CameraHolderOrientation(DeclaredTask task) =>
        Matrix3f.RotationZ(RealStrict(task, "Holder Gamma"));

    /// <summary>Places a security camera's lens on its holder model's mount.</summary>
    /// <param name="task">The <c>SCamera</c> task.</param>
    /// <param name="holderPosition">The holder's position, in metres.</param>
    /// <param name="byName">The bound pack instances, which supply the holder model.</param>
    /// <returns>The lens position, in metres; the holder's own when its model has no mount.</returns>
    internal static Vector3d CameraLensPosition(
        DeclaredTask task,
        Vector3d holderPosition,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName)
    {
        string holderName = task.TryGetValue(WorldScene.SecurityCameraModelParameters[0], out object? raw)
            && raw is string declaredName
            && declaredName.Length > 0
                ? declaredName
                : WorldScene.SecurityCameraModels[0];

        if (!byName.TryGetValue(holderName, out var holder)
            || !holder.Model.TryGetMagicVertex(0, out Vector3f mount))
        {
            return holderPosition;
        }

        // Matrix3f stores the engine's row-major basis, while the holder-to-world operation here is
        // M*v. Transposing before the row-vector helper gives the same product as retail sub_414E20.
        Vector3f offset = CameraHolderOrientation(task).Transposed().Transform(mount);
        return new Vector3d(
            holderPosition.X + (offset.X / WorldScene.UnitsPerMetre),
            holderPosition.Y + (offset.Y / WorldScene.UnitsPerMetre),
            holderPosition.Z + (offset.Z / WorldScene.UnitsPerMetre));
    }
}
