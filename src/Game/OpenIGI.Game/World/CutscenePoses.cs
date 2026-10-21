using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// The pose bookkeeping every cutscene system shares: a consist car derived from its lead, the snapshot a dynamic body
/// keeps of its pose before each step, and the revision that tells an attachment which of the two to read.
/// </summary>
internal static class CutscenePoses
{
    internal static void UpdateDerivedPose(CutsceneTransformState state)
    {
        if (state.TrainRoot is null || ReferenceEquals(state.TrainRoot, state))
        {
            return;
        }

        EvaluateDynamicPose(state, previous: false, out Vector3d position, out Matrix3f orientation);
        state.Position = position;
        state.Orientation = orientation;
    }

    internal static void SnapshotDynamicPose(CutsceneTransformState state, int simulationOrder)
    {
        state.PreviousPosition = state.Position;
        state.PreviousOrientation = state.Orientation;
        state.PreviousTrackPosition = state.TrackPosition;
        state.DynamicRevision++;
        state.LastSimulationOrder = simulationOrder;
    }

    internal static int DynamicRevision(CutsceneTransformState state) =>
        state.TrainRoot?.DynamicRevision ?? state.DynamicRevision;

    internal static void EvaluateDynamicPose(
        CutsceneTransformState state,
        bool previous,
        out Vector3d position,
        out Matrix3f orientation)
    {
        // A rail the original cannot walk leaves every car where it was (0x42A2C0 returns early).
        if (state.TrainRoot is { Consist: { } consist } root
            && consist.TryPlace(
                previous ? root.PreviousTrackPosition : root.TrackPosition,
                state.ConsistIndex,
                out position,
                out orientation))
        {
            return;
        }

        position = previous ? state.PreviousPosition : state.Position;
        orientation = previous ? state.PreviousOrientation : state.Orientation;
    }
}
