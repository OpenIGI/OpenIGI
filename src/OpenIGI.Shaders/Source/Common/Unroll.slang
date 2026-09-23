// A fixed-count loop the WGSL path has the shader compiler unroll itself.
//
// SPIR-V hands a small loop to the driver, which unrolls it, and its counters and the arrays they index
// become registers. WGSL does not get that far: the WebGPU implementation bounds every loop at run time,
// so the driver sees one it cannot count, keeps it, and every array a counter indexes -- a 3x3 kernel's
// weights, the nine values a median sorts -- goes to local memory, a load and a store each access. The
// temporal resolve's rejection pass ran five times slower on WebGPU than on Vulkan for exactly that.
// Unrolled before the WGSL is written, the counters are constants and none of that happens.
//
// SPIR-V keeps its loops as they were -- it is what the legacy renderer runs, and its picture has to stay
// what it was to the bit.
//
// Put OPENIGI_UNROLL in front of a loop whose trip count the compiler can work out.

#ifndef OPENIGI_UNROLL_INCLUDED
#define OPENIGI_UNROLL_INCLUDED

#ifdef OPENIGI_TARGET_WGSL
#define OPENIGI_UNROLL [ForceUnroll]
#else
#define OPENIGI_UNROLL
#endif

#endif
