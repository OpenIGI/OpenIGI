// The geometry pass: world geometry into albedo, world normal, material and baked light, all linear.
//
// Everything the lighting reads is written here, and everything written here is written once per
// pixel: a terrain pixel is drawn by more than one pass, but only the BASE pass writes the normal
// and material attachments -- the additive ones have their colour write mask cleared on every
// attachment but the albedo (see GBufferPipeline).

#include "../Common/GBufferEncoding.slang"
#include "../Common/SampledColour.slang"
#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"

struct GBufferPush
{
    float4x4 modelViewProjection;
    float4 normal0;

    // w: how much of the previous pose is in the position drawn.
    float4 normal1;
    float4 normal2;

    // roughness, metallic, emissive, lightmapScale.
    float4 material;
};

OPENIGI_PUSH_CONSTANT(GBufferPush, push);

[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLightmap;
[[vk::binding(3, 0)]] SamplerState uLightmapSampler;

// The derived surface detail belonging to this draw's diffuse: the tangent-space normal's x and y,
// a roughness multiplier, and how dielectric the surface is. I.G.I. ships none of this, so it comes
// from a map derived from the diffuse itself. A diffuse with none registered is given the neutral
// map, whose every channel is the identity of the term it drives.
[[vk::binding(4, 0)]] Texture2D<float4> uDetail;
[[vk::binding(5, 0)]] SamplerState uDetailSampler;

// The frame's own surface settings, which are the same for every draw in it.
struct SurfaceSettings
{
    // detail strength, floor gloss, unused, unused.
    float4 surface;
};

[[vk::binding(0, 1)]] ConstantBuffer<SurfaceSettings> frame;

// The cut-out threshold, baked into the pipeline. D3DRS_ALPHAREF out of 255: the default is the half
// this port always tested at, I.G.I. 2 asks for 64 generally and 32 for foliage. It has to be the
// same value here as in the shadow pass and the forward mesh, or the passes disagree.
[vk::constant_id(0)] const float cAlphaReference = 0.5019608;

struct VertexInput
{
    [[vk::location(0)]] float3 position;
    [[vk::location(1)]] float3 normal;
    [[vk::location(2)]] float2 diffuseUv;
    [[vk::location(3)]] float2 lightmapUv;
    [[vk::location(4)]] float4 colorBgra;
    [[vk::location(5)]] float4 surfaceNormal;

    // The mesh's PREVIOUS pose, per vertex. A static mesh reports its own buffer as this one, so its
    // delta is exactly zero and no blend of it can move a vertex; a CPU-skinned body keeps the pose
    // it was last given in a second buffer, which is what lets a frame be drawn BETWEEN two
    // simulation ticks rather than snapped onto the last one.
    [[vk::location(6)]] float3 previousPosition;
    [[vk::location(7)]] float3 previousNormal;
};

struct Varyings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 diffuseUv;
    [[vk::location(1)]] float2 lightmapUv;
    [[vk::location(2)]] float4 color;
    [[vk::location(3)]] float3 worldNormal;
    [[vk::location(4)]] float fade;

    // A position in WORLD AXES, for the tangent frame the detail map is applied through. It is not
    // the world position -- the draw's translation is missing, because the push block carries a
    // rotation and no translation -- and it does not need to be: the frame is built from this
    // value's screen-space DERIVATIVES, and a constant offset over a primitive differs by zero.
    [[vk::location(5)]] float3 surfacePosition;
};

/// The four attachments the geometry pass writes.
struct GBufferTargets
{
    float4 baseColour : SV_Target0;
    float4 normal : SV_Target1;
    float4 material : SV_Target2;
    float4 baked : SV_Target3;
};

