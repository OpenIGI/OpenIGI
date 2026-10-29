// webgpu.h, implemented in a browser over the JavaScript WebGPU API.
//
// The WebGPU backend calls wgpu-native's C API through P/Invoke into a library named "wgpu_native". In a
// browser build this file IS that library: it is linked into the .NET runtime's wasm (a
// NativeFileReference named wgpu_native), so the same generated bindings, the same backend and the same
// renderer run unchanged, and every call lands here.
//
// This half does as little as it can. Each function forwards to the bridge (wgpu-bridge.js) with the
// struct POINTERS it was given; the bridge reads the structs out of wasm memory itself, by the offsets
// this file hands it once at start-up -- computed here by the compiler, from the real header, so the
// layouts can never drift from the ones the C# binding writes. What C cannot hand across as it stands --
// a 64-bit integer, a callback -- is converted or kept here.
//
// Callbacks are kept here and fired from wgpuInstanceProcessEvents, as the backend asks for them
// (AllowProcessEvents): the bridge queues what the browser has finished, and this pops the queue.

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <emscripten.h>

#include "../OpenIGI.Rendering.WebGpu.Native/native/webgpu.h"
#include "../OpenIGI.Rendering.WebGpu.Native/native/wgpu.h"

// ---------------------------------------------------------------------------------------------------
// The bridge. Every call goes to globalThis.OpenIgiWebGpu, which main.js installs before the runtime
// starts; the handles are the bridge's own table indices, so a pointer-sized handle is just a number.
// Spelled out in each body: an EM_JS body is stringified before any macro inside it could expand.

EM_JS(void, oiw_attach, (void), {
  globalThis.OpenIgiWebGpu.attach({ heap : () => HEAPU8 });
});

EM_JS(void, oiw_field, (const char* name, int offset), { globalThis.OpenIgiWebGpu.field(name, offset); });

EM_JS(uint32_t, oiw_call_now, (const char* name, uint32_t a, uint32_t b, uint32_t c, uint32_t d, uint32_t e,
                           uint32_t f),
      { return globalThis.OpenIgiWebGpu.call(name, a, b, c, d, e, f) >>> 0; });

EM_JS(double, oiw_call_d_now, (const char* name, uint32_t a, double b, double c, double d, double e),
      { return globalThis.OpenIgiWebGpu.callD(name, a, b, c, d, e); });

EM_JS(int, oiw_pop, (uint32_t* out), { return globalThis.OpenIgiWebGpu.pop(out); });

EM_JS(void, oiw_replay, (const uint32_t* words, uint32_t count), { globalThis.OpenIgiWebGpu.replay(words, count); });

// ---------------------------------------------------------------------------------------------------
// The render pass's hot commands, queued. A frame records thousands of binds and draws, and each one crossing
// into JavaScript on its own cost more than the browser's own work for it. They are written here as words and
// replayed in one crossing; every other call into the bridge sends the queue first, so the browser sees exactly
// the order the backend recorded.

enum {
  Q_PIPELINE = 1,  // pass, pipeline
  Q_BIND = 2,      // pass, index, group, count, offsets...
  Q_VERTEX = 3,    // pass, slot, buffer, offset lo, hi, size lo, hi (all ones: the rest)
  Q_INDEX = 4,     // pass, buffer, format, offset lo, hi, size lo, hi
  Q_DRAW = 5,      // pass, vertices, instances, first vertex, first instance
  Q_INDEXED = 6,   // pass, indices, instances, first index, base vertex, first instance
  Q_SCISSOR = 7,   // pass, x, y, width, height
};

#define QUEUE_WORDS 16384
static uint32_t queue_words[QUEUE_WORDS];
static uint32_t queued;

static void send_queue(void) {
  if (queued != 0) {
    oiw_replay(queue_words, queued);
    queued = 0;
  }
}

static uint32_t* reserve(uint32_t words) {
  if (queued + words > QUEUE_WORDS) send_queue();
  uint32_t* at = queue_words + queued;
  queued += words;
  return at;
}

static uint32_t oiw_call(const char* name, uint32_t a, uint32_t b, uint32_t c, uint32_t d, uint32_t e, uint32_t f) {
  send_queue();
  return oiw_call_now(name, a, b, c, d, e, f);
}

static double oiw_call_d(const char* name, uint32_t a, double b, double c, double d, double e) {
  send_queue();
  return oiw_call_d_now(name, a, b, c, d, e);
}

// The fields the bridge reads. One line each: the struct, the field.
#define F(type, field) oiw_field(#type "." #field, (int)offsetof(type, field))
#define S(type) oiw_field(#type ".$size", (int)sizeof(type))

