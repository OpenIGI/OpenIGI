namespace OpenIGI.Rendering;

/// <summary>
/// The surface cache's own lookup, as a GLSL snippet shared by every pass that reads a hit's radiance
/// out of the cards.
/// </summary>
/// <remarks>
/// <para>
/// A traced ray returns a point in the world, and a point is not light. What turns one into the other is
/// the card the point lies on: the surface cache captured the level as a set of rectangles, lit them, and
/// left the result in an atlas — so finding which card covers a point, and where on it, is the whole of
/// how a trace becomes radiance.
/// </para>
/// <para>
/// <b>This lives here, below both the passes that use it, for one reason: there must be exactly one copy
/// of it.</b> The gather that fills the indirect light and the reflection pass that fills a mirror ask the
/// same question of the same buffers, and two hand-kept copies of a hash and a projection would drift —
/// a bucket function that disagreed by one prime, or a depth tolerance that disagreed by a centimetre,
/// would put the two passes on different surfaces and neither would look wrong on its own.
/// </para>
/// <para>
/// <b>A snippet, not a shader.</b> It declares no version, no bindings and no uniforms: every function
/// here reads globals the host shader is expected to have declared — the page records, the lookup grid,
/// and the card atlases. Concatenated into a host that declares those, it compiles.
/// </para>
/// <para>
/// Globals the host must declare before including this: the page records <c>uPages</c>, the lookup grid
/// <c>uCells</c> and <c>uCellPages</c>; <c>sampler2D uAlbedo</c> and <c>sampler2D uDepth</c> over the card
/// atlases; <c>vec3 uAtlasSize</c>; <c>int uLookupBuckets</c>, <c>vec3 uLookupMin</c>, <c>float
/// uLookupCell</c>; and the constants <c>kVectorsPerPage</c>, <c>kCardSkin</c> and <c>kCardSettled</c>.
/// </para>
/// </remarks>
public static class SurfaceCacheShaders
{
    /// <summary>The card layout's own numbers, to declare before <see cref="Lookup"/>.</summary>
    /// <remarks>
    /// Here for the reason the lookup itself is here: there must be exactly one copy. These three
    /// describe how the pages were WRITTEN — how many vectors a page record spans, how far outside its
    /// rectangle a point may stray and still belong to a card, and how close a candidate's recorded depth
    /// must come before the search stops looking at the rest. A reader that disagrees with the builder
    /// about any of them does not fail; it quietly reads the wrong card, which looks like a reflection
    /// landing on the wrong wall rather than like a bug.
    /// </remarks>
    public const string Constants =
        """
        const int kVectorsPerPage = 10;
        const float kCardSkin = 0.25;
        const float kCardSettled = 0.02;
        """;