/// The vertex stage: clip position, world normal, and the terrain's level-of-detail morph.
[shader("vertex")]
Varyings vertexMain(VertexInput input)
{
    Varyings output;

    // Terrain says so in the fourth component of the first normal row, which is zero for everything
    // else. When it does, the rows carry the level-of-detail ramp and the page's offset from the
    // camera rather than a rotation, because a terrain page has none.
    // One for I.G.I. 1's ground and two for its detail pass; THREE is the sequel's ground, which
    // wants none of the level-of-detail machinery below -- it morphs on the CPU and carries a real
    // normal -- and only asks the fragment stage to multiply its detail map in. So it is read out of
    // the flag here and every branch that follows is asked about the first game's.
    float terrain = push.normal0.w;
    bool ramped = terrain > 0.5 && terrain < 2.5;

    // Written as base + t * delta rather than as a mix: the delta is exactly zero for every mesh that
    // does not animate, so at any blend those come out as input.position to the bit and the world's
    // picture cannot depend on what the last animated draw left set.
    float poseBlend = push.normal1.w;
    float3 poseDelta = input.previousPosition - input.position;
    float3 localPosition = input.position + poseBlend * poseDelta;
    float morph = 0.0;
    output.fade = 1.0;

    if (ramped)
    {
        // scale, offset, slope.
        float3 ramp = push.normal0.xyz;
        float3 fromCamera = localPosition + push.normal1.xyz;

        // The vertex slides towards where it sits at the next level of detail, and how far along that
        // slide it is depends on how far away it ends up -- measured on the target rather than the
        // start, so two pages meeting at a shared vertex agree on it and no crack opens between them.
        // The distance is the largest of the three axes, which is the shape the ground is divided
        // into.
        float3 target = fromCamera + input.normal;
        float reach = max(abs(target.x), max(abs(target.y), abs(target.z)));
        morph = clamp((reach * ramp.x - ramp.y) * ramp.z, 0.0, 1.0);
        localPosition += input.normal * morph;

        // The weight this pass carries at this vertex, as sub_512C30 computes it from the same fade:
        // the colour's alpha selects one -- nothing, the fade, its complement, or one -- for how this
        // vertex's material relates to the pass's (see TerrainChunkMesh), and the pass's texture scale
        // then takes the complement for the dense scale, which is the near one, and the fade for the
        // sparse scale, which the next coarser level continues. Terrain passes add, and at every
        // vertex these weights sum to one across every pass and both scales, so the near scale
        // cross-fades into the far scale and a vertex's material into its source's, with nothing
        // popping and nothing over-bright. The weight scales the albedo rather than the alpha: it is a
        // share of the ground, not a coverage, and the colour's own channels are white for terrain.
        float selector = input.colorBgra.a * 3.0;
        float material = selector < 0.5 ? 0.0 : (selector < 1.5 ? morph : (selector < 2.5 ? 1.0 - morph : 1.0));
        output.fade = material * (terrain > 1.5 ? 1.0 - morph : morph);
    }

    float4 clip = mul(push.modelViewProjection, float4(localPosition, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;

    // The matrix pushed here already carries the view-to-world rotation, so the normal lands in world
    // space rather than view space. Every pass that reads the G-buffer wants it there: a world normal
    // does not change when the camera turns, which is what makes it comparable against the previous
    // frame and against a ray that hit the same surface from elsewhere.
    //
    // Written as the three dot products the GLSL's mat3(row0, row1, row2) * v came to, rather than as
    // a matrix: a matrix constructor fills columns in GLSL and rows in Slang, and the difference is a
    // silent transpose.
    float3 rotate0 = push.normal0.xyz;
    float3 rotate1 = push.normal1.xyz;
    float3 rotate2 = push.normal2.xyz;

    // Most meshes keep their normal in the normal channel. The terrain cannot: that channel carries
    // the offset each vertex slides along as the ground morphs between levels of detail, and lighting
    // the ground with it is what made distant terrain read as a chequerboard. A mesh that has nowhere
    // to put its normal carries one here instead, and the fourth channel is how it says so -- all
    // zero means nothing was carried.
    // Blended along with the position, or a limb caught between two poses is lit as though it were at
    // the end of the second one. The terrain's channel is the morph offset rather than a normal and
    // is read raw above; it never animates, so nothing is blended out from under it.
    float3 posedNormal = input.normal + poseBlend * (input.previousNormal - input.normal);
    float3 localNormal = input.surfaceNormal.a > 0.5 ? ((input.surfaceNormal.rgb * 2.0) - 1.0) : posedNormal;

    // A terrain page is drawn unrotated, so its normal is already the world one and the rows that
    // would have rotated it are carrying the ramp instead.
    output.worldNormal = ramped
        ? localNormal
        : ((rotate0 * localNormal.x) + (rotate1 * localNormal.y) + (rotate2 * localNormal.z));

    // A terrain page is drawn unrotated and knows where it sits relative to the eye, so its own
    // camera-relative position is already in world axes; everything else is rotated into them.
    output.surfacePosition = ramped
        ? localPosition + push.normal1.xyz
        : ((rotate0 * localPosition.x) + (rotate1 * localPosition.y) + (rotate2 * localPosition.z));

    // The terrain's second texture coordinate is not a lightmap: it is how far the first one slides
    // as the ground morphs, so the texture travels with the vertex under it.
    output.diffuseUv = ramped ? input.diffuseUv + (input.lightmapUv * morph) : input.diffuseUv;
    output.lightmapUv = input.lightmapUv;

    // The terrain's alpha is the weight selector read above, not a coverage; it must not gate the
    // cut-out test or scale the output, so the ground goes through as plain white.
    output.color = ramped ? float4(1.0, 1.0, 1.0, 1.0) : input.colorBgra.bgra;

    return output;
}

/// A tangent frame recovered from the screen-space derivatives of the position and the texture
/// coordinate, rather than from tangents the mesh does not carry [Mikkelsen 2010].
///
/// This game's models were authored before tangent frames were something anyone stored, so deriving
/// one is the only way a tangent-space map can be applied to them at all. The frame is returned as
/// its three axes rather than as a matrix, because a matrix constructor fills columns in GLSL and
/// rows in Slang and the GLSL this replaces multiplied by columns.
void CotangentFrame(
    float3 normal,
    float3 position,
    float2 uv,
    out float3 tangentAxis,
    out float3 bitangentAxis)
{
    float3 dPositionX = ddx(position);
    float3 dPositionY = ddy(position);
    float2 dUvX = ddx(uv);
    float2 dUvY = ddy(uv);

    float3 crossY = cross(dPositionY, normal);
    float3 crossX = cross(normal, dPositionX);
    float3 tangent = (crossY * dUvX.x) + (crossX * dUvY.x);
    float3 bitangent = (crossY * dUvX.y) + (crossX * dUvY.y);

    // A degenerate UV layout -- a collapsed triangle, or geometry with no meaningful mapping -- gives
    // both axes zero length, and normalising that is a NaN which propagates through the whole
    // G-buffer. The reciprocal square root of the LARGER axis scales both, so the frame stays
    // orthogonal, and the max keeps it finite.
    float scale = rsqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 1e-8));
    tangentAxis = tangent * scale;
    bitangentAxis = bitangent * scale;
}