static void describe_layouts(void) {
  F(WGPUStringView, data); F(WGPUStringView, length); S(WGPUStringView);
  F(WGPUChainedStruct, next); F(WGPUChainedStruct, sType);

  F(WGPUAdapterInfo, vendor); F(WGPUAdapterInfo, architecture); F(WGPUAdapterInfo, device);
  F(WGPUAdapterInfo, description); F(WGPUAdapterInfo, backendType); F(WGPUAdapterInfo, adapterType);
  F(WGPUAdapterInfo, vendorID); F(WGPUAdapterInfo, deviceID);

  F(WGPULimits, maxTextureDimension1D); F(WGPULimits, maxTextureDimension2D); F(WGPULimits, maxTextureDimension3D);
  F(WGPULimits, maxTextureArrayLayers); F(WGPULimits, maxBindGroups); F(WGPULimits, maxBindGroupsPlusVertexBuffers);
  F(WGPULimits, maxBindingsPerBindGroup); F(WGPULimits, maxDynamicUniformBuffersPerPipelineLayout);
  F(WGPULimits, maxDynamicStorageBuffersPerPipelineLayout); F(WGPULimits, maxSampledTexturesPerShaderStage);
  F(WGPULimits, maxSamplersPerShaderStage); F(WGPULimits, maxStorageBuffersPerShaderStage);
  F(WGPULimits, maxStorageTexturesPerShaderStage); F(WGPULimits, maxUniformBuffersPerShaderStage);
  F(WGPULimits, maxUniformBufferBindingSize); F(WGPULimits, maxStorageBufferBindingSize);
  F(WGPULimits, minUniformBufferOffsetAlignment); F(WGPULimits, minStorageBufferOffsetAlignment);
  F(WGPULimits, maxVertexBuffers); F(WGPULimits, maxBufferSize); F(WGPULimits, maxVertexAttributes);
  F(WGPULimits, maxVertexBufferArrayStride); F(WGPULimits, maxInterStageShaderVariables);
  F(WGPULimits, maxColorAttachments); F(WGPULimits, maxColorAttachmentBytesPerSample);
  F(WGPULimits, maxComputeWorkgroupStorageSize); F(WGPULimits, maxComputeInvocationsPerWorkgroup);
  F(WGPULimits, maxComputeWorkgroupSizeX); F(WGPULimits, maxComputeWorkgroupSizeY);
  F(WGPULimits, maxComputeWorkgroupSizeZ); F(WGPULimits, maxComputeWorkgroupsPerDimension);
  F(WGPULimits, maxImmediateSize);

  F(WGPUSupportedFeatures, featureCount); F(WGPUSupportedFeatures, features);

  F(WGPUBufferDescriptor, label); F(WGPUBufferDescriptor, usage); F(WGPUBufferDescriptor, size);
  F(WGPUBufferDescriptor, mappedAtCreation);

  F(WGPUExtent3D, width); F(WGPUExtent3D, height); F(WGPUExtent3D, depthOrArrayLayers);
  F(WGPUOrigin3D, x); F(WGPUOrigin3D, y); F(WGPUOrigin3D, z);

  F(WGPUTextureDescriptor, label); F(WGPUTextureDescriptor, usage); F(WGPUTextureDescriptor, dimension);
  F(WGPUTextureDescriptor, size); F(WGPUTextureDescriptor, format); F(WGPUTextureDescriptor, mipLevelCount);
  F(WGPUTextureDescriptor, sampleCount); F(WGPUTextureDescriptor, viewFormatCount);
  F(WGPUTextureDescriptor, viewFormats);

  F(WGPUTextureViewDescriptor, label); F(WGPUTextureViewDescriptor, format); F(WGPUTextureViewDescriptor, dimension);
  F(WGPUTextureViewDescriptor, baseMipLevel); F(WGPUTextureViewDescriptor, mipLevelCount);
  F(WGPUTextureViewDescriptor, baseArrayLayer); F(WGPUTextureViewDescriptor, arrayLayerCount);
  F(WGPUTextureViewDescriptor, aspect); F(WGPUTextureViewDescriptor, usage);

  F(WGPUSamplerDescriptor, label); F(WGPUSamplerDescriptor, addressModeU); F(WGPUSamplerDescriptor, addressModeV);
  F(WGPUSamplerDescriptor, addressModeW); F(WGPUSamplerDescriptor, magFilter); F(WGPUSamplerDescriptor, minFilter);
  F(WGPUSamplerDescriptor, mipmapFilter); F(WGPUSamplerDescriptor, lodMinClamp); F(WGPUSamplerDescriptor, lodMaxClamp);
  F(WGPUSamplerDescriptor, compare); F(WGPUSamplerDescriptor, maxAnisotropy);

  F(WGPUShaderModuleDescriptor, nextInChain); F(WGPUShaderModuleDescriptor, label);
  F(WGPUShaderSourceWGSL, code);

  F(WGPUBindGroupLayoutDescriptor, label); F(WGPUBindGroupLayoutDescriptor, entryCount);
  F(WGPUBindGroupLayoutDescriptor, entries);
  F(WGPUBindGroupLayoutEntry, binding); F(WGPUBindGroupLayoutEntry, visibility); F(WGPUBindGroupLayoutEntry, buffer);
  F(WGPUBindGroupLayoutEntry, sampler); F(WGPUBindGroupLayoutEntry, texture); F(WGPUBindGroupLayoutEntry, storageTexture);
  S(WGPUBindGroupLayoutEntry);
  F(WGPUBufferBindingLayout, type); F(WGPUBufferBindingLayout, hasDynamicOffset); F(WGPUBufferBindingLayout, minBindingSize);
  F(WGPUSamplerBindingLayout, type);
  F(WGPUTextureBindingLayout, sampleType); F(WGPUTextureBindingLayout, viewDimension); F(WGPUTextureBindingLayout, multisampled);
  F(WGPUStorageTextureBindingLayout, access); F(WGPUStorageTextureBindingLayout, format);
  F(WGPUStorageTextureBindingLayout, viewDimension);

  F(WGPUBindGroupDescriptor, label); F(WGPUBindGroupDescriptor, layout); F(WGPUBindGroupDescriptor, entryCount);
  F(WGPUBindGroupDescriptor, entries);
  F(WGPUBindGroupEntry, binding); F(WGPUBindGroupEntry, buffer); F(WGPUBindGroupEntry, offset); F(WGPUBindGroupEntry, size);
  F(WGPUBindGroupEntry, sampler); F(WGPUBindGroupEntry, textureView); S(WGPUBindGroupEntry);

  F(WGPUPipelineLayoutDescriptor, label); F(WGPUPipelineLayoutDescriptor, bindGroupLayoutCount);
  F(WGPUPipelineLayoutDescriptor, bindGroupLayouts);

  F(WGPUConstantEntry, key); F(WGPUConstantEntry, value); S(WGPUConstantEntry);
  F(WGPUComputePipelineDescriptor, label); F(WGPUComputePipelineDescriptor, layout); F(WGPUComputePipelineDescriptor, compute);
  F(WGPUComputeState, module); F(WGPUComputeState, entryPoint); F(WGPUComputeState, constantCount); F(WGPUComputeState, constants);

  F(WGPURenderPipelineDescriptor, label); F(WGPURenderPipelineDescriptor, layout); F(WGPURenderPipelineDescriptor, vertex);
  F(WGPURenderPipelineDescriptor, primitive); F(WGPURenderPipelineDescriptor, depthStencil);
  F(WGPURenderPipelineDescriptor, multisample); F(WGPURenderPipelineDescriptor, fragment);
  F(WGPUVertexState, module); F(WGPUVertexState, entryPoint); F(WGPUVertexState, constantCount); F(WGPUVertexState, constants);
  F(WGPUVertexState, bufferCount); F(WGPUVertexState, buffers);
  F(WGPUVertexBufferLayout, stepMode); F(WGPUVertexBufferLayout, arrayStride); F(WGPUVertexBufferLayout, attributeCount);
  F(WGPUVertexBufferLayout, attributes); S(WGPUVertexBufferLayout);
  F(WGPUVertexAttribute, format); F(WGPUVertexAttribute, offset); F(WGPUVertexAttribute, shaderLocation); S(WGPUVertexAttribute);
  F(WGPUPrimitiveState, topology); F(WGPUPrimitiveState, stripIndexFormat); F(WGPUPrimitiveState, frontFace);
  F(WGPUPrimitiveState, cullMode); F(WGPUPrimitiveState, unclippedDepth);
  F(WGPUDepthStencilState, format); F(WGPUDepthStencilState, depthWriteEnabled); F(WGPUDepthStencilState, depthCompare);
  F(WGPUDepthStencilState, stencilFront); F(WGPUDepthStencilState, stencilBack); F(WGPUDepthStencilState, stencilReadMask);
  F(WGPUDepthStencilState, stencilWriteMask); F(WGPUDepthStencilState, depthBias);
  F(WGPUDepthStencilState, depthBiasSlopeScale); F(WGPUDepthStencilState, depthBiasClamp);
  F(WGPUStencilFaceState, compare); F(WGPUStencilFaceState, failOp); F(WGPUStencilFaceState, depthFailOp);
  F(WGPUStencilFaceState, passOp);
  F(WGPUMultisampleState, count); F(WGPUMultisampleState, mask); F(WGPUMultisampleState, alphaToCoverageEnabled);
  F(WGPUFragmentState, module); F(WGPUFragmentState, entryPoint); F(WGPUFragmentState, constantCount);
  F(WGPUFragmentState, constants); F(WGPUFragmentState, targetCount); F(WGPUFragmentState, targets);
  F(WGPUColorTargetState, format); F(WGPUColorTargetState, blend); F(WGPUColorTargetState, writeMask); S(WGPUColorTargetState);
  F(WGPUBlendState, color); F(WGPUBlendState, alpha);
  F(WGPUBlendComponent, operation); F(WGPUBlendComponent, srcFactor); F(WGPUBlendComponent, dstFactor);

  F(WGPUQuerySetDescriptor, label); F(WGPUQuerySetDescriptor, type); F(WGPUQuerySetDescriptor, count);
  F(WGPUCommandEncoderDescriptor, label); F(WGPUCommandBufferDescriptor, label);

  F(WGPURenderPassDescriptor, label); F(WGPURenderPassDescriptor, colorAttachmentCount);
  F(WGPURenderPassDescriptor, colorAttachments); F(WGPURenderPassDescriptor, depthStencilAttachment);
  F(WGPURenderPassDescriptor, occlusionQuerySet); F(WGPURenderPassDescriptor, timestampWrites);
  F(WGPURenderPassColorAttachment, view); F(WGPURenderPassColorAttachment, depthSlice);
  F(WGPURenderPassColorAttachment, resolveTarget); F(WGPURenderPassColorAttachment, loadOp);
  F(WGPURenderPassColorAttachment, storeOp); F(WGPURenderPassColorAttachment, clearValue); S(WGPURenderPassColorAttachment);
  F(WGPURenderPassDepthStencilAttachment, view); F(WGPURenderPassDepthStencilAttachment, depthLoadOp);
  F(WGPURenderPassDepthStencilAttachment, depthStoreOp); F(WGPURenderPassDepthStencilAttachment, depthClearValue);
  F(WGPURenderPassDepthStencilAttachment, depthReadOnly); F(WGPURenderPassDepthStencilAttachment, stencilLoadOp);
  F(WGPURenderPassDepthStencilAttachment, stencilStoreOp); F(WGPURenderPassDepthStencilAttachment, stencilClearValue);
  F(WGPURenderPassDepthStencilAttachment, stencilReadOnly);
  F(WGPUPassTimestampWrites, querySet); F(WGPUPassTimestampWrites, beginningOfPassWriteIndex);
  F(WGPUPassTimestampWrites, endOfPassWriteIndex);
  F(WGPUComputePassDescriptor, label); F(WGPUComputePassDescriptor, timestampWrites);
  F(WGPUColor, r); F(WGPUColor, g); F(WGPUColor, b); F(WGPUColor, a);

  F(WGPUTexelCopyBufferLayout, offset); F(WGPUTexelCopyBufferLayout, bytesPerRow); F(WGPUTexelCopyBufferLayout, rowsPerImage);
  F(WGPUTexelCopyBufferInfo, layout); F(WGPUTexelCopyBufferInfo, buffer);
  F(WGPUTexelCopyTextureInfo, texture); F(WGPUTexelCopyTextureInfo, mipLevel); F(WGPUTexelCopyTextureInfo, origin);
  F(WGPUTexelCopyTextureInfo, aspect);

  F(WGPUSurfaceConfiguration, device); F(WGPUSurfaceConfiguration, format); F(WGPUSurfaceConfiguration, usage);
  F(WGPUSurfaceConfiguration, width); F(WGPUSurfaceConfiguration, height); F(WGPUSurfaceConfiguration, viewFormatCount);
  F(WGPUSurfaceConfiguration, viewFormats); F(WGPUSurfaceConfiguration, alphaMode);
  F(WGPUSurfaceCapabilities, usages); F(WGPUSurfaceCapabilities, formatCount); F(WGPUSurfaceCapabilities, formats);
  F(WGPUSurfaceCapabilities, presentModeCount); F(WGPUSurfaceCapabilities, presentModes);
  F(WGPUSurfaceCapabilities, alphaModeCount); F(WGPUSurfaceCapabilities, alphaModes);
  F(WGPUSurfaceTexture, texture); F(WGPUSurfaceTexture, status);

  F(WGPUDeviceDescriptor, label);
}

