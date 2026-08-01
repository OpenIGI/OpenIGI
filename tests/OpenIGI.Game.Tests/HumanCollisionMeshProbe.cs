using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The human collision mesh carries a rig bone per vertex — the data the faithful bullet-vs-bone trace
/// (<see cref="OpenIGI.Game.Player.HumanHitTest.TryCastMesh"/>) skins and scores against.
/// </summary>
/// <remarks>
/// Verified against igi.exe's own retail models: a skinned human ships an <c>HSMC</c> collision mesh whose
/// 16-byte <c>XTVC</c> vertex is a <c>float3</c> position and one trailing <c>u32</c> — the rig bone the
/// vertex follows (IGI2 <c>MultiMesh_Vertex_t</c>'s bone field). The values are the 33-bone rig's own
/// numbering, so <see cref="OpenIGI.Game.Player.HumanDamage.ZoneOfBone"/> keys straight off them.
/// </remarks>
public sealed class HumanCollisionMeshProbe
{
    [RequiresGameDataFact]
    public void SkinnedHumanCollisionMeshesCarryRigBonesPerVertex()
    {
        int humansWithMesh = 0;

        foreach (MefModel model in RetailLevelOneGround.Models())
        {
            int maxInfluenceBone = -1;
            foreach (MefSkinInfluence inf in model.SkinInfluences)
            {
                maxInfluenceBone = Math.Max(maxInfluenceBone, inf.BoneIndex);
            }

            // A human rig spreads its skin over the whole 33-bone hierarchy; skip props and static scenery.
            if (maxInfluenceBone < 20 || maxInfluenceBone > 32)
            {
                continue;
            }

            if (!MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? mesh) || mesh!.Vertices.Count == 0)
            {
                continue;
            }

            humansWithMesh++;

            Assert.Equal(mesh.Vertices.Count, mesh.VertexBones.Count);

            // Every collision vertex names a real rig bone, and the set spans more than one — it is genuine
            // per-bone skinning, not a column of zeros.
            var used = new HashSet<int>();
            foreach (int bone in mesh.VertexBones)
            {
                Assert.InRange(bone, 0, 32);
                used.Add(bone);
            }

            Assert.True(used.Count > 8, $"only {used.Count} distinct bones — not a skinned collision mesh");
        }

        Assert.True(humansWithMesh > 0, "no skinned human model shipped a collision mesh");
    }
}
