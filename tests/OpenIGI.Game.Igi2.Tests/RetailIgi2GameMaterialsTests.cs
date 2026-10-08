using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The session's materials, read from <c>material/material.qvm</c> after the weapons.</summary>
public sealed class RetailIgi2GameMaterialsTests
{
    private static readonly Lazy<Igi2GameMaterials> Materials = new(static () => Igi2Session.Start().Require<Igi2GameMaterials>());

    /// <summary>
    /// The script fills rows 0 to 32 of the table's 64, each with its name, its resistance to a bullet and its sounds,
    /// every argument in its place: concrete stops four points of a bullet's penetration and rings as the ground does.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Concrete_takes_four_points_of_a_bullets_penetration()
    {
        Igi2GameMaterials materials = Materials.Value;

        Igi2GameMaterial concrete = materials[11]!;
        Assert.Equal("Concrete", concrete.Name);
        Assert.Equal(4f, concrete.PenetrationPower);
        Assert.Equal(25f, concrete.MaxAngleForRicochet);
        Assert.Equal(["bul_ground_1", "bul_ground_2", "bul_ground_3", "bul_ground_4", "bul_ground_5", "bul_ground_6"], concrete.BulletHitSounds);
        Assert.Equal("knh_concrete", concrete.KnifeHitSound);
        Assert.Equal("Branches", materials[32]!.Name);
        Assert.Null(materials[33]);
        Assert.Null(materials[64]);
    }

    /// <summary>
    /// Each row's drawing half comes first (<c>DefineQMaterial</c>): glass lets a line of sight through whole, a fence
    /// four fifths of it, concrete none.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Glass_lets_sight_through_and_concrete_does_not()
    {
        Igi2GameMaterials materials = Materials.Value;

        Assert.Equal(1f, materials[14]!.QMaterial.Transparency);
        Assert.Equal(0.8f, materials[18]!.QMaterial.Transparency);
        Assert.Equal(0f, materials[11]!.QMaterial.Transparency);
        Assert.Equal(0.9f, materials[11]!.QMaterial.Density);
    }

    /// <summary>
    /// Both of a material's effects are gunshot effects that read six parameters each: wood shows a hole where a bullet
    /// goes on and a dent where it stops, its hole two centimetres across in world units.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Wood_leaves_a_hole_or_a_dent()
    {
        Igi2GameMaterial wood = Materials.Value[3]!;

        Igi2GunShotEffectParameters enter = wood.BulletEnterEffect.Parameters!;
        Assert.True(enter.IsSmoke);
        Assert.Equal("bullethole_wood.tex", enter.PenetratingBulletHole);
        Assert.Equal("dent-wood.tex", enter.NonPenetratingBulletHole);
        Assert.Equal((new Fpu(0.02f) * 4096.0).F, enter.BulletHoleSize);
        Assert.Equal("hole-wood-out.tex", wood.BulletLeaveEffect.Parameters!.PenetratingBulletHole);
        Assert.Equal(wood.BulletEnterEffect.TaskType, wood.BulletLeaveEffect.TaskType);
    }
}