static int attached = 0;

static void attach(void) {
  if (!attached) {
    attached = 1;
    oiw_attach();
    describe_layouts();
  }
}

// A handle is the bridge's table index; nought is the null handle.
#define H(x) ((uint32_t)(uintptr_t)(x))
#define CALL(name, ...) oiw_call(name, __VA_ARGS__)
#define WHOLE(size) ((size) == WGPU_WHOLE_SIZE ? -1.0 : (double)(size))

// ---------------------------------------------------------------------------------------------------
// Callbacks, kept here until the bridge reports the work they wait for is done.

typedef enum { OP_ADAPTER = 1, OP_DEVICE = 2, OP_MAP = 3, OP_WORK_DONE = 4 } op_kind;

typedef struct {
  op_kind kind;
  void* callback;
  void* userdata1;
  void* userdata2;
} pending_op;

#define MAX_OPS 1024
static pending_op ops[MAX_OPS];
static uint64_t next_future = 1;

static uint32_t keep(op_kind kind, void* callback, void* userdata1, void* userdata2) {
  for (uint32_t i = 1; i < MAX_OPS; i++) {
    if (ops[i].kind == 0) {
      ops[i] = (pending_op){kind, callback, userdata1, userdata2};
      return i;
    }
  }
  abort();
}

static WGPUFuture future(void) { return (WGPUFuture){next_future++}; }

