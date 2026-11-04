using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.World;

/// <summary>
/// Plays a skinned model's clips for a viewer: the clips its rig has, and its vertices posed at a time in one of them. Each
/// title answers with its own clips and skinner (I.G.I.'s bone animation sets, I.G.I. 2's clip library) behind this one shape.
/// </summary>
public interface IModelAnimator
{
    /// <summary>Gets the clips the model's rig can play, by name.</summary>
    IReadOnlyList<string> Clips { get; }

    /// <summary>Gets the clip the model stands in where the game places it, or -1 when that is not known.</summary>
    int StandClip => -1;

    /// <summary>Gets a clip's length.</summary>
    /// <param name="clip">The clip's index in <see cref="Clips"/>.</param>
    /// <returns>Its length in seconds.</returns>
    float Seconds(int clip);

    /// <summary>Poses the model.</summary>
    /// <param name="clip">The clip's index in <see cref="Clips"/>.</param>
    /// <param name="seconds">The time into it, wrapped by the caller.</param>
    /// <param name="destination">Receives the posed vertices, in the model's vertex order and units.</param>
    /// <returns>The vertices written.</returns>
    int Pose(int clip, float seconds, Span<Vertex3D> destination);
}

/// <summary>An animation file played on a body of the level, for a viewer.</summary>
/// <param name="Body">The model it is played on.</param>
/// <param name="BodyName">The model's name as level data spells it, for its textures.</param>
/// <param name="Animator">The file's clips on that body.</param>
/// <param name="OpenClip">The clip to open on, or -1 to let the viewer choose.</param>
/// <param name="Facts">What the file is, in a few words (<c>skeleton 1, 95 clips</c>).</param>
public sealed record AnimationFilePreview(OpenIGI.Formats.World.MefModel Body, string BodyName, IModelAnimator Animator, int OpenClip, string Facts);
