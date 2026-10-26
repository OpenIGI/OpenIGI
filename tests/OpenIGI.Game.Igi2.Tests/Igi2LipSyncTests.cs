using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A spoken line moves its speakers' mouths by its sound's lip-sync track (<c>StatusMessage_UpdateMorph</c>), and the skin draws a
/// face's morphs before posing it (<c>Direct3DRender_MorphVerticesBone</c>).
/// </summary>
public sealed class Igi2LipSyncTests
{
    [RequiresTitleDataFact("igi2")]
    public void A_line_opens_its_speakers_mouths_by_its_track_and_shuts_them_when_it_runs_out()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2LipSync tracks = Igi2LipSync.Load(MissionFiles(), SoundDirectories());

        // Mission 1's intro: White's line names both of his figures.
        ReadOnlySpan<float> track = tracks.Track("cv11_intro_01_white");
        Assert.Equal(470, track.Length);
        Igi2StatusMessageTask line = m.Tree.OfType<Igi2StatusMessageTask>().Single(static t => t.Id == 2992);
        Igi2EditBoneObjTask white = m.Tree.OfType<Igi2EditBoneObjTask>().Single(static t => t.Id == 3111);
        Igi2EditBoneObjTask other = m.Tree.OfType<Igi2EditBoneObjTask>().Single(static t => t.Id == 3125);

        m.Sounds.LipSync = tracks;
        line.TaskById = id => m.Tree.FirstOrDefault(t => t.Id == id);
        line.IsForceSend = true;

        int opened = 0;
        for (int tick = 0; tick < track.Length; tick++)
        {
            _ = line.Step(tick, static () => false);
            Assert.Equal(track[tick], white.MorphWeights[Igi2MorphWeights.Mouth]);
            Assert.Equal(track[tick], other.MorphWeights[Igi2MorphWeights.Mouth]);
            opened += track[tick] != 0f ? 1 : 0;
        }

        Assert.True(opened > 0);
        _ = line.Step(track.Length, static () => false);
        Assert.Equal(0f, white.MorphWeights[Igi2MorphWeights.Mouth]);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_faces_morphs_move_its_vertices_before_the_skin_poses_them()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        MefModel white = m.Level.ModelFor("white_1")!;
        Assert.Equal([57, 5], white.Morph!.ChannelVertexCounts[..2].ToArray());

        Igi2BoneSkin skin = Igi2BoneSkin.Create(white)!;
        int bones = white.BoneHierarchy!.BoneCount;
        BonePose pose = BonePose.FromBones(
            Enumerable.Repeat(Matrix3f.Identity, bones).ToArray(),
            new Vector3f[bones],
            Enumerable.Repeat(BoneSkeleton.NoParent, bones).ToArray());
        Vertex3D[] shut = new Vertex3D[skin.VertexCount];
        Vertex3D[] open = new Vertex3D[skin.VertexCount];
        Vertex3D[] blinking = new Vertex3D[skin.VertexCount];
        _ = skin.Skin(pose, 1f, shut, new float[MefMorph.Channels]);
        float[] mouth = new float[MefMorph.Channels];
        mouth[Igi2MorphWeights.Mouth] = 1f;
        _ = skin.Skin(pose, 1f, open, mouth);
        float[] eyelids = new float[MefMorph.Channels];
        eyelids[Igi2MorphWeights.Eyelids] = 1f;
        _ = skin.Skin(pose, 1f, blinking, eyelids);

        Assert.Equal(57, Moved(shut, open));
        Assert.Equal(5, Moved(shut, blinking));
    }

    private static int Moved(Vertex3D[] a, Vertex3D[] b) => a.Zip(b).Count(static p => p.First.Position != p.Second.Position);

    private static VirtualFileSystem MissionFiles()
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        return files;
    }

    private static string[] SoundDirectories() =>
        [.. ((string[])["LOCAL:common/sounds", "LOCAL:missions/location1/common/sounds", "MISSION:sounds"])
            .SelectMany(static d => new[] { d, $"{d}/{Igi2SoundDefinitions.DefaultLanguage}" })];
}