static const WGPUStringView EMPTY = {NULL, 0};

void wgpuInstanceProcessEvents(WGPUInstance instance) {
  (void)instance;
  uint32_t out[3];
  send_queue();
  while (oiw_pop(out)) {
    pending_op op = ops[out[0]];
    ops[out[0]].kind = 0;
    switch (op.kind) {
      case OP_ADAPTER:
        ((WGPURequestAdapterCallback)op.callback)((WGPURequestAdapterStatus)out[1], (WGPUAdapter)(uintptr_t)out[2],
                                                  EMPTY, op.userdata1, op.userdata2);
        break;
      case OP_DEVICE:
        ((WGPURequestDeviceCallback)op.callback)((WGPURequestDeviceStatus)out[1], (WGPUDevice)(uintptr_t)out[2],
                                                 EMPTY, op.userdata1, op.userdata2);
        break;
      case OP_MAP:
        ((WGPUBufferMapCallback)op.callback)((WGPUMapAsyncStatus)out[1], EMPTY, op.userdata1, op.userdata2);
        break;
      case OP_WORK_DONE:
        ((WGPUQueueWorkDoneCallback)op.callback)((WGPUQueueWorkDoneStatus)out[1], EMPTY, op.userdata1, op.userdata2);
        break;
    }
  }
}

// ---------------------------------------------------------------------------------------------------
// The instance, the adapter and the device. The page acquires the adapter and the device before the
// runtime starts -- a browser cannot wait on a promise from inside a call -- so these hand back what
// it already holds, through the callbacks, on the next ProcessEvents.

uint32_t wgpuGetVersion(void) { return 0x1D000101u; }

WGPUInstance wgpuCreateInstance(WGPUInstanceDescriptor const* descriptor) {
  (void)descriptor;
  attach();
  return (WGPUInstance)(uintptr_t)CALL("createInstance", 0, 0, 0, 0, 0, 0);
}

WGPUFuture wgpuInstanceRequestAdapter(WGPUInstance instance, WGPURequestAdapterOptions const* options,
                                      WGPURequestAdapterCallbackInfo info) {
  (void)options;
  CALL("requestAdapter", H(instance), keep(OP_ADAPTER, (void*)info.callback, info.userdata1, info.userdata2), 0, 0, 0, 0);
  return future();
}

WGPUFuture wgpuAdapterRequestDevice(WGPUAdapter adapter, WGPUDeviceDescriptor const* descriptor,
                                    WGPURequestDeviceCallbackInfo info) {
  (void)descriptor;
  CALL("requestDevice", H(adapter), keep(OP_DEVICE, (void*)info.callback, info.userdata1, info.userdata2), 0, 0, 0, 0);
  return future();
}

void wgpuAdapterGetFeatures(WGPUAdapter adapter, WGPUSupportedFeatures* features) {
  // The bridge fills a list it allocates here: the count first, then the values.
  uint32_t count = CALL("featureCount", H(adapter), 0, 0, 0, 0, 0);
  WGPUFeatureName* list = (WGPUFeatureName*)calloc(count ? count : 1, sizeof(WGPUFeatureName));
  CALL("features", H(adapter), H(list), count, 0, 0, 0);
  features->featureCount = count;
  features->features = list;
}

