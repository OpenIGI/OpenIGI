using OpenIGI.Core.Diagnostics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>The two animation sets Project I.G.I.'s player is made of, under <c>common/anims</c>: the body's and the first-person rig's.</summary>
/// <param name="Body">Skeleton 0's set, <c>000.iff</c>: without it the player can stand and fall but not walk.</param>
/// <param name="ViewRig">Skeleton 5's set, <c>005.iff</c>, the rig every Human builds: without it nothing can be held.</param>
public sealed record Igi1PlayerAnimations(BoneAnimationSet? Body, BoneAnimationSet? ViewRig)
{
    /// <summary>The body's set.</summary>
    public const string BodySetName = "000.iff";

    /// <summary>The first-person rig's set.</summary>
    public const string ViewRigSetName = "005.iff";

    /// <summary>Reads both sets from the game's install; one that is missing or unreadable is reported and left out.</summary>
    /// <param name="gameRoot">The directory the game is installed in.</param>
    /// <param name="logger">Where a missing set is reported.</param>
    /// <returns>The sets.</returns>
    public static Igi1PlayerAnimations Read(string gameRoot, IOpenIgiLogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameRoot);
        ArgumentNullException.ThrowIfNull(logger);
        return new Igi1PlayerAnimations(
            ReadSet(gameRoot, BodySetName, "the player cannot walk", logger),
            ReadSet(gameRoot, ViewRigSetName, "no weapon can be held", logger));
    }

    private static BoneAnimationSet? ReadSet(string gameRoot, string name, string consequence, IOpenIgiLogger logger)
    {
        string path = Path.Combine(gameRoot, "common", "anims", name);
        try
        {
            if (File.Exists(path))
            {
                return BoneAnimationSet.Read(File.ReadAllBytes(path));
            }

            logger.Warning("Player", $"No animation set at {path}; {consequence}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            logger.Warning("Player", $"Could not read {path}: {ex.Message}; {consequence}.");
        }

        return null;
    }
}
