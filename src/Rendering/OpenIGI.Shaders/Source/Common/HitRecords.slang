// The material record a traced hit is walked back to, and the two buffers it names by address.
//
// One record serves both the shadow pass's cut-out test and the reflection's hit shading, and each
// reaches into it by word offset. Landing on the same word does not fail loudly: a cut-out surface would
// test its alpha against a texture layer number, so a fence would come out solid or absent depending on
// which layer its colour happened to get. The offsets are named here, once, so the two cannot collide.
//
// The buffers are read by HANDLE rather than as bound buffers, which is what lets a hit reach a mesh the
// pass never bound. A handle is the buffer's IRhiBuffer.ShaderHandle, two words: on Vulkan its device
// address, read through a pointer; on Direct3D 12 the index of a raw view of it in the shader-visible
// descriptor heap, read through ResourceDescriptorHeap. Either way the buffers are plain word and float
// arrays, for the reason the shadow pass declared them so: the alignment that demands is four bytes, which
// every one of these buffers already satisfies, and no layout rule has to agree with a C# struct.
//
// Every lane of a wave can be walking a different mesh, so on Direct3D 12 the heap index is marked
// non-uniform. Unmarked, the hardware may read one lane's descriptor for the whole wave -- GPU-based
// validation reports it on every shadow dispatch -- and a hit lands in another mesh's vertices.

#ifndef OPENIGI_HIT_RECORDS_INCLUDED
#define OPENIGI_HIT_RECORDS_INCLUDED

// Vertex3D is 48 bytes: the position is float 0, and the diffuse UV is float 6.
static const uint kHitVertexFloats = 12u;
static const uint kHitUvFloat = 6u;

// Eight words per record.
static const uint kHitRecordWords = 8u;

// The vertex buffer's handle, low word then high.
static const uint kHitVertexAddressWord = 0u;

// The index buffer's handle, low word then high.
static const uint kHitIndexAddressWord = 2u;

// The geometry's first index, which the build was given as its primitive offset.
static const uint kHitFirstIndexWord = 4u;

// The cut-out mask's layer in the mask array, or negative where the material has none.
static const uint kHitMaskLayerWord = 5u;

// The albedo's layer in the traced-albedo array, or negative where the material has none.
static const uint kHitAlbedoLayerWord = 6u;

// Non-zero where the geometry moves, which no surface-cache card can describe.
static const uint kHitDynamicWord = 7u;

// That word's layout: bit 0 says the run moves; bits 8-15 hold how strongly it glows, in sixteenths of its
// albedo, nought for a surface that does not -- the strength the G-buffer gives the same run.
static const uint kHitDynamicBit = 1u;
static const uint kHitEmissiveShift = 8u;
static const float kHitEmissiveSteps = 16.0;

/// A buffer of 32-bit words reached by its handle.
struct HitWords
{
    uint2 handle;

    __subscript(uint index) -> uint
    {
        get
        {
            __target_switch
            {
            case spirv:
                return ConstBufferPointer<uint>.fromUInt((uint64_t(handle.y) << 32) | uint64_t(handle.x))[index];
            case hlsl:
                {
                    ByteAddressBuffer words = nonuniform(DescriptorHandle<ByteAddressBuffer>(handle));
                    return words.Load(index * 4u);
                }
            }
        }
    }
}

/// A buffer of 32-bit floats reached by its handle.
struct HitFloats
{
    uint2 handle;

    __subscript(uint index) -> float
    {
        get
        {
            __target_switch
            {
            case spirv:
                return ConstBufferPointer<float>.fromUInt((uint64_t(handle.y) << 32) | uint64_t(handle.x))[index];
            case hlsl:
                {
                    ByteAddressBuffer words = nonuniform(DescriptorHandle<ByteAddressBuffer>(handle));
                    return asfloat(words.Load(index * 4u));
                }
            }
        }
    }
}

HitWords HitWordsAt(uint low, uint high)
{
    HitWords words = { uint2(low, high) };
    return words;
}

HitFloats HitFloatsAt(uint low, uint high)
{
    HitFloats floats = { uint2(low, high) };
    return floats;
}

// One 16-bit index, out of a buffer read as 32-bit words so that no 16-bit storage feature is needed.
// The buffer's own base is word aligned; only the element is not.
uint HitIndexAt(HitWords indices, uint at)
{
    uint word = indices[at >> 1u];
    return ((at & 1u) == 0u) ? (word & 0xffffu) : (word >> 16u);
}

#endif
