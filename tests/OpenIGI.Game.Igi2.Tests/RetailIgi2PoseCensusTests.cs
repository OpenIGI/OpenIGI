using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Every body the nineteen missions stand up can be posed: a rig the level's packs carry clips for, and every clip its list or its
/// states name. A body whose clip is not found plays nothing, and a blend into nothing writes every bone's turn as none
/// (<c>AnimBlender_Interpolate</c>) -- the soldier or figure stands in its bind pose, arms out.
/// </summary>
public sealed class RetailIgi2PoseCensusTests
{
    /// <summary>The clip every armed or unarmed soldier's tables end on when nothing better is found.</summary>
    private const string LastResortClip = "stand_lowerbody";

    /// <summary>No soldier's rig lacks its clips, and no figure names a clip its rig does not have.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_body_in_the_campaign_has_its_clips()
    {
        List<string> misses = [];
        foreach ((int location, int level) in RetailIgi2CampaignCensusTests.Missions)
        {
            Igi2Level mission = Igi2Level.Find($"location{location}", $"level{level}");
            (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(mission);
            Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
            Igi2HumanFaces faces = new();
            string where = $"{(location * 10) + level}";

            foreach (Engine.Tasks.GameTask task in mission.Root.Descendants())
            {
                switch (task)
                {
                    case Igi2HumanSoldierTask soldier:
                        Check(where, soldier.Id, faces.Make(soldier.Model), [LastResortClip]);
                        break;
                    case Igi2EditBoneObjTask figure:
                        Check(where, figure.Id, faces.Make(figure.Model), figure.Clips);
                        break;
                }
            }

            void Check(string at, int id, string model, IReadOnlyList<string> names)
            {
                if (mission.ModelFor(model)?.BoneHierarchy is not { } rig)
                {
                    misses.Add($"{at} #{id} {model}: no boned model");
                    return;
                }

                if (clips.ClipsOf(rig).Count == 0)
                {
                    misses.Add($"{at} #{id} {model}: its rig has no clips");
                    return;
                }

                foreach (string name in names.Where(n => clips.Find(rig, n) is null))
                {
                    misses.Add($"{at} #{id} {model}: no clip '{name}'");
                }
            }
        }

        // The residue is the shipped data's, not the port's: soldier_1 is in no archive of the installation and igiagent_1 only in
        // other levels' (RetailIgi2ModelTableTests). Retail makes these bodies with no model and draws nothing; so does the port.
        string[] dataGaps =
        [
            "14 #3288 igiagent_1: no boned model",
            "31 #3197 soldier_1: no boned model",
            "31 #3171 soldier_1: no boned model",
        ];
        Assert.True(
            misses.Order(StringComparer.Ordinal).SequenceEqual(dataGaps.Order(StringComparer.Ordinal)),
            $"{misses.Count} bodies cannot be posed:\n  {string.Join("\n  ", misses.Take(80))}");
    }
}
