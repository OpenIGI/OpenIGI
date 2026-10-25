using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Lighting;

/// <summary>
/// The page records the card lighting reads: every page's atlas rectangles, card and placement packed
/// into floats, the tiles the pages are cut into, and the level origin those floats are measured from.
/// </summary>
/// <remarks>
/// Written once at build and again, for a page that moves, by <see cref="SurfaceCacheMovers"/>; the
/// shaders and <see cref="SurfaceCacheLookup"/> read the same layout.
/// </remarks>
internal static class SurfaceCachePageRecords
{
    /// <summary>The square of texels one work group covers.</summary>
    internal const int TileSize = 8;

    /// <summary>How many floats one page's record occupies. Ten <c>vec4</c>s: nine of card and placement,
    /// then a world-space bounding sphere the card lookup rejects candidates against.</summary>
    internal const int FloatsPerPage = 40;

    /// <summary>How far past a card's own depth a point may sit and still be taken as on it.</summary>
    /// <remarks>Matches the trace shader's <c>kCardSkin</c>; the bounding sphere has to allow for it.</remarks>
    internal const float CardSkin = 0.25f;

    /// <summary>The middle of everything lit, which the GPU's floats are measured from.</summary>
    internal static Vector3d CentreOf(SurfaceCacheInstances instances)
    {
        if (instances.Instances.Length == 0)
        {
            return default;
        }

        Vector3d total = default;

        foreach (SurfaceCacheInstances.Placed placed in instances.Instances)
        {
            total = new Vector3d(
                total.X + placed.Position.X, total.Y + placed.Position.Y, total.Z + placed.Position.Z);
        }

        int count = instances.Instances.Length;
        return new Vector3d(total.X / count, total.Y / count, total.Z / count);
    }

    /// <summary>Packs every page's record, and cuts the pages into tiles.</summary>
    internal static (float[] Pages, int[] Tiles) Describe(
        SurfaceCacheCapture.Result capture, SurfaceCacheInstances instances, Vector3d origin)
    {
        SurfaceCacheLayout layout = instances.Layout;
        float[] packed = new float[layout.Pages.Length * FloatsPerPage];
        List<int> tiles = [];

        foreach (SurfaceCacheInstances.Placed placed in instances.Instances)
        {
            for (int slot = 0; slot < placed.PageCount; slot++)
            {
                int page = placed.FirstPage + slot;
                SurfaceCacheLayout.Page lightPage = layout.Pages[page];

                if (!lightPage.IsAllocated)
                {
                    continue;
                }

                int cardIndex = instances.CardForPage[page];
                MeshCard card = capture.Cards[cardIndex];
                SurfaceCacheLayout.Page cardPage = capture.Layout.Pages[cardIndex];

                int at = page * FloatsPerPage;

                packed[at + 0] = lightPage.X;
                packed[at + 1] = lightPage.Y;
                packed[at + 2] = lightPage.Width;
                packed[at + 3] = lightPage.Height;

                packed[at + 4] = cardPage.X;
                packed[at + 5] = cardPage.Y;
                packed[at + 6] = cardPage.Width;
                packed[at + 7] = cardPage.Height;

                packed[at + 8] = card.Centre.X;
                packed[at + 9] = card.Centre.Y;
                packed[at + 10] = card.Centre.Z;
                packed[at + 11] = card.HalfWidth;

                packed[at + 12] = card.Right.X;
                packed[at + 13] = card.Right.Y;
                packed[at + 14] = card.Right.Z;
                packed[at + 15] = card.HalfHeight;

                packed[at + 16] = card.Up.X;
                packed[at + 17] = card.Up.Y;
                packed[at + 18] = card.Up.Z;
                packed[at + 19] = card.Depth;

                packed[at + 20] = card.Direction.X;
                packed[at + 21] = card.Direction.Y;
                packed[at + 22] = card.Direction.Z;
                packed[at + 23] = 0f;

                WritePlacement(packed, at, card, placed.Position, placed.Orientation, origin);

                // Cut into tiles now, on the CPU. A work group takes one of these and knows its page
                // without searching for it; a page that moves keeps its tiles, which address the atlas.
                int across = (lightPage.Width + TileSize - 1) / TileSize;
                int down = (lightPage.Height + TileSize - 1) / TileSize;

                for (int y = 0; y < down; y++)
                {
                    for (int x = 0; x < across; x++)
                    {
                        tiles.Add(page);
                        tiles.Add(x);
                        tiles.Add(y);
                        tiles.Add(0);
                    }
                }
            }
        }

        return (packed, tiles.Count > 0 ? [.. tiles] : [0, 0, 0, 0]);
    }

