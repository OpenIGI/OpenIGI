// The material record a traced hit is walked back to, and the two buffers it names by address.
//
// One record serves both the shadow pass's cut-out test and the reflection's hit shading, and each
// reaches into it by word offset. Landing on the same word does not fail loudly: a cut-out surface would
// test its alpha against a texture layer number, so a fence would come out solid or absent depending on
// which layer its colour happened to get. The offsets are named here, once, so the two cannot collide.
//
// The buffers are read by ADDRESS rather than as bound buffers, which is what lets a hit reach a mesh
// the pass never bound. Pointed at plain word and float arrays for the reason the shadow pass declared
// them so: the alignment that demands is four bytes, which every one of these buffers already satisfies,
// and no layout rule has to agree with a C# struct.

#ifndef OPENIGI_HIT_RECORDS_INCLUDED
#define OPENIGI_HIT_RECORDS_INCLUDED

// Vertex3D is 48 bytes: the position is float 0, and the diffuse UV is float 6.
static const uint kHitVertexFloats = 12u;
static const uint kHitUvFloat = 6u;

// Eight words per record.
static const uint kHitRecordWords = 8u;

// The vertex buffer's address, low word then high.
static const uint kHitVertexAddressWord = 0u;

// The index buffer's address, low word then high.
static const uint kHitIndexAddressWord = 2u;

// The geometry's first index, which the build was given as its primitive offset.
static const uint kHitFirstIndexWord = 4u;

// The cut-out mask's layer in the mask array, or negative where the material has none.
static const uint kHitMaskLayerWord = 5u;

// The albedo's layer in the traced-albedo array, or negative where the material has none.
static const uint kHitAlbedoLayerWord = 6u;

// Non-zero where the geometry moves, which no surface-cache card can describe.
static const uint kHitDynamicWord = 7u;

ConstBufferPointer<uint> HitWordsAt(uint low, uint high)
{
    return ConstBufferPointer<uint>.fromUInt((uint64_t(high) << 32) | uint64_t(low));
}

ConstBufferPointer<float> HitFloatsAt(uint low, uint high)
{
    return ConstBufferPointer<float>.fromUInt((uint64_t(high) << 32) | uint64_t(low));
}

// One 16-bit index, out of a buffer read as 32-bit words so that no 16-bit storage feature is needed.
// The buffer's own base is word aligned; only the element is not.
uint HitIndexAt(ConstBufferPointer<uint> indices, uint at)
{
    uint word = indices[at >> 1u];
    return ((at & 1u) == 0u) ? (word & 0xffffu) : (word >> 16u);
}

#endif
