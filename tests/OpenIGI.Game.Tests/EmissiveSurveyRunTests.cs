using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Runs the emissive survey over retail level 1.
/// </summary>
/// <remarks>
/// The survey is how a level's light fixtures are found at all, since I.G.I. declares no lights and
/// every lamp is geometry with a texture. This pins that it still separates them from ordinary
/// surfaces: a change that made every sunlit wall score highly would not fail anything else.
/// </remarks>
public sealed class EmissiveSurveyRunTests
{
    [RequiresGameDataFact]
    public void LevelOnesLightFixtureStandsOutFromEverySurfaceAroundIt()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));

        IReadOnlyList<EmissiveTextureSurvey.Finding> findings =
            EmissiveTextureSurvey.Survey(scene, new ResourceRegistry(fileSystem));

        Assert.NotEmpty(findings);

        // 236_01_1 is lit pure white on all thirty of its uses. Nothing else in the level manages
        // that: the next candidates sit at 0.85 over two uses, and the ordinary walls -- which are
        // merely sunlit -- are bright on a tenth of their uses at most.
        EmissiveTextureSurvey.Finding top = findings[0];

        Assert.Equal("236_01_1", top.Texture);
        Assert.True(top.MeanLuminance > 0.95f, $"the top candidate averages only {top.MeanLuminance:F2}");
        Assert.Equal(top.Uses, top.BrightUses);

        // And it has to be a clear win rather than a photo finish, or the ranking is not telling us
        // anything the noise would not.
        Assert.True(
            top.Score > findings[1].Score * 1.5f,
            $"{top.Texture} scored {top.Score:F3} against {findings[1].Texture} at {findings[1].Score:F3}");
    }
}