void wgpuSupportedFeaturesFreeMembers(WGPUSupportedFeatures features) { free((void*)features.features); }

WGPUBool wgpuAdapterHasFeature(WGPUAdapter adapter, WGPUFeatureName feature) {
  return CALL("hasFeature", H(adapter), (uint32_t)feature, 0, 0, 0, 0);
}

// With its length given rather than WGPU_STRLEN: the backend reads the length as it stands.
static WGPUStringView copy_string(const char* which, uint32_t object) {
  uint32_t length = CALL("stringLength", object, H(which), 0, 0, 0, 0);
  char* text = (char*)malloc(length + 1);
  CALL("stringCopy", object, H(which), H(text), length, 0, 0);
  text[length] = 0;
  return (WGPUStringView){text, length};
}

WGPUStatus wgpuAdapterGetInfo(WGPUAdapter adapter, WGPUAdapterInfo* info) {
  memset(info, 0, sizeof(*info));
  info->vendor = copy_string("vendor", H(adapter));
  info->architecture = copy_string("architecture", H(adapter));
  info->device = copy_string("device", H(adapter));
  info->description = copy_string("description", H(adapter));
  info->backendType = WGPUBackendType_WebGPU;
  info->adapterType = WGPUAdapterType_Unknown;
  return WGPUStatus_Success;
}

void wgpuAdapterInfoFreeMembers(WGPUAdapterInfo info) {
  free((void*)info.vendor.data);
  free((void*)info.architecture.data);
  free((void*)info.device.data);
  free((void*)info.description.data);
}

WGPUStatus wgpuAdapterGetLimits(WGPUAdapter adapter, WGPULimits* limits) {
  CALL("limits", H(adapter), H(limits), 0, 0, 0, 0);
  return WGPUStatus_Success;
}

WGPUStatus wgpuDeviceGetLimits(WGPUDevice device, WGPULimits* limits) {
  CALL("limits", H(device), H(limits), 0, 0, 0, 0);
  return WGPUStatus_Success;
}

WGPUQueue wgpuDeviceGetQueue(WGPUDevice device) { return (WGPUQueue)(uintptr_t)CALL("getQueue", H(device), 0, 0, 0, 0, 0); }

// Nothing to wait for from here: the queue orders the work, and a browser cannot block on the GPU.
WGPUBool wgpuDevicePoll(WGPUDevice device, WGPUBool wait, WGPUSubmissionIndex const* index) {
  (void)device; (void)wait; (void)index;
  return 1;
}

// ---------------------------------------------------------------------------------------------------
// Creation: every descriptor goes over as its pointer.

#define CREATE(fn, T, name) T fn(WGPUDevice device, T##Descriptor const* d) { \
  return (T)(uintptr_t)CALL(name, H(device), H(d), 0, 0, 0, 0); }

CREATE(wgpuDeviceCreateBuffer, WGPUBuffer, "createBuffer")
CREATE(wgpuDeviceCreateTexture, WGPUTexture, "createTexture")
CREATE(wgpuDeviceCreateSampler, WGPUSampler, "createSampler")
CREATE(wgpuDeviceCreateShaderModule, WGPUShaderModule, "createShaderModule")
CREATE(wgpuDeviceCreateBindGroupLayout, WGPUBindGroupLayout, "createBindGroupLayout")
CREATE(wgpuDeviceCreateBindGroup, WGPUBindGroup, "createBindGroup")
CREATE(wgpuDeviceCreatePipelineLayout, WGPUPipelineLayout, "createPipelineLayout")
CREATE(wgpuDeviceCreateComputePipeline, WGPUComputePipeline, "createComputePipeline")
CREATE(wgpuDeviceCreateRenderPipeline, WGPURenderPipeline, "createRenderPipeline")
CREATE(wgpuDeviceCreateQuerySet, WGPUQuerySet, "createQuerySet")
CREATE(wgpuDeviceCreateCommandEncoder, WGPUCommandEncoder, "createCommandEncoder")

/* A browser has no trusted modules: its WebGPU keeps every runtime check, as the specification requires, so
   the module is made the ordinary way and the request is dropped. */
WGPUShaderModule wgpuDeviceCreateShaderModuleTrusted(
    WGPUDevice device, WGPUShaderModuleDescriptor const* d, WGPUShaderRuntimeChecks runtimeChecks) {
  (void)runtimeChecks;
  return wgpuDeviceCreateShaderModule(device, d);
}

WGPUTextureView wgpuTextureCreateView(WGPUTexture texture, WGPUTextureViewDescriptor const* d) {
  return (WGPUTextureView)(uintptr_t)CALL("createView", H(texture), H(d), 0, 0, 0, 0);
}

// ---------------------------------------------------------------------------------------------------
// Release: the bridge drops its reference, and destroys a buffer, a texture or a query set outright.

#define RELEASE(fn, T) void fn(T x) { CALL("release", H(x), 0, 0, 0, 0, 0); }

