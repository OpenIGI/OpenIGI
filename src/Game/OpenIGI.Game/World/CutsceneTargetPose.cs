using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>A live target pose supplied to a cutscene in engine units.</summary>
public readonly record struct CutsceneTargetPose(Vector3d Position, Matrix3f Orientation);
