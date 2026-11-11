using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The fence vault's footfalls: animation 171's own events, which <c>Human_FireAnimTrigger</c> (0x4613D0) plays off the
/// body's clip as <c>walk_fence_1..4</c>. The interaction body must report them, or the vault is silent.
/// </summary>
public sealed class FenceVaultSoundTests
{
    /// <summary>HSTATE 41's clip, the vault over a climbable fence.</summary>
    private const int FenceVaultAnimation = 171;

    [RequiresGameDataFact]
    public void TheFenceVaultClipCrossesItsWalkFenceTriggers()
    {
        AnimTriggerRegistry triggers = LoadTriggers();
        int[] fenceEvents = [.. triggers.Definitions.Values
            .Where(static trigger => trigger.Resource.StartsWith("walk_fence_", StringComparison.Ordinal))
            .Select(static trigger => trigger.EventId)];
        Assert.Equal(4, fenceEvents.Length);

        SimulatedLadderBody body = new(BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff"))));
        body.StartInteractionClip(FenceVaultAnimation, loops: false);

        HashSet<int> heard = [];
        for (int tick = 0; tick < 600; tick++)
        {
            body.AdvanceLadderClip(0f, out Vector3d _, out float? _, out bool ended);
            foreach (int id in fenceEvents)
            {
                if (body.CrossedEvent(id))
                {
                    heard.Add(id);
                }
            }

            if (ended)
            {
                break;
            }
        }

        Assert.NotEmpty(heard);
    }

    private static AnimTriggerRegistry LoadTriggers()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        OpenIGI.Game.Flow.LevelTaskTypes.Register(loop.Types);
        AnimTriggerRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Load(resources, loop.Natives);
        return registry;
    }
}
