using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Walks a Project I.G.I. level into placements.</summary>
/// <remarks>
/// <para>
/// A wrapper, and deliberately nothing more. <see cref="WorldScene.Build"/> is unchanged and still lives
/// in the shared project beside the type it produces; what moved is <em>who decides to call it</em>. The
/// host used to, which made the host a host only this game could load a mission into.
/// </para>
/// <para>
/// Its counts come back as nought because this walk does not keep them: it reports what it could not
/// place through the log as it goes, rather than tallying. That is worth leaving alone — the numbers are
/// for a developer overlay, and inventing them here would mean re-walking the scene to count what the
/// walk already logged.
/// </para>
/// </remarks>
public sealed class Igi1WorldBuilder : ITitleWorldBuilder
{
    /// <inheritdoc/>
    public WorldBuildResult Build(in WorldBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Mission);
        ArgumentNullException.ThrowIfNull(request.LevelRoot);

        Igi1Quirks quirks = request.Services?.TryGet(out Igi1Quirks? published) == true ? published : new Igi1Quirks();
        WorldScene scene = WorldScene.Build(
            request.Mission, request.LevelRoot, request.Magic, request.Logger, request.VehiclePhysics, quirks.DestroyedModelArchiveName);
        LoadedMission mission = request.Mission;
        return new WorldBuildResult(
            scene,
            0,
            0,
            Solids: ObjectCollisionIndex.Build(
                scene.Placements,
                AssetFormatProfile.Igi1.Collision,
                name => mission.TryGetModel(name, out MefModel? model) ? model : null));
    }

    /// <inheritdoc/>
    public override string ToString() => "Project I.G.I. scene walk";
}
