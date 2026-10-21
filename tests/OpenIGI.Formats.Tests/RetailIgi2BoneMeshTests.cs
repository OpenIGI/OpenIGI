using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>I.G.I. 2's bone meshes carry their own skeleton, and the shipped clips fit them.</summary>
public sealed class RetailIgi2BoneMeshTests
{
    /// <summary>Every skinned model names one bone per hierarchy entry and skins within it.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_skinned_model_carries_a_named_hierarchy()
    {
        int skinned = 0;
        foreach ((string name, MefModel model) in Igi2CommonModels.Models())
        {
            if (model.Format != MefMeshFormat.Skinned)
            {
                continue;
            }

            skinned++;
            Assert.True(model.BoneHierarchy is not null, $"{name} is skinned but carries no hierarchy.");
            Assert.Equal(model.BoneHierarchy!.BoneCount, model.BoneNames.Count);
            Assert.True(model.HighestSkinBone < model.BoneHierarchy.BoneCount, $"{name} skins to a bone it does not have.");
        }

        Assert.True(skinned > 0);
    }

    /// <summary>A human model has the bones <c>HumanAnim_Init</c> asks for by name.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_human_model_has_the_bones_the_engine_names()
    {
        MefModel jones = Igi2CommonModels.Models().Single(static m => m.Name == "jones_1").Model;

        foreach (string bone in new[]
                 {
                     "center", "lower body", "upper body", "shoulders", "head",
                     "upper left arm", "lower left arm", "left hand", "upper right arm", "lower right arm",
                     "right hand", "upper left leg", "lower left leg", "left foot", "upper right leg",
                     "lower right leg", "right foot",
                 })
        {
            Assert.Contains(bone, jones.BoneNames);
        }
    }

    /// <summary>The shipped clips fit a human model's hierarchy shape.</summary>
    /// <remarks>Matched the engine's way, by the child-count bytes alone.</remarks>
    [RequiresTitleDataFact("igi2")]
    public void Clips_fit_a_human_model_by_shape()
    {
        Igi2BoneHierarchy human = Igi2CommonModels.Models().Single(static m => m.Name == "jones_1").Model.BoneHierarchy!;

        int fitting = Igi2CommonModels.Clips().Count(c => c.Clip.Hierarchy.HasShapeOf(human));

        Assert.True(fitting > 100, $"Only {fitting} clips fit the human hierarchy.");
    }
}