RELEASE(wgpuInstanceRelease, WGPUInstance)
RELEASE(wgpuAdapterRelease, WGPUAdapter)
RELEASE(wgpuDeviceRelease, WGPUDevice)
RELEASE(wgpuQueueRelease, WGPUQueue)
RELEASE(wgpuBufferRelease, WGPUBuffer)
RELEASE(wgpuTextureRelease, WGPUTexture)
RELEASE(wgpuTextureViewRelease, WGPUTextureView)
RELEASE(wgpuSamplerRelease, WGPUSampler)
RELEASE(wgpuShaderModuleRelease, WGPUShaderModule)
RELEASE(wgpuBindGroupLayoutRelease, WGPUBindGroupLayout)
RELEASE(wgpuBindGroupRelease, WGPUBindGroup)
RELEASE(wgpuPipelineLayoutRelease, WGPUPipelineLayout)
RELEASE(wgpuComputePipelineRelease, WGPUComputePipeline)
RELEASE(wgpuRenderPipelineRelease, WGPURenderPipeline)
RELEASE(wgpuQuerySetRelease, WGPUQuerySet)
RELEASE(wgpuCommandEncoderRelease, WGPUCommandEncoder)
RELEASE(wgpuCommandBufferRelease, WGPUCommandBuffer)
RELEASE(wgpuRenderPassEncoderRelease, WGPURenderPassEncoder)
RELEASE(wgpuComputePassEncoderRelease, WGPUComputePassEncoder)
RELEASE(wgpuSurfaceRelease, WGPUSurface)

// ---------------------------------------------------------------------------------------------------
// Buffers.

WGPUFuture wgpuBufferMapAsync(WGPUBuffer buffer, WGPUMapMode mode, size_t offset, size_t size,
                              WGPUBufferMapCallbackInfo info) {
  uint32_t op = keep(OP_MAP, (void*)info.callback, info.userdata1, info.userdata2);
  oiw_call_d("mapAsync", H(buffer), (double)mode, (double)offset, size == WGPU_WHOLE_MAP_SIZE ? -1.0 : (double)size, (double)op);
  return future();
}

// The mapped bytes, copied out of the browser's mapping into memory kept here until the unmap. A few
// buffers are mapped at a time -- the readbacks in flight -- so a short list is all it needs.
typedef struct {
  uint32_t buffer;
  void* bytes;
} mapping;

#define MAX_MAPPINGS 256
static mapping mappings[MAX_MAPPINGS];

static void forget_mapping(uint32_t buffer) {
  for (int i = 0; i < MAX_MAPPINGS; i++) {
    if (mappings[i].buffer == buffer) {
      free(mappings[i].bytes);
      mappings[i] = (mapping){0, NULL};
    }
  }
}

void const* wgpuBufferGetConstMappedRange(WGPUBuffer buffer, size_t offset, size_t size) {
  forget_mapping(H(buffer));
  for (int i = 0; i < MAX_MAPPINGS; i++) {
    if (mappings[i].buffer == 0) {
      mappings[i] = (mapping){H(buffer), malloc(size ? size : 1)};
      oiw_call_d("getMappedRange", H(buffer), (double)offset, (double)size, (double)H(mappings[i].bytes), 0);
      return mappings[i].bytes;
    }
  }
  return NULL;
}

void wgpuBufferUnmap(WGPUBuffer buffer) {
  forget_mapping(H(buffer));
  CALL("unmap", H(buffer), 0, 0, 0, 0, 0);
}

// ---------------------------------------------------------------------------------------------------
// The queue.

void wgpuQueueSubmit(WGPUQueue queue, size_t count, WGPUCommandBuffer const* commands) {
  CALL("submit", H(queue), (uint32_t)count, H(commands), 0, 0, 0);
}

// The queue's own promise for "everything submitted so far has finished", reported like a map: the
// callback is kept here and fired from the next ProcessEvents after the promise settles.
WGPUFuture wgpuQueueOnSubmittedWorkDone(WGPUQueue queue, WGPUQueueWorkDoneCallbackInfo info) {
  uint32_t op = keep(OP_WORK_DONE, (void*)info.callback, info.userdata1, info.userdata2);
  CALL("onSubmittedWorkDone", H(queue), op, 0, 0, 0, 0);
  return future();
}

void wgpuQueueWriteBuffer(WGPUQueue queue, WGPUBuffer buffer, uint64_t offset, void const* data, size_t size) {
  oiw_call_d("writeBuffer", H(queue), (double)H(buffer), (double)offset, (double)H(data), (double)size);
}

void wgpuQueueWriteTexture(WGPUQueue queue, WGPUTexelCopyTextureInfo const* destination, void const* data, size_t size,
                           WGPUTexelCopyBufferLayout const* layout, WGPUExtent3D const* extent) {
  CALL("writeTexture", H(queue), H(destination), H(data), (uint32_t)size, H(layout), H(extent));
}

// ---------------------------------------------------------------------------------------------------
// The command encoder.

WGPURenderPassEncoder wgpuCommandEncoderBeginRenderPass(WGPUCommandEncoder encoder, WGPURenderPassDescriptor const* d) {
  return (WGPURenderPassEncoder)(uintptr_t)CALL("beginRenderPass", H(encoder), H(d), 0, 0, 0, 0);
}

WGPUComputePassEncoder wgpuCommandEncoderBeginComputePass(WGPUCommandEncoder encoder, WGPUComputePassDescriptor const* d) {
  return (WGPUComputePassEncoder)(uintptr_t)CALL("beginComputePass", H(encoder), H(d), 0, 0, 0, 0);
}

void wgpuCommandEncoderClearBuffer(WGPUCommandEncoder encoder, WGPUBuffer buffer, uint64_t offset, uint64_t size) {
  oiw_call_d("clearBuffer", H(encoder), (double)H(buffer), (double)offset, WHOLE(size), 0);
}

void wgpuCommandEncoderCopyBufferToBuffer(WGPUCommandEncoder encoder, WGPUBuffer source, uint64_t sourceOffset,
                                          WGPUBuffer destination, uint64_t destinationOffset, uint64_t size) {
  // Five numbers do not fit the double-taking call, so the two handles go first by themselves.
  CALL("copyBufferToBufferHandles", H(encoder), H(source), H(destination), 0, 0, 0);
  oiw_call_d("copyBufferToBuffer", H(encoder), (double)sourceOffset, (double)destinationOffset, WHOLE(size), 0);
}