    /// <summary>The lookup's functions, to concatenate after a host shader's own declarations.</summary>
    public const string Lookup =
        """
        // Which bucket a cell is filed under. Teschner's three primes, one per axis; must match
        // SurfaceCacheLookup.Bucket exactly, or a page is filed where no lookup will look for it.
        //
        // Hashed rather than laid out in a box because a level is not a box: its geometry sits in a few
        // dense clumps kilometres apart, with strays tens of kilometres further still, and a box grid has
        // to stretch one cell size over all of it. See SurfaceCacheLookup.CellMetres for what that cost.
        int LookupBucket(ivec3 cell)
        {
            uint hash = (uint(cell.x) * 0x8DA6B343u) ^ (uint(cell.y) * 0xD8163841u) ^ (uint(cell.z) * 0xCB1AB31Fu);
            return int(hash & uint(uLookupBuckets - 1));
        }

        // Which card page covers a point, and where on it.
        //
        // The grid narrows the field to a handful of candidates; the work here is deciding between them,
        // because a point on a wall sits inside the box of every card of that wall and of whatever stands
        // against it. Each candidate is taken back into its own placement's space, projected onto its
        // card, and rejected if it falls outside the rectangle. What survives is judged on DEPTH: the
        // card recorded how far behind its plane the surface was, so the candidate whose recorded depth
        // best matches where the point actually is is the one whose geometry the point belongs to. Without
        // that test a floor reads as the underside of the ceiling above it.
        // A point taken onto one page's card: where across the rectangle it falls, in [-1, 1] each way
        // when inside; how far behind the card's plane it sits; the card's own depth and facing; and a
        // direction carried into the same space, for a caller that has a surface normal to judge by.
        //
        // Every lookup below goes through this, which is the point of it: there is one projection, so
        // the reflections and the gather cannot disagree about which card a point falls on.
        void ProjectOntoCard(
            int page,
            vec3 world,
            vec3 directionW,
            out vec2 across,
            out vec2 halfSize,
            out float behind,
            out float depth,
            out vec3 facing,
            out vec3 directionLocal,
            out vec4 litRect,
            out vec4 cardPage)
        {
            litRect = uPages[(page * kVectorsPerPage) + 0];
            cardPage = uPages[(page * kVectorsPerPage) + 1];
            vec4 centreAndHalfWidth = uPages[(page * kVectorsPerPage) + 2];
            vec4 rightAndHalfHeight = uPages[(page * kVectorsPerPage) + 3];
            vec4 upAndDepth = uPages[(page * kVectorsPerPage) + 4];
            vec4 direction = uPages[(page * kVectorsPerPage) + 5];
            vec4 row0 = uPages[(page * kVectorsPerPage) + 6];
            vec4 row1 = uPages[(page * kVectorsPerPage) + 7];
            vec4 row2 = uPages[(page * kVectorsPerPage) + 8];

            // Back into the placement's own space. The rows carry model-to-world and the lighting
            // applies them transposed, so undoing that is the untransposed multiply.
            vec3 delta = world - vec3(row0.w, row1.w, row2.w);
            vec3 local = vec3(dot(row0.xyz, delta), dot(row1.xyz, delta), dot(row2.xyz, delta));
            directionLocal = vec3(dot(row0.xyz, directionW), dot(row1.xyz, directionW), dot(row2.xyz, directionW));
            vec3 relative = local - centreAndHalfWidth.xyz;

            halfSize = max(vec2(centreAndHalfWidth.w, rightAndHalfHeight.w), vec2(1e-6));
            across = vec2(dot(relative, rightAndHalfHeight.xyz), dot(relative, upAndDepth.xyz)) / halfSize;

            // How far behind the plane the point is. The capture looked along -Direction, so a
            // surface further from the plane is more negative along it.
            behind = -dot(relative, direction.xyz);
            depth = upAndDepth.w;
            facing = direction.xyz;
        }

        // The same question asked with a stated tolerance, for a caller whose point is not exactly on the
        // surface it means.
        //
        // A ray-triangle intersection lands ON the geometry, and the quarter-metre skin below is sized for
        // that. A hit found by MARCHING a distance field does not: the field is sampled at voxel centres
        // and can place a surface no better than a fraction of a voxel, which on the outer clipmaps is
        // most of a metre, so such a hit floats in front of the wall it struck. Judged by the skin, four
        // fifths of them project onto no card at all -- the slab test throws them out before the depth
        // test can choose between candidates -- and the glazing on a machine without ray tracing came back
        // as a level-sized hole. The tolerance is the caller's because only the caller knows how good its
        // own hit is; what it must NOT be is a wider constant here, which would loosen the depth test for
        // the gather as well and let a floor start reading as the ceiling above it.
        bool LookupCardWithin(
            vec3 world,
            float skin,
            out vec2 found,
            out vec2 litFound,
            out float agreement,
            out float cardDepth)
        {
            cardDepth = 0.0;
            found = vec2(0.0);
            litFound = vec2(0.0);
            agreement = 1e9;

            if (uLookupBuckets <= 0)
            {
                return false;
            }

            // floor, not a truncating cast: a point on the negative side of the grid's origin would
            // otherwise round towards zero and land one cell over from the page filed there. The build
            // floors too, and the two have to agree on every point.
            ivec3 cell = ivec3(floor((world - uLookupMin) / uLookupCell));
            ivec2 range = uCells[LookupBucket(cell)];

            bool any = false;
            bool settled = false;

            for (int slot = 0; slot < range.y && !settled; slot++)
            {
                int page = uCellPages[range.x + slot];

                // One read to reject a page whose card cannot reach this point, before the nine it takes to
                // project onto it. The sphere bounds every point the projection accepts, so a point outside
                // it is one the projection would reject anyway -- same answer, shorter walk. This is what
                // the busiest cell's page count, the whole cost of the gather, actually turned on.
                vec4 bound = uPages[(page * kVectorsPerPage) + 9];
                vec3 toCentre = world - bound.xyz;
                if (dot(toCentre, toCentre) > bound.w * bound.w)
                {
                    continue;
                }

                vec2 across;
                vec2 halfSize;
                float behind;
                float depth;
                vec3 facing;
                vec3 unusedDirection;
                vec4 litRect;
                vec4 cardPage;
                ProjectOntoCard(
                    page, world, vec3(0.0),
                    across, halfSize, behind, depth, facing, unusedDirection, litRect, cardPage);

                if (abs(across.x) > 1.0 || abs(across.y) > 1.0)
                {
                    continue;
                }

                if (behind < -skin || behind > depth + skin)
                {
                    continue;
                }

                vec2 uv = (across * 0.5) + 0.5;
                vec2 texel = (cardPage.xy + (uv * cardPage.zw)) / vec2(textureSize(uAlbedo, 0));

                // Unwritten card texels describe nothing, whatever their box says.
                if (texture(uAlbedo, texel).a < 0.5)
                {
                    continue;
                }

                float recorded = texture(uDepth, texel).r * depth;
                float error = abs(recorded - behind);

                if (error < agreement)
                {
                    agreement = error;

                    // How deep the card's own capture reaches, handed back so a caller can judge the
                    // error against it. A card is a FLAT capture: on a wall the surface it describes sits
                    // on its plane and any disagreement is quantisation, but on a curved object the
                    // surface bulges away from that plane by up to the object's own size, and no fixed
                    // tolerance can be right for both. This is the number that tells them apart.
                    cardDepth = depth;
                    found = texel;
                    settled = error < kCardSettled;

                    // The same surface in the lighting atlas, which is packed to its own layout: the card
                    // atlas and the lighting atlas allocate the same size per card but not at the same
                    // place, so a bounce reading the card atlas's coordinates would read a different
                    // object's light.
                    litFound = (litRect.xy + (uv * litRect.zw)) / uAtlasSize.xy;
                    any = true;
                }
            }

            return any;
        }

        // And the ordinary form, for a caller whose hit is exact and does not care how deep the card
        // it landed on was.
        bool LookupCard(vec3 world, out vec2 found, out vec2 litFound, out float agreement)
        {
            float depth;
            return LookupCardWithin(world, kCardSkin, found, litFound, agreement, depth);
        }

        // What the cards say a point on a surface radiates, in the reference's own shape, for a host that
        // names its lighting atlas as kCardLighting. A host that does not gets none of this compiled.
        //
        // NOT "which one card best matches", which is what LookupCardWithin answers and what the
        // reflections used to take. A card is chosen by the surface's NORMAL. A model's box has up to six,
        // and the one that looked this way is the one that photographed this surface: the two Z cards of
        // a storage tank hold its roof and its floor, and a point on its wall is the side cards' business
        // alone. Judged on depth error alone the roof card WON the wall -- its silhouette texel recorded
        // the floor beneath, which happened to be the nearest number -- and the wall came out as a fan of
        // stripes, one per silhouette texel column, in every window that reflected it.
        //
        // So every card facing the normal is sampled, weighted by how squarely it faced (the squared
        // normal component along its axis, as the reference weights it), and the four texels under the
        // bilinear footprint are each judged on their own against the depth the card recorded THERE: a
        // texel never written, or one that recorded a surface some way in front of or behind this point,
        // drops out and the rest are renormalised. The result is a continuous weight, and the radiance is
        // the weighted mean over everything that survived. Nothing here is a threshold on one number,
        // which is what left a rash of marks wherever a surface sat near the bound.
        //
        // The bias is the caller's, in metres: how far off the surface its hit may float. A triangle hit
        // lands on the geometry and passes the skin; a marched hit passes the voxel that found it. Half
        // of it is allowed outside the box on every side, and the depth test's threshold is the bias over
        // the card's half depth with a falloff of a quarter of that, both exactly as the reference sizes
        // them, so the two paths accept the same neighbourhood of a card.
        //
        // Returns false where no texel of any facing card described the point. The caller then lights the
        // hit itself, exactly as the reference does when its cache has no answer.
        #ifdef kCardLighting
        bool SampleCardsWithin(vec3 world, vec3 normalW, float bias, out vec3 radiance)
        {
            radiance = vec3(0.0);

            if (uLookupBuckets <= 0)
            {
                return false;
            }

            ivec3 cell = ivec3(floor((world - uLookupMin) / uLookupCell));
            ivec2 range = uCells[LookupBucket(cell)];
            vec2 albedoSize = vec2(textureSize(uAlbedo, 0));
            float weightSum = 0.0;

            for (int slot = 0; slot < range.y; slot++)
            {
                int page = uCellPages[range.x + slot];

                // The bounding sphere, widened by the slack the box test below allows.
                vec4 bound = uPages[(page * kVectorsPerPage) + 9];
                vec3 toCentre = world - bound.xyz;
                float reach = bound.w + bias;
                if (dot(toCentre, toCentre) > reach * reach)
                {
                    continue;
                }

                vec2 across;
                vec2 halfSize;
                float behind;
                float depth;
                vec3 facing;
                vec3 normalLocal;
                vec4 litRect;
                vec4 cardPage;
                ProjectOntoCard(
                    page, world, normalW,
                    across, halfSize, behind, depth, facing, normalLocal, litRect, cardPage);

                // Card projection angle: only a card that looked the way this surface faces, and the
                // more squarely the more it counts. The facing is a unit axis of the model, so this is
                // the squared component of the local normal along that axis.
                float towards = dot(facing, normalLocal);
                if (towards <= 0.0)
                {
                    continue;
                }

                float normalWeight = towards * towards;

                // Inside the box, allowing half the bias out of it on every side; then clamped onto the
                // rectangle, so a point a little past an edge reads the edge texel rather than nothing.
                vec2 slack = vec2(0.5 * bias) / halfSize;
                if (any(greaterThan(abs(across), vec2(1.0) + slack))
                    || behind < -0.5 * bias
                    || behind > depth + (0.5 * bias))
                {
                    continue;
                }

                vec2 uv = (clamp(across, vec2(-1.0), vec2(1.0)) * 0.5) + 0.5;

                // The four texels under the bilinear footprint and their weights, in the order a gather
                // returns them: (i, j+1), (i+1, j+1), (i+1, j), (i, j).
                vec2 texel = (cardPage.xy + (uv * cardPage.zw)) / albedoSize;
                vec2 f = fract((texel * albedoSize) - 0.5);
                vec4 bilinear = vec4(
                    (1.0 - f.x) * f.y,
                    f.x * f.y,
                    f.x * (1.0 - f.y),
                    (1.0 - f.x) * (1.0 - f.y));

                // Each texel judged on its own: unwritten ones describe nothing, and a written one has to
                // have recorded this point's depth, within the threshold, with the falloff past it.
                vec4 written = step(vec4(0.5), textureGather(uAlbedo, texel, 3));
                vec4 recorded = textureGather(uDepth, texel, 0);

                float hitDepth = behind / max(depth, 1e-6);
                float threshold = bias / max(0.5 * depth, 1e-6);
                float falloff = 0.25 * threshold;
                vec4 visibility = written
                    * (1.0 - clamp((abs(vec4(hitDepth) - recorded) - threshold) / falloff, 0.0, 1.0));

                vec4 weights = bilinear * visibility;
                float texelWeightSum = weights.x + weights.y + weights.z + weights.w;
                float cardWeight = normalWeight * texelWeightSum;
                if (cardWeight <= 0.0)
                {
                    continue;
                }

                weights /= texelWeightSum;

                // The same texels in the lighting atlas, which is packed to its own layout but allocates
                // each card the same size, so the footprint is the same four texels there.
                vec2 lit = (litRect.xy + (uv * litRect.zw)) / uAtlasSize.xy;
                vec3 lighting = vec3(
                    dot(textureGather(kCardLighting, lit, 0), weights),
                    dot(textureGather(kCardLighting, lit, 1), weights),
                    dot(textureGather(kCardLighting, lit, 2), weights));

                radiance += max(lighting, vec3(0.0)) * cardWeight;
                weightSum += cardWeight;
            }

            if (weightSum <= 0.0)
            {
                return false;
            }

            radiance /= weightSum;
            return true;
        }
        #endif
        """;
}