    /// <summary>Writes where one page's placement stands: its rotation, its position and its world bound.</summary>
    /// <param name="packed">Every page's record.</param>
    /// <param name="at">Where this page's record starts.</param>
    /// <param name="card">The page's card, in the model's own space.</param>
    /// <param name="position">Where the placement stands.</param>
    /// <param name="orientation">How it is turned.</param>
    /// <param name="origin">The level origin the cache works relative to.</param>
    internal static void WritePlacement(
        float[] packed, int at, MeshCard card, Vector3d position, Matrix3f orientation, Vector3d origin)
    {
        // The placement's rotation, a row per register, with its position relative to the level
        // origin riding in the spare component. This is the model-to-world rotation, so the
        // shader applies it transposed -- matching how SceneDistanceField reaches world space.
        Matrix3f rotation = orientation;
        Vector3d placedPosition = position;

        packed[at + 24] = rotation.M11;
        packed[at + 25] = rotation.M12;
        packed[at + 26] = rotation.M13;
        packed[at + 27] = (float)(placedPosition.X - origin.X);

        packed[at + 28] = rotation.M21;
        packed[at + 29] = rotation.M22;
        packed[at + 30] = rotation.M23;
        packed[at + 31] = (float)(placedPosition.Y - origin.Y);

        packed[at + 32] = rotation.M31;
        packed[at + 33] = rotation.M32;
        packed[at + 34] = rotation.M33;
        packed[at + 35] = (float)(placedPosition.Z - origin.Z);

        // A world-space bounding sphere for the page, so the card lookup can reject a candidate
        // whose card cannot reach this point with one buffer read instead of the nine it takes to
        // project onto it -- the busiest grid cell holds most of a room's pages and the walk tests
        // every one, almost all nowhere near the point. Built from the card's eight corners put
        // through the very transform the lookup and SurfaceCacheLookup.TryBounds use (the placement
        // rotation carries scale, so a world radius cannot be inferred from the local half-extents),
        // with the depth range widened by the skin the projection tolerates. The AABB of those
        // corners bounds every point the projection can accept; the sphere bounds the AABB. It is
        // therefore never smaller than it must be, so no point the walk would have kept is rejected
        // -- the picture is identical, the walk merely shorter.
        float px = (float)(placedPosition.X - origin.X);
        float py = (float)(placedPosition.Y - origin.Y);
        float pz = (float)(placedPosition.Z - origin.Z);
        float loX = float.MaxValue, loY = float.MaxValue, loZ = float.MaxValue;
        float hiX = float.MinValue, hiY = float.MinValue, hiZ = float.MinValue;
        for (int corner = 0; corner < 8; corner++)
        {
            float cu = (corner & 1) == 0 ? -card.HalfWidth : card.HalfWidth;
            float cv = (corner & 2) == 0 ? -card.HalfHeight : card.HalfHeight;
            float cw = (corner & 4) == 0 ? CardSkin : -(card.Depth + CardSkin);

            float lx = card.Centre.X + (card.Right.X * cu) + (card.Up.X * cv) + (card.Direction.X * cw);
            float ly = card.Centre.Y + (card.Right.Y * cu) + (card.Up.Y * cv) + (card.Direction.Y * cw);
            float lz = card.Centre.Z + (card.Right.Z * cu) + (card.Up.Z * cv) + (card.Direction.Z * cw);

            float wx = px + (rotation.M11 * lx) + (rotation.M21 * ly) + (rotation.M31 * lz);
            float wy = py + (rotation.M12 * lx) + (rotation.M22 * ly) + (rotation.M32 * lz);
            float wz = pz + (rotation.M13 * lx) + (rotation.M23 * ly) + (rotation.M33 * lz);

            loX = MathF.Min(loX, wx); hiX = MathF.Max(hiX, wx);
            loY = MathF.Min(loY, wy); hiY = MathF.Max(hiY, wy);
            loZ = MathF.Min(loZ, wz); hiZ = MathF.Max(hiZ, wz);
        }

        float ex = (hiX - loX) * 0.5f, ey = (hiY - loY) * 0.5f, ez = (hiZ - loZ) * 0.5f;
        packed[at + 36] = (loX + hiX) * 0.5f;
        packed[at + 37] = (loY + hiY) * 0.5f;
        packed[at + 38] = (loZ + hiZ) * 0.5f;
        packed[at + 39] = (1.001f * MathF.Sqrt((ex * ex) + (ey * ey) + (ez * ez))) + 1e-3f;
    }
}
