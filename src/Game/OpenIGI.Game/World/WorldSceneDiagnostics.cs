using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.World;

namespace OpenIGI.Game.World;

/// <summary>
/// The scene walk's verification log: one loader record per placed object, in the form the reference
/// level verifier reads, with the authored-to-runtime comparison beside it.
/// </summary>
internal static class WorldSceneDiagnostics
{
    /// <summary>
    /// Emits the same per-object record consumed by the reference level verifier, while also recording
    /// the authored-to-runtime comparison. The model id is the pack instance id, not a friendly catalog
    /// name: that is the identity the retail loader uses when it adds the object.
    /// </summary>
    internal static void LogPlacementVerification(
        IOpenIgiLogger log,
        LoadedMission mission,
        DeclaredTask task,
        string modelId,
        Vector3d runtimePositionMetres,
        Matrix3f runtimeOrientation,
        int modelSlot,
        bool isTrain,
        bool isCharacter,
        bool isCameraLens)
    {
        string type = task.Type.Name;
        string name = task.Name;
        string source = $"LOCAL:{mission.Descriptor.MissionDirectory}/objects.qvm";
        string positionText = FormatPosition(runtimePositionMetres * WorldScene.UnitsPerMetre);
        string orientationText = FormatOrientation(runtimeOrientation);

        bool hasAuthoredPosition = WorldScene.TryGetPosition(task, out Vector3d authoredPositionMetres);
        Matrix3f authoredOrientation = WorldPlacementOrientation.PlacementOrientation(task, modelSlot);
        bool positionMatch = hasAuthoredPosition
            && LevelPositionValidator.PosMatch(
                authoredPositionMetres * WorldScene.UnitsPerMetre,
                runtimePositionMetres);
        bool orientationMatch = LevelPositionValidator.OriMatch(authoredOrientation, runtimeOrientation);
        string verification = isTrain
            ? "DERIVED_TRAIN"
            : isCameraLens
                ? "DERIVED_CAMERA_LENS"
            : !hasAuthoredPosition
                ? "DERIVED_RUNTIME"
                : isCharacter && !positionMatch
                    ? "EXPECTED_TERRAIN_GROUNDING"
                    : positionMatch && orientationMatch ? "PASS" : "MISMATCH";

        log.Information(
            WorldScene.LogCategory + ".Loader",
            $"Object Loaded: ModelID={modelId}, Type={type}, Name={name}, "
                + $"Pos={positionText}, Ori={orientationText}, Tex={modelId}, Model={modelId}, "
                + $"Source={source}, PositionMatch={positionMatch}, OrientationMatch={orientationMatch}, "
                + $"Verification={verification}");

        if (verification == "MISMATCH")
        {
            log.Error(
                WorldScene.LogCategory + ".Loader",
                $"Object verification failed: ModelID={modelId}, Type={type}, Name={name}, "
                    + $"Source={source}, authoredPos={FormatPosition(authoredPositionMetres * WorldScene.UnitsPerMetre)}, "
                    + $"runtimePos={positionText}, authoredOri={FormatOrientation(authoredOrientation)}, "
                    + $"runtimeOri={orientationText}");
        }
    }

    /// <summary>
    /// Emits the loader record for a placement the scene derived rather than read from a task's own
    /// position: a spline segment or a model attachment.
    /// </summary>
    internal static void LogDerivedPlacement(
        IOpenIgiLogger log,
        LoadedMission mission,
        DeclaredTask? owner,
        string modelId,
        Vector3d positionMetres,
        Matrix3f orientation,
        string sourceKind)
    {
        string type = owner?.Type.Name ?? "Derived";
        string name = owner?.Name ?? string.Empty;
        string source = $"LOCAL:{mission.Descriptor.MissionDirectory}/objects.qvm";
        log.Information(
            WorldScene.LogCategory + ".Loader",
            $"Object Loaded: ModelID={modelId}, Type={type}, Name={name}, "
                + $"Pos={FormatPosition(positionMetres * WorldScene.UnitsPerMetre)}, Ori={FormatOrientation(orientation)}, "
                + $"Tex={modelId}, Model={modelId}, Source={source}, PositionMatch=True, "
                + $"OrientationMatch=True, Verification=DERIVED_{sourceKind}");
    }

    private static string FormatPosition(Vector3d position) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({position.X:F6}, {position.Y:F6}, {position.Z:F6})");

    private static string FormatOrientation(Matrix3f matrix)
    {
        float beta = MathF.Asin(Math.Clamp(-matrix.M31, -1f, 1f));
        float alpha = MathF.Atan2(matrix.M32, matrix.M33);
        float gamma = MathF.Atan2(matrix.M21, matrix.M11);
        return string.Create(CultureInfo.InvariantCulture, $"({alpha:F6}, {beta:F6}, {gamma:F6})");
    }
}