void wgpuCommandEncoderCopyBufferToTexture(WGPUCommandEncoder encoder, WGPUTexelCopyBufferInfo const* source,
                                           WGPUTexelCopyTextureInfo const* destination, WGPUExtent3D const* size) {
  CALL("copyBufferToTexture", H(encoder), H(source), H(destination), H(size), 0, 0);
}

void wgpuCommandEncoderCopyTextureToBuffer(WGPUCommandEncoder encoder, WGPUTexelCopyTextureInfo const* source,
                                           WGPUTexelCopyBufferInfo const* destination, WGPUExtent3D const* size) {
  CALL("copyTextureToBuffer", H(encoder), H(source), H(destination), H(size), 0, 0);
}

void wgpuCommandEncoderCopyTextureToTexture(WGPUCommandEncoder encoder, WGPUTexelCopyTextureInfo const* source,
                                            WGPUTexelCopyTextureInfo const* destination, WGPUExtent3D const* size) {
  CALL("copyTextureToTexture", H(encoder), H(source), H(destination), H(size), 0, 0);
}

void wgpuCommandEncoderResolveQuerySet(WGPUCommandEncoder encoder, WGPUQuerySet set, uint32_t first, uint32_t count,
                                       WGPUBuffer destination, uint64_t offset) {
  CALL("resolveQuerySetHandles", H(encoder), H(set), H(destination), 0, 0, 0);
  oiw_call_d("resolveQuerySet", H(encoder), (double)first, (double)count, (double)offset, 0);
}

WGPUCommandBuffer wgpuCommandEncoderFinish(WGPUCommandEncoder encoder, WGPUCommandBufferDescriptor const* d) {
  return (WGPUCommandBuffer)(uintptr_t)CALL("finish", H(encoder), H(d), 0, 0, 0, 0);
}

// ---------------------------------------------------------------------------------------------------
// The compute pass.

void wgpuComputePassEncoderSetPipeline(WGPUComputePassEncoder pass, WGPUComputePipeline pipeline) {
  CALL("setPipeline", H(pass), H(pipeline), 0, 0, 0, 0);
}

void wgpuComputePassEncoderSetBindGroup(WGPUComputePassEncoder pass, uint32_t index, WGPUBindGroup group, size_t count,
                                        uint32_t const* offsets) {
  CALL("setBindGroup", H(pass), index, H(group), (uint32_t)count, H(offsets), 0);
}

void wgpuComputePassEncoderDispatchWorkgroups(WGPUComputePassEncoder pass, uint32_t x, uint32_t y, uint32_t z) {
  CALL("dispatch", H(pass), x, y, z, 0, 0);
}

void wgpuComputePassEncoderDispatchWorkgroupsIndirect(WGPUComputePassEncoder pass, WGPUBuffer buffer, uint64_t offset) {
  CALL("dispatchIndirect", H(pass), H(buffer), (uint32_t)offset, 0, 0, 0);
}

void wgpuComputePassEncoderPushDebugGroup(WGPUComputePassEncoder pass, WGPUStringView label) {
  CALL("pushDebugGroup", H(pass), H(label.data), (uint32_t)label.length, 0, 0, 0);
}

void wgpuComputePassEncoderPopDebugGroup(WGPUComputePassEncoder pass) { CALL("popDebugGroup", H(pass), 0, 0, 0, 0, 0); }

void wgpuComputePassEncoderEnd(WGPUComputePassEncoder pass) { CALL("end", H(pass), 0, 0, 0, 0, 0); }

// ---------------------------------------------------------------------------------------------------
// The render pass.

void wgpuRenderPassEncoderSetPipeline(WGPURenderPassEncoder pass, WGPURenderPipeline pipeline) {
  uint32_t* q = reserve(3);
  q[0] = Q_PIPELINE; q[1] = H(pass); q[2] = H(pipeline);
}

void wgpuRenderPassEncoderSetBindGroup(WGPURenderPassEncoder pass, uint32_t index, WGPUBindGroup group, size_t count,
                                       uint32_t const* offsets) {
  // The offsets are copied in: they live on the caller's stack, gone long before the queue is sent.
  uint32_t* q = reserve(5 + (uint32_t)count);
  q[0] = Q_BIND; q[1] = H(pass); q[2] = index; q[3] = H(group); q[4] = (uint32_t)count;
  for (size_t i = 0; i < count; i++) q[5 + i] = offsets[i];
}

void wgpuRenderPassEncoderSetVertexBuffer(WGPURenderPassEncoder pass, uint32_t slot, WGPUBuffer buffer, uint64_t offset,
                                          uint64_t size) {
  uint32_t* q = reserve(8);
  q[0] = Q_VERTEX; q[1] = H(pass); q[2] = slot; q[3] = H(buffer);
  q[4] = (uint32_t)offset; q[5] = (uint32_t)(offset >> 32);
  q[6] = (uint32_t)size; q[7] = (uint32_t)(size >> 32);
}

void wgpuRenderPassEncoderSetIndexBuffer(WGPURenderPassEncoder pass, WGPUBuffer buffer, WGPUIndexFormat format,
                                         uint64_t offset, uint64_t size) {
  uint32_t* q = reserve(8);
  q[0] = Q_INDEX; q[1] = H(pass); q[2] = H(buffer); q[3] = (uint32_t)format;
  q[4] = (uint32_t)offset; q[5] = (uint32_t)(offset >> 32);
  q[6] = (uint32_t)size; q[7] = (uint32_t)(size >> 32);
}

