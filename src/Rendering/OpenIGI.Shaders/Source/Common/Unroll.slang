// A fixed-count loop the WGSL and GLSL paths have the shader compiler unroll itself.
//
// SPIR-V hands a small loop to the driver, which unrolls it, and its counters and the arrays they index
// become registers. WGSL does not get that far: the WebGPU implementation bounds every loop at run time,
// so the driver sees one it cannot count, keeps it, and every array a counter indexes -- a 3x3 kernel's
// weights, the nine values a median sorts -- goes to local memory, a load and a store each access. The
// temporal resolve's rejection pass ran five times slower on WebGPU than on Vulkan for exactly that.
// Unrolled before the WGSL is written, the counters are constants and none of that happens.
//
// GLSL meets the same end by another road: SPIRV-Cross writes such a loop as for (;;) with a break, its
// counter declared outside it, and NVIDIA's OpenGL compiler does not count a loop of that shape, so it keeps
// it and its arrays go to local memory too -- the same rejection pass ran half again as long as on Vulkan.
//
// SPIR-V keeps its loops as they were: its driver unrolls them itself, as above.
//
// Put OPENIGI_UNROLL in front of a loop whose trip count the compiler can work out.

#ifndef OPENIGI_UNROLL_INCLUDED
#define OPENIGI_UNROLL_INCLUDED

#if defined(OPENIGI_TARGET_WGSL) || defined(OPENIGI_TARGET_GLSL)
#define OPENIGI_UNROLL [ForceUnroll]
#else
#define OPENIGI_UNROLL
#endif

#endif
