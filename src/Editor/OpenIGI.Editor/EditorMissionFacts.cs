using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor;

/// <summary>What the running level says about the open mission, for the checks that need more than its script.</summary>
/// <param name="host">The running game.</param>
/// <param name="workspace">The open mission and its project.</param>
internal sealed class EditorMissionFacts(IEditorHost host, MissionWorkspace workspace) : IMissionFacts
{
    // How far below a point a surface is looked for, and how far above one the ground is looked for when nothing is below.
    private const double SearchDownMetres = 300.0;
    private const double SearchUpMetres = 500.0;

    // A point a little above the one asked, so a body standing exactly on a surface finds it.
    private const double LeadMetres = 0.3;

    /// <inheritdoc/>
    public MissionTarget Target => workspace.Mission is { } mission ? workspace.Project.TargetOf(mission.Id) : MissionTarget.Retail;

    /// <inheritdoc/>
    public int MountainAreas => host.Terrain?.Mountains.Count ?? 0;

    /// <inheritdoc/>
    public bool? HasModel(string model) => host.HasModel(model);

    /// <inheritdoc/>
    /// <remarks>
    /// The first surface below, a roof or a floor as much as the terrain, as a body placed there lands on; when there is
    /// none, the terrain above, which is the ground over a buried one.
    /// </remarks>
    public double? GroundUnder(Vector3d pointMetres)
    {
        if (host.SurfaceBelow(new Vector3d(pointMetres.X, pointMetres.Y, pointMetres.Z + LeadMetres), SearchDownMetres) is { } below)
        {
            return below;
        }

        double units = WorldScene.UnitsPerMetre;
        return host.Ground is { } ground
            && ground.TryDrop(pointMetres.X * units, pointMetres.Y * units, (pointMetres.Z + SearchUpMetres) * units, SearchUpMetres * units, upward: false, out TerrainGround above)
                ? above.Height / units
                : null;
    }

    /// <inheritdoc/>
    public IEditorCommand? MakeOpenIgiOnly()
    {
        if (workspace.Mission is not { } mission)
        {
            return null;
        }

        EditorProject project = workspace.Project;
        MissionTarget before = project.TargetOf(mission.Id);
        return new DelegateCommand(
            "Make Mission OpenIGI Only",
            () => project.SetTarget(mission.Id, MissionTarget.OpenIgi),
            () => project.SetTarget(mission.Id, before));
    }
}