void wgpuRenderPassEncoderDraw(WGPURenderPassEncoder pass, uint32_t vertices, uint32_t instances, uint32_t firstVertex,
                               uint32_t firstInstance) {
  uint32_t* q = reserve(6);
  q[0] = Q_DRAW; q[1] = H(pass); q[2] = vertices; q[3] = instances; q[4] = firstVertex; q[5] = firstInstance;
}

void wgpuRenderPassEncoderDrawIndexed(WGPURenderPassEncoder pass, uint32_t indices, uint32_t instances, uint32_t firstIndex,
                                      int32_t baseVertex, uint32_t firstInstance) {
  uint32_t* q = reserve(7);
  q[0] = Q_INDEXED; q[1] = H(pass); q[2] = indices; q[3] = instances; q[4] = firstIndex; q[5] = (uint32_t)baseVertex;
  q[6] = firstInstance;
}

void wgpuRenderPassEncoderDrawIndirect(WGPURenderPassEncoder pass, WGPUBuffer buffer, uint64_t offset) {
  CALL("drawIndirect", H(pass), H(buffer), (uint32_t)offset, 0, 0, 0);
}

void wgpuRenderPassEncoderDrawIndexedIndirect(WGPURenderPassEncoder pass, WGPUBuffer buffer, uint64_t offset) {
  CALL("drawIndexedIndirect", H(pass), H(buffer), (uint32_t)offset, 0, 0, 0);
}

void wgpuRenderPassEncoderSetViewport(WGPURenderPassEncoder pass, float x, float y, float width, float height, float minDepth,
                                      float maxDepth) {
  oiw_call_d("setViewport", H(pass), x, y, width, height);
  oiw_call_d("setViewportDepth", H(pass), minDepth, maxDepth, 0, 0);
}

void wgpuRenderPassEncoderSetScissorRect(WGPURenderPassEncoder pass, uint32_t x, uint32_t y, uint32_t width, uint32_t height) {
  uint32_t* q = reserve(6);
  q[0] = Q_SCISSOR; q[1] = H(pass); q[2] = x; q[3] = y; q[4] = width; q[5] = height;
}

void wgpuRenderPassEncoderSetBlendConstant(WGPURenderPassEncoder pass, WGPUColor const* color) {
  CALL("setBlendConstant", H(pass), H(color), 0, 0, 0, 0);
}

void wgpuRenderPassEncoderBeginOcclusionQuery(WGPURenderPassEncoder pass, uint32_t index) {
  CALL("beginOcclusionQuery", H(pass), index, 0, 0, 0, 0);
}

void wgpuRenderPassEncoderEndOcclusionQuery(WGPURenderPassEncoder pass) { CALL("endOcclusionQuery", H(pass), 0, 0, 0, 0, 0); }

void wgpuRenderPassEncoderPushDebugGroup(WGPURenderPassEncoder pass, WGPUStringView label) {
  CALL("pushDebugGroup", H(pass), H(label.data), (uint32_t)label.length, 0, 0, 0);
}

void wgpuRenderPassEncoderPopDebugGroup(WGPURenderPassEncoder pass) { CALL("popDebugGroup", H(pass), 0, 0, 0, 0, 0); }

void wgpuRenderPassEncoderEnd(WGPURenderPassEncoder pass) { CALL("end", H(pass), 0, 0, 0, 0, 0); }

// ---------------------------------------------------------------------------------------------------
// The surface: the page's canvas, whatever the descriptor names.

WGPUSurface wgpuInstanceCreateSurface(WGPUInstance instance, WGPUSurfaceDescriptor const* d) {
  (void)d;
  return (WGPUSurface)(uintptr_t)CALL("createSurface", H(instance), 0, 0, 0, 0, 0);
}

WGPUStatus wgpuSurfaceGetCapabilities(WGPUSurface surface, WGPUAdapter adapter, WGPUSurfaceCapabilities* caps) {
  (void)adapter;
  static WGPUTextureFormat formats[1];
  static WGPUPresentMode modes[1] = {WGPUPresentMode_Fifo};
  static WGPUCompositeAlphaMode alphas[1] = {WGPUCompositeAlphaMode_Opaque};
  formats[0] = (WGPUTextureFormat)CALL("preferredFormat", H(surface), 0, 0, 0, 0, 0);
  memset(caps, 0, sizeof(*caps));
  caps->usages = WGPUTextureUsage_RenderAttachment | WGPUTextureUsage_CopySrc | WGPUTextureUsage_CopyDst
                 | WGPUTextureUsage_TextureBinding;
  caps->formatCount = 1;
  caps->formats = formats;
  caps->presentModeCount = 1;
  caps->presentModes = modes;
  caps->alphaModeCount = 1;
  caps->alphaModes = alphas;
  return WGPUStatus_Success;
}

void wgpuSurfaceCapabilitiesFreeMembers(WGPUSurfaceCapabilities caps) { (void)caps; }

void wgpuSurfaceConfigure(WGPUSurface surface, WGPUSurfaceConfiguration const* config) {
  CALL("configure", H(surface), H(config), 0, 0, 0, 0);
}

void wgpuSurfaceUnconfigure(WGPUSurface surface) { CALL("unconfigure", H(surface), 0, 0, 0, 0, 0); }

void wgpuSurfaceGetCurrentTexture(WGPUSurface surface, WGPUSurfaceTexture* texture) {
  texture->texture = (WGPUTexture)(uintptr_t)CALL("currentTexture", H(surface), 0, 0, 0, 0, 0);
  texture->status = texture->texture ? WGPUSurfaceGetCurrentTextureStatus_SuccessOptimal
                                     : WGPUSurfaceGetCurrentTextureStatus_Error;
}

// The browser presents what the frame drew when the frame's task ends.
WGPUStatus wgpuSurfacePresent(WGPUSurface surface) {
  (void)surface;
  return WGPUStatus_Success;
}