/// The fragment stage: albedo, world normal, material and baked light, linear.
[shader("fragment")]
GBufferTargets fragmentMain(Varyings input)
{
    GBufferTargets output;

    float4 diffuse = uDiffuse.Sample(uDiffuseSampler, input.diffuseUv);
    float alpha = diffuse.a * input.color.a;

    // A hard cut-out keeps or drops a WHOLE pixel, so a chain-link mesh or a wire thinner than a
    // pixel is present in one frame and gone the next depending on where the jitter fell -- and it
    // fades out entirely once it is narrow enough to miss most centres. The hashed test instead
    // recovers the edge's coverage from the alpha gradient and keeps the pixel with THAT probability,
    // dithered per pixel and per frame, which the temporal resolve averages into a smooth edge: a
    // wire covering a third of a pixel comes out a third as bright rather than absent two frames in
    // three. This is the reference's dithered opacity mask, and it only works where a temporal filter
    // runs, which is why it follows the same presets.
    float alphaGradient = fwidth(alpha);

    // GATED ON THE ALPHA ACTUALLY VARYING ACROSS THE PIXEL, and that gate is load-bearing rather than
    // an optimisation. Plenty of this game's textures carry a flat alpha somewhere below one without
    // being cut-outs at all; run those through the dither and a constant alpha of a half computes a
    // coverage of a half and throws away every other pixel of an ordinary wall. The OpenGL path is
    // spared this because it only alpha-tests materials that asked to be tested; this path tests
    // everything, so it has to tell an EDGE from a merely translucent surface. A cut-out edge is
    // exactly where the alpha changes steeply from texel to texel; a flat one has no gradient and
    // takes the hard test unchanged.
    if (push.normal2.w > 0.0 && alphaGradient > 0.002)
    {
        float coverage = clamp(((alpha - cAlphaReference) / alphaGradient) + 0.5, 0.0, 1.0);
        float hash = frac(sin(dot(input.position.xy, float2(12.9898, 78.233)) + push.normal2.w) * 43758.5453);
        if (hash > coverage)
        {
            discard;
        }
    }
    else if (alpha < cAlphaReference)
    {
        discard;
    }

    // Decode the display-encoded textures to linear before they enter the G-buffer, so all the
    // lighting downstream is linear.
    // The fade is one for everything but terrain, where it is this pass's share of the ground at this
    // pixel: the passes are added, so the share scales the albedo and never the alpha.
    float3 albedo = SampledLinear(diffuse.rgb) * DecodeSrgb(input.color.rgb) * input.fade;
    float3 baked = SampledLinear(uLightmap.Sample(uLightmapSampler, input.lightmapUv).rgb) * push.material.w;

    // The sequel's ground draws its bake as a THIRD pass, MODULATE2X over the two under it
    // (TerrainRender_SetBlendMode, igi2ed 0x666B90). On the original's forward path that lands in the
    // picture and is the end of it. A deferred path cannot take it there: the base colour attachment
    // holds an ALBEDO, and a bake multiplied into an albedo is lit a SECOND time by whatever lights
    // the frame.
    //
    // Mission 1 is where that showed. Its bake is a night one, so the ground's albedo came out at
    // about a hundredth of its own texture -- 0.01 against a tan nearer 0.35 -- and the dynamic model
    // then lit that with a sun the level paints (0.10, 0.15, 0.25). The hillsides went black,
    // textureless and blue, where the baked model draws them tan and grained. Every preset but
    // vanilla lights dynamically, which is exactly the set that showed it.
    //
    // The pipeline masks every other attachment for this pass, so the three below it are written only
    // to keep the shader well defined.
    if (push.normal0.w > 3.5)
    {
        output.baseColour = float4(0.0, 0.0, 0.0, 0.0);
        output.normal = float4(0.0, 0.0, 0.0, 0.0);
        output.material = float4(0.0, 0.0, 0.0, 0.0);
        output.baked = float4(SampledLinear(diffuse.rgb) * DecodeSrgb(input.color.rgb), 1.0);
        return output;
    }

    // The sequel's ground: the lightmap slot is its material's DETAIL map, sampled at the second
    // texture coordinate and multiplied into the colour -- stage 1's MODULATE(TEXTURE, CURRENT) at
    // TEXCOORDINDEX 1 (TerrainRender_SetTextureState, igi2ed 0x66B810), over the diffuse stage 0
    // already laid down. It is what puts the grain in a hillside: the diffuse alone repeats every few
    // tens of metres and reads as a wash. Its bake is not here at all -- it is per sample and already
    // folded into the vertex colour -- so the baked attachment goes through neutral, exactly as an
    // unbound lightmap did.
    if (push.normal0.w > 2.5)
    {
        albedo *= SampledLinear(uLightmap.Sample(uLightmapSampler, input.lightmapUv).rgb);
        baked = float3(push.material.w, push.material.w, push.material.w);
    }

    float3 normal = normalize(input.worldNormal);

    // A body: cloth and skin, which do not shine. See the end of this stage for what it is given.
    bool matte = push.normal0.w < -0.5;

    float roughness = push.material.x;
    float metallic = push.material.y;

    // Fully open until a detail map says otherwise, which is both the neutral value and what a
    // surface with no map has to read as.
    float occlusion = 1.0;

    // The derived detail. Skipped whole when the preset asks for no strength, which is every tier
    // below high.
    //
    // TERRAIN IS INCLUDED, and used to be excluded. The reason given was that its normal channel
    // carries the level-of-detail morph target rather than a normal -- which was true then and is not
    // now: the ground's real normal moved to surfaceNormal, and the vertex stage already hands this
    // one a world-space normal, a world-space surface position and a diffuse coordinate that slides
    // with the morph, which is everything CotangentFrame needs. The exclusion outlived its reason,
    // and it was the single largest hole in this feature: the ground is most of what an outdoor frame
    // shows, so a map applied to every building and no terrain reads as the maps doing nothing at all.
    //
    // Safe against the additive passes for a reason worth stating: a terrain pixel is drawn more than
    // once, but only the BASE pass writes the normal and material attachments -- the additive ones
    // have their colour write mask cleared on every attachment but the albedo (see GBufferPipeline).
    // So this runs once per pixel, not once per pass, and the detail cannot accumulate.
    if (frame.surface.x > 0.0)
    {
        float4 detail = uDetail.Sample(uDetailSampler, input.diffuseUv);

        // The stored x and y, with z rebuilt rather than read: a unit vector's third component is not
        // independent information, and the generator spends its two channels on the two that are.
        //
        // The strength is applied by BLENDING towards the mapped normal, not by scaling the stored
        // pair before z is rebuilt. That older form could not survive a strength above one: what is
        // stored is the xy of an already-unit-length normal, so x^2 + y^2 <= 1 by construction, and
        // scaling it by s made the pair longer than one for every texel tilted past asin(1/s). The
        // rebuilt z then hit its own floor and the world normal came out lying IN the tangent plane,
        // pointing along the wall rather than off it. At s = 3 that is every texel past 19.5 degrees
        // of tilt -- most of a gravel or brick map, which is why turning the strength up made surfaces
        // worse rather than more detailed.
        //
        // Blending cannot degenerate: the two ends are both unit vectors and everything between them
        // is a real direction. This is Godot's arrangement (scene_forward_clustered.glsl,
        // NORMAL_MAP_USED).
        float3 tangentNormal = float3((detail.xy * 2.0) - 1.0, 0.0);
        tangentNormal.z = sqrt(max(1.0 - dot(tangentNormal.xy, tangentNormal.xy), 0.0));

        float3 tangentAxis;
        float3 bitangentAxis;
        CotangentFrame(normal, input.surfacePosition, input.diffuseUv, tangentAxis, bitangentAxis);
        float3 mapped = (tangentAxis * tangentNormal.x)
            + (bitangentAxis * tangentNormal.y)
            + (normal * tangentNormal.z);
        normal = normalize(lerp(normal, mapped, frame.surface.x));

        // The map's roughness is a MULTIPLIER on whatever the material said, not a replacement for it:
        // materials.json knows that a given texture is glass and the derivation does not, so the
        // derivation is only allowed to say how much finer structure sits on top. Stored halved,
        // because a multiplier runs past one and a texture channel does not.
        //
        // And scaled by the same strength the normal is, which it was not: at High's quarter strength the
        // normal took a quarter of the map and the roughness took all of it, so a smooth texel on a 0.6
        // floor came out at 0.36 -- under both reflection ceilings, and handed a full-strength reflection
        // at the 1.7 intensity. The two halves of one map now move together.
        float roughnessScale = lerp(1.0, detail.z * 2.0, saturate(frame.surface.x));
        roughness = clamp(roughness * roughnessScale, 0.02, 1.0);

        // Alpha is OCCLUSION -- how much of its surroundings a texel can see, from the horizon search
        // the generator has always run and nothing has ever read. Metal moved to materials.json, where
        // it belongs: it is a property of a material and not of a texel, and no filter over a
        // photograph can tell painted grey from steel.
        occlusion = detail.w;
    }

    // Up-facing surfaces are smoothed towards the floor sheen so a floor takes a reflection while the
    // walls stay matte, which is the split the reference has -- or, above one, roughened, so a floor
    // seen at a grazing angle does not out-shine the walls around it. Here the normal is already in world
    // space and this world's up is Z, so how up-facing a surface is needs no transform. In the
    // G-buffer rather than in the shading because the roughness written here is what the screen-space
    // reflections decide by as well -- a floor glossy only to the light would be the one surface the
    // setting exists for and still not reflect.
    if (frame.surface.y != 1.0 && !matte)
    {
        float upFacing = clamp(normal.z, 0.0, 1.0);
        roughness = min(roughness * lerp(1.0, frame.surface.y, smoothstep(0.55, 0.85, upFacing)), 1.0);
    }

    // Geometric specular anti-aliasing, and it is the last thing done to the roughness because it is a
    // statement about the pixel rather than about the material: wherever the shading normal swings
    // hard across one pixel, that pixel does not have A normal, it has a distribution of them -- and a
    // narrow GGX lobe evaluated at the middle of that distribution is what turns a derived normal map
    // into crawling glitter under a moving light.
    //
    // The fix is to widen the lobe by exactly the variance the pixel actually holds, measured from the
    // screen-space derivatives of the normal and added in ALPHA space, where variances are additive.
    // [Kaplanyan et al., "Improved Geometric Specular Antialiasing", and this is Godot's arrangement
    // of it -- scene_forward_clustered.glsl, with its own defaults of 0.25 and 0.18.] The reference
    // engine's NormalCurvatureToRoughness does the same job from the VERTEX normal, which cannot see a
    // normal map at all; this runs on the shading normal, which is the one that is actually aliasing.
    //
    // Unconditional, not gated on whether a detail map was applied: a curved railing or a distant
    // fence post swings its normal across a pixel with no map involved, and those were glittering too.
    //
    // This is what pays for the lamp specular being at full strength in the lighting pass.
    {
        const float kSpecularAaAmount = 0.25;
        const float kSpecularAaLimit = 0.18;

        float3 dNormalX = ddx(normal);
        float3 dNormalY = ddy(normal);
        float variance = kSpecularAaAmount * (dot(dNormalX, dNormalX) + dot(dNormalY, dNormalY));
        float alpha2 = roughness * roughness;
        roughness = sqrt(min(1.0, alpha2 + min(2.0 * variance, kSpecularAaLimit)));
    }

    // The alpha carries the material's own occlusion, in the reference's layout -- the fine contact
    // shading that is a property of the TEXTURE rather than of where the surface stands, so it is the
    // same from every angle in every frame and costs nothing per pixel to have.
    //
    // It survives the attachment unencoded, and that is not luck: this target is R8G8B8A8Srgb, and a
    // Vulkan sRGB format applies its transfer function to the three colour channels and never to
    // alpha. So the albedo beside it is display-encoded and this is linear, which is what a visibility
    // term has to be -- putting occlusion through a gamma curve would darken every crease in the game
    // by about a stop.
    output.baseColour = float4(albedo, occlusion);
    output.normal = EncodeGBufferNormal(normal, 0.0);

    // The sky dome is light rather than a lit surface, and the reference says so with the shading
    // model rather than with a separate channel -- so the flag the material carries becomes an unlit
    // shading model here, and the lighting pass reads it from the same place it reads everything else.
    int shadingModel = push.material.z > 0.0 ? kShadingModelUnlit : kShadingModelDefaultLit;

    // Specular is the reference's default 0.5, which is what makes a non-metal's normal-incidence
    // reflectance the 0.04 every dielectric in this scene wants -- except on an unlit surface, which
    // reflects nothing and lends the slot to how brightly it emits instead.
    float specular = shadingModel == kShadingModelUnlit
        ? EncodeEmissiveStrength(push.material.z)
        : 0.5;

    // NEITHER GAME'S GROUND HAS A SPECULAR RESPONSE, because retail's has none to have.
    //
    // Project I.G.I. draws its terrain with the fixed function told outright not to: sub_512C30 sets
    // D3DRENDERSTATE_LIGHTING (137) to 0 and D3DRENDERSTATE_SPECULARENABLE (29) to 0 in the state
    // block it lays down before a single page goes out, so the ground is its baked vertex colour times
    // its texture and the hardware is forbidden from putting a highlight on it. I.G.I. 2's is three
    // texture passes with no lighting in them at all -- TerrainRender_SetGenericState (igi2ed
    // 0x66BAD0) and TerrainRender_SetTextureState (0x66B810) set up colour stages and a blend and
    // nothing else -- and the one directional light the sequel does apply is gated off terrain
    // explicitly in the forward path.
    //
    // Given a lobe it laid a broad sheen down every slope turned towards the sun, worst at distance
    // where one coarse tile is a whole hillside and the highlight is uniform across it, and it fed the
    // screen-space reflections on top. What the remaster adds to the ground and retail does not have
    // is the sun's and the lamps' DIFFUSE light, and that is untouched here: this takes the highlight
    // off the hillside, not the daylight.
    //
    // Standing water is the exception, and it puts itself back: the lighting pass restores a
    // dielectric's specular level wherever its puddle mask is, so rain still glints on ground the dry
    // frame keeps matte.
    if (push.normal0.w > 0.5)
    {
        specular = 0.0;
        roughness = 1.0;
    }

    // NOR DO THE BODIES, and for a body there is no water exception either.
    //
    // A soldier drawn as the default dielectric took every gloss term the modern path has: the derived
    // roughness map multiplied its 0.6 down towards a mirror, the floor-gloss term above smoothed every
    // helmet and shoulder for facing up, the specular anti-aliasing could only widen what was left, and
    // anything under the reflection pass's ceiling was handed a traced reflection. Uniforms read as
    // plastic. So a body is the ground's treatment -- no specular level, full roughness -- which takes
    // the sun's and the lamps' highlight and the ambient sheen off it, and the flag tells the two later
    // passes that would add shine back regardless (the puddle mask in the shading, the reflections) to
    // leave the pixel alone. The detail NORMAL still applies: that is the folds of the cloth under the
    // diffuse light, not a sheen.
    if (matte && shadingModel == kShadingModelDefaultLit)
    {
        specular = 0.0;
        roughness = 1.0;
        metallic = 0.0;
        shadingModel |= kShadingFlagMatte;
    }

    output.material = EncodeGBufferMaterial(metallic, specular, roughness, shadingModel);
    output.baked = float4(baked, 1.0);

    return output;
}
