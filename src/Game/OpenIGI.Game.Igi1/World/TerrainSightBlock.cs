using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Whether the terrain stands between two points — the ground's part of a sight or bullet ray.
/// </summary>
/// <remarks>
/// <para>
/// The original's ray cast (<c>0x4CDB20</c>) tests the terrain octree whenever the category mask's
/// sign bit is set, and <b>both of the sight ray's passes send −1</b> (player.md §7.6) — so in retail a
/// hill hides you and takes the rounds fired at you. The port's collision index covers placed objects
/// only, which let guards see and shoot straight through terrain.
/// </para>
/// <para>
/// <b>Divergence, recorded.</b> This is a <i>sampled</i> stand-in, not a transcription: the ray is
/// walked in fixed steps and each column's ground height (<see cref="TerrainHeightQuery"/>) is compared
/// against the ray's own height there. The original walks the octree's leaves exactly. A sampled test
/// can miss a blade of terrain thinner than the step and treats the ground as a heightfield (an
/// overhang blocks as if solid below), but it blocks and un-blocks in the same places the real test
/// does on open ground, which is where the divergence was visible. Replace it when <c>0x4CDB20</c>'s
/// terrain arm is transcribed.
/// </para>
/// </remarks>
public static class TerrainSightBlock
{
    /// <summary>How far apart the samples sit along the ray — 2 m.</summary>
    public const double StepInUnits = 8192.0;

    /// <summary>How far above a sample the ground probe starts, so a ray hugging a slope still resolves.</summary>
    public const double ProbeAboveInUnits = 65536.0;

    /// <summary>Finds where the terrain first rises above the segment, if it does.</summary>
    /// <param name="terrain">The terrain to test.</param>
    /// <param name="from">Where the segment starts, in world units.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="hit">Receives the blocking point, on the ground surface.</param>
    /// <returns><see langword="false"/> when the segment clears the ground.</returns>
    /// <remarks>
    /// The first and last samples are skipped: both endpoints stand <i>on</i> the ground (an eye above
    /// feet, an aim point half a metre up), and testing the columns they occupy reports the floor
    /// under the shooter or the target as a blocker.
    /// </remarks>
    public static bool TryBlock(TerrainData terrain, Vector3d from, Vector3d to, out WorldRayHit hit)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        hit = default;

        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (length <= StepInUnits * 2.0)
        {
            return false;
        }

        int samples = (int)(length / StepInUnits);
        for (int i = 1; i < samples; i++)
        {
            double t = i / (double)samples;
            double x = from.X + (dx * t);
            double y = from.Y + (dy * t);
            double rayZ = from.Z + (dz * t);

            if (!TerrainHeightQuery.TryGetGround(terrain, x, y, rayZ + ProbeAboveInUnits, out TerrainGround ground))
            {
                continue;
            }

            if (ground.Height > rayZ)
            {
                hit = new WorldRayHit(
                    new Vector3d(x, y, ground.Height),
                    new Vector3f((float)ground.NormalX, (float)ground.NormalY, (float)ground.NormalZ),
                    length * t);
                return true;
            }
        }

        return false;
    }
}
