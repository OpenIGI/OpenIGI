// The JavaScript half of webgpu.h in a browser: the WebGPU objects, and the structs the C# side filled in,
// read straight out of wasm memory.
//
// wgpu_native.c forwards every webgpu.h call here as a name and a handful of numbers -- handles, and
// POINTERS to the descriptors the backend wrote. The offsets of every field this reads were computed by
// the compiler from the real header and handed over once at start-up (field), so nothing here knows a
// layout of its own: it asks for 'WGPUBufferDescriptor.size' and reads there.
//
// Handles are indices into one table. Nought is the null handle.

import { E, V } from './wgpu-enums.g.js';

const U32_UNDEFINED = 0xFFFFFFFF;

let heap = () => { throw new Error('The WebGPU bridge was used before the runtime attached it.'); };
const offsets = new Map();
const names = new Map();
const objects = [null];
const free = [];
const completions = [];
const passes = new Map();

let view = null;
const dv = () => {
  const buffer = heap().buffer;
  if (view === null || view.buffer !== buffer) {
    view = new DataView(buffer);
  }
  return view;
};

const u8 = p => dv().getUint8(p);
const u16 = p => dv().getUint16(p, true);
const u32 = p => dv().getUint32(p, true);
const i32 = p => dv().getInt32(p, true);
const f32 = p => dv().getFloat32(p, true);
const f64 = p => dv().getFloat64(p, true);
const u64 = p => u32(p) + u32(p + 4) * 0x100000000;
const ptr = u32;
const setU32 = (p, v) => dv().setUint32(p, v >>> 0, true);
const setU64 = (p, v) => { setU32(p, v % 0x100000000); setU32(p + 4, Math.floor(v / 0x100000000)); };

const decoder = new TextDecoder();
const encoder = new TextEncoder();

function cString(p) {
  let end = p;
  const bytes = heap();
  while (bytes[end] !== 0) end++;
  return decoder.decode(bytes.subarray(p, end));
}

function name(p) {
  let text = names.get(p);
  if (text === undefined) {
    text = cString(p);
    names.set(p, text);
  }
  return text;
}

function o(field) {
  const at = offsets.get(field);
  if (at === undefined) throw new Error(`The WebGPU bridge has no offset for ${field}.`);
  return at;
}

const at = (base, field) => base + o(field);

function stringView(p) {
  const data = ptr(p + o('WGPUStringView.data'));
  const length = u32(p + o('WGPUStringView.length'));
  if (data === 0) return undefined;
  if (length === U32_UNDEFINED) return cString(data);
  return decoder.decode(heap().subarray(data, data + length));
}

const label = (d, type) => stringView(at(d, `${type}.label`)) || undefined;
const optional = v => (v === U32_UNDEFINED ? undefined : v);

function add(object, destroy = false) {
  const entry = { object, destroy };
  if (free.length) {
    const id = free.pop();
    objects[id] = entry;
    return id;
  }
  objects.push(entry);
  return objects.length - 1;
}

const get = h => (h === 0 ? undefined : objects[h]?.object);

function release(h) {
  const entry = objects[h];
  if (!entry) return 0;
  if (entry.destroy) entry.object.destroy?.();
  objects[h] = null;
  passes.delete(h);
  free.push(h);
  return 0;
}

function array(p, count, stride, read) {
  const items = [];
  for (let i = 0; i < count; i++) items.push(read(p + i * stride));
  return items;
}

function constants(p, count) {
  if (count === 0) return undefined;
  const entries = {};
  for (let i = 0; i < count; i++) {
    const c = p + i * o('WGPUConstantEntry.$size');
    entries[stringView(at(c, 'WGPUConstantEntry.key'))] = f64(at(c, 'WGPUConstantEntry.value'));
  }
  return entries;
}

function extent(p) {
  return {
    width: u32(at(p, 'WGPUExtent3D.width')),
    height: u32(at(p, 'WGPUExtent3D.height')),
    depthOrArrayLayers: u32(at(p, 'WGPUExtent3D.depthOrArrayLayers')),
  };
}

function textureCopy(p) {
  const origin = at(p, 'WGPUTexelCopyTextureInfo.origin');
  return {
    texture: get(ptr(at(p, 'WGPUTexelCopyTextureInfo.texture'))),
    mipLevel: u32(at(p, 'WGPUTexelCopyTextureInfo.mipLevel')),
    origin: { x: u32(at(origin, 'WGPUOrigin3D.x')), y: u32(at(origin, 'WGPUOrigin3D.y')), z: u32(at(origin, 'WGPUOrigin3D.z')) },
    aspect: E.TextureAspect[u32(at(p, 'WGPUTexelCopyTextureInfo.aspect'))],
  };
}

function bufferLayout(p) {
  return {
    offset: u64(at(p, 'WGPUTexelCopyBufferLayout.offset')),
    bytesPerRow: optional(u32(at(p, 'WGPUTexelCopyBufferLayout.bytesPerRow'))),
    rowsPerImage: optional(u32(at(p, 'WGPUTexelCopyBufferLayout.rowsPerImage'))),
  };
}

function bufferCopy(p) {
  return { ...bufferLayout(at(p, 'WGPUTexelCopyBufferInfo.layout')), buffer: get(ptr(at(p, 'WGPUTexelCopyBufferInfo.buffer'))) };
}

function timestampWrites(p) {
  if (p === 0) return undefined;
  return {
    querySet: get(ptr(at(p, 'WGPUPassTimestampWrites.querySet'))),
    beginningOfPassWriteIndex: optional(u32(at(p, 'WGPUPassTimestampWrites.beginningOfPassWriteIndex'))),
    endOfPassWriteIndex: optional(u32(at(p, 'WGPUPassTimestampWrites.endOfPassWriteIndex'))),
  };
}

function blendComponent(p) {
  return {
    operation: E.BlendOperation[u32(at(p, 'WGPUBlendComponent.operation'))],
    srcFactor: E.BlendFactor[u32(at(p, 'WGPUBlendComponent.srcFactor'))],
    dstFactor: E.BlendFactor[u32(at(p, 'WGPUBlendComponent.dstFactor'))],
  };
}

function stencilFace(p) {
  return {
    compare: E.CompareFunction[u32(at(p, 'WGPUStencilFaceState.compare'))],
    failOp: E.StencilOperation[u32(at(p, 'WGPUStencilFaceState.failOp'))],
    depthFailOp: E.StencilOperation[u32(at(p, 'WGPUStencilFaceState.depthFailOp'))],
    passOp: E.StencilOperation[u32(at(p, 'WGPUStencilFaceState.passOp'))],
  };
}

const LIMITS_64 = new Set(['maxUniformBufferBindingSize', 'maxStorageBufferBindingSize', 'maxBufferSize']);

// Binding types whose header value is "Undefined" mean the default; "BindingNotUsed" means no such entry.
const bindingType = (value, table, fallback) => (value === 0 ? undefined : value === 1 ? fallback : table[value]);

const passState = h => passes.get(h) ?? (passes.set(h, {}), passes.get(h));

let adapter = null;
let device = null;
let canvas = null;
let adapterHandle = 0;
let deviceHandle = 0;
let queueHandle = 0;

const calls = {
  createInstance: () => add({ instance: true }),
  requestAdapter: (instance, op) => { completions.push([op, 1, adapterHandle]); return 0; },
  requestDevice: (a, op) => { completions.push([op, 1, deviceHandle]); return 0; },

  featureCount: () => [...device.features].filter(f => V.FeatureName[f] !== undefined).length,
  features: (a, list, count) => {
    [...device.features].map(f => V.FeatureName[f]).filter(v => v !== undefined).slice(0, count)
      .forEach((v, i) => setU32(list + i * 4, v));
    return 0;
  },
  hasFeature: (a, feature) => (device.features.has(E.FeatureName[feature]) ? 1 : 0),

  stringLength: (h, which) => encoder.encode(adapter.info?.[name(which)] ?? '').length,
  stringCopy: (h, which, into, length) => {
    heap().set(encoder.encode(adapter.info?.[name(which)] ?? '').subarray(0, length), into);
    return 0;
  },

  limits: (h, p) => {
    const limits = device.limits;
    for (const [field] of offsets) {
      if (!field.startsWith('WGPULimits.')) continue;
      const key = field.slice('WGPULimits.'.length);
      const value = limits[key] ?? 0;
      if (LIMITS_64.has(key)) setU64(p + o(field), value);
      else setU32(p + o(field), Math.min(value, 0xFFFFFFFF));
    }
    return 0;
  },

  getQueue: () => queueHandle,

  createBuffer: (d, p) => add(device.createBuffer({
    label: label(p, 'WGPUBufferDescriptor'),
    usage: u64(at(p, 'WGPUBufferDescriptor.usage')),
    size: u64(at(p, 'WGPUBufferDescriptor.size')),
    mappedAtCreation: u32(at(p, 'WGPUBufferDescriptor.mappedAtCreation')) !== 0,
  }), true),

  createTexture: (d, p) => add(device.createTexture({
    label: label(p, 'WGPUTextureDescriptor'),
    usage: u64(at(p, 'WGPUTextureDescriptor.usage')),
    dimension: E.TextureDimension[u32(at(p, 'WGPUTextureDescriptor.dimension'))],
    size: extent(at(p, 'WGPUTextureDescriptor.size')),
    format: E.TextureFormat[u32(at(p, 'WGPUTextureDescriptor.format'))],
    mipLevelCount: u32(at(p, 'WGPUTextureDescriptor.mipLevelCount')),
    sampleCount: u32(at(p, 'WGPUTextureDescriptor.sampleCount')),
    viewFormats: array(ptr(at(p, 'WGPUTextureDescriptor.viewFormats')), u32(at(p, 'WGPUTextureDescriptor.viewFormatCount')), 4,
      q => E.TextureFormat[u32(q)]),
  }), true),

  createView: (t, p) => {
    const texture = get(t);
    if (p === 0) return add(texture.createView());
    const usage = u64(at(p, 'WGPUTextureViewDescriptor.usage'));
    return add(texture.createView({
      label: label(p, 'WGPUTextureViewDescriptor'),
      format: E.TextureFormat[u32(at(p, 'WGPUTextureViewDescriptor.format'))],
      dimension: E.TextureViewDimension[u32(at(p, 'WGPUTextureViewDescriptor.dimension'))],
      baseMipLevel: u32(at(p, 'WGPUTextureViewDescriptor.baseMipLevel')),
      mipLevelCount: optional(u32(at(p, 'WGPUTextureViewDescriptor.mipLevelCount'))),
      baseArrayLayer: u32(at(p, 'WGPUTextureViewDescriptor.baseArrayLayer')),
      arrayLayerCount: optional(u32(at(p, 'WGPUTextureViewDescriptor.arrayLayerCount'))),
      aspect: E.TextureAspect[u32(at(p, 'WGPUTextureViewDescriptor.aspect'))],
      usage: usage === 0 ? undefined : usage,
    }));
  },

  createSampler: (d, p) => add(device.createSampler({
    label: label(p, 'WGPUSamplerDescriptor'),
    addressModeU: E.AddressMode[u32(at(p, 'WGPUSamplerDescriptor.addressModeU'))],
    addressModeV: E.AddressMode[u32(at(p, 'WGPUSamplerDescriptor.addressModeV'))],
    addressModeW: E.AddressMode[u32(at(p, 'WGPUSamplerDescriptor.addressModeW'))],
    magFilter: E.FilterMode[u32(at(p, 'WGPUSamplerDescriptor.magFilter'))],
    minFilter: E.FilterMode[u32(at(p, 'WGPUSamplerDescriptor.minFilter'))],
    mipmapFilter: E.MipmapFilterMode[u32(at(p, 'WGPUSamplerDescriptor.mipmapFilter'))],
    lodMinClamp: f32(at(p, 'WGPUSamplerDescriptor.lodMinClamp')),
    lodMaxClamp: f32(at(p, 'WGPUSamplerDescriptor.lodMaxClamp')),
    compare: E.CompareFunction[u32(at(p, 'WGPUSamplerDescriptor.compare'))],
    maxAnisotropy: u16(at(p, 'WGPUSamplerDescriptor.maxAnisotropy')) || 1,
  })),

  createShaderModule: (d, p) => {
    const chain = ptr(at(p, 'WGPUShaderModuleDescriptor.nextInChain'));
    if (chain === 0 || u32(at(chain, 'WGPUChainedStruct.sType')) !== 2) {
      throw new Error('Only WGSL shader modules can be made in a browser.');
    }
    return add(device.createShaderModule({
      label: label(p, 'WGPUShaderModuleDescriptor'),
      code: stringView(at(chain, 'WGPUShaderSourceWGSL.code')),
    }));
  },

  createBindGroupLayout: (d, p) => add(device.createBindGroupLayout({
    label: label(p, 'WGPUBindGroupLayoutDescriptor'),
    entries: array(ptr(at(p, 'WGPUBindGroupLayoutDescriptor.entries')), u32(at(p, 'WGPUBindGroupLayoutDescriptor.entryCount')),
      o('WGPUBindGroupLayoutEntry.$size'), e => {
        const entry = { binding: u32(at(e, 'WGPUBindGroupLayoutEntry.binding')), visibility: u64(at(e, 'WGPUBindGroupLayoutEntry.visibility')) };
        const buffer = at(e, 'WGPUBindGroupLayoutEntry.buffer');
        const bufferType = bindingType(u32(at(buffer, 'WGPUBufferBindingLayout.type')), E.BufferBindingType, 'uniform');
        if (bufferType) {
          entry.buffer = {
            type: bufferType,
            hasDynamicOffset: u32(at(buffer, 'WGPUBufferBindingLayout.hasDynamicOffset')) !== 0,
            minBindingSize: u64(at(buffer, 'WGPUBufferBindingLayout.minBindingSize')),
          };
        }
        const sampler = at(e, 'WGPUBindGroupLayoutEntry.sampler');
        const samplerType = bindingType(u32(at(sampler, 'WGPUSamplerBindingLayout.type')), E.SamplerBindingType, 'filtering');
        if (samplerType) entry.sampler = { type: samplerType };
        const texture = at(e, 'WGPUBindGroupLayoutEntry.texture');
        const sampleType = bindingType(u32(at(texture, 'WGPUTextureBindingLayout.sampleType')), E.TextureSampleType, 'float');
        if (sampleType) {
          entry.texture = {
            sampleType,
            viewDimension: E.TextureViewDimension[u32(at(texture, 'WGPUTextureBindingLayout.viewDimension'))],
            multisampled: u32(at(texture, 'WGPUTextureBindingLayout.multisampled')) !== 0,
          };
        }
        const storage = at(e, 'WGPUBindGroupLayoutEntry.storageTexture');
        const access = bindingType(u32(at(storage, 'WGPUStorageTextureBindingLayout.access')), E.StorageTextureAccess, 'write-only');
        if (access) {
          entry.storageTexture = {
            access,
            format: E.TextureFormat[u32(at(storage, 'WGPUStorageTextureBindingLayout.format'))],
            viewDimension: E.TextureViewDimension[u32(at(storage, 'WGPUStorageTextureBindingLayout.viewDimension'))],
          };
        }
        return entry;
      }),
  })),

  createBindGroup: (d, p) => add(device.createBindGroup({
    label: label(p, 'WGPUBindGroupDescriptor'),
    layout: get(ptr(at(p, 'WGPUBindGroupDescriptor.layout'))),
    entries: array(ptr(at(p, 'WGPUBindGroupDescriptor.entries')), u32(at(p, 'WGPUBindGroupDescriptor.entryCount')),
      o('WGPUBindGroupEntry.$size'), e => {
        const binding = u32(at(e, 'WGPUBindGroupEntry.binding'));
        const buffer = ptr(at(e, 'WGPUBindGroupEntry.buffer'));
        if (buffer !== 0) {
          const size = u64(at(e, 'WGPUBindGroupEntry.size'));
          return {
            binding,
            resource: { buffer: get(buffer), offset: u64(at(e, 'WGPUBindGroupEntry.offset')), size: size >= 2 ** 53 ? undefined : size },
          };
        }
        const sampler = ptr(at(e, 'WGPUBindGroupEntry.sampler'));
        if (sampler !== 0) return { binding, resource: get(sampler) };
        return { binding, resource: get(ptr(at(e, 'WGPUBindGroupEntry.textureView'))) };
      }),
  })),

  createPipelineLayout: (d, p) => add(device.createPipelineLayout({
    label: label(p, 'WGPUPipelineLayoutDescriptor'),
    bindGroupLayouts: array(ptr(at(p, 'WGPUPipelineLayoutDescriptor.bindGroupLayouts')),
      u32(at(p, 'WGPUPipelineLayoutDescriptor.bindGroupLayoutCount')), 4, q => get(ptr(q))),
  })),

  createComputePipeline: (d, p) => {
    const c = at(p, 'WGPUComputePipelineDescriptor.compute');
    const layout = ptr(at(p, 'WGPUComputePipelineDescriptor.layout'));
    return add(device.createComputePipeline({
      label: label(p, 'WGPUComputePipelineDescriptor'),
      layout: layout === 0 ? 'auto' : get(layout),
      compute: {
        module: get(ptr(at(c, 'WGPUComputeState.module'))),
        entryPoint: stringView(at(c, 'WGPUComputeState.entryPoint')),
        constants: constants(ptr(at(c, 'WGPUComputeState.constants')), u32(at(c, 'WGPUComputeState.constantCount'))),
      },
    }));
  },

  createRenderPipeline: (d, p) => {
    const v = at(p, 'WGPURenderPipelineDescriptor.vertex');
    const pr = at(p, 'WGPURenderPipelineDescriptor.primitive');
    const ds = ptr(at(p, 'WGPURenderPipelineDescriptor.depthStencil'));
    const ms = at(p, 'WGPURenderPipelineDescriptor.multisample');
    const fr = ptr(at(p, 'WGPURenderPipelineDescriptor.fragment'));
    const layout = ptr(at(p, 'WGPURenderPipelineDescriptor.layout'));
    const descriptor = {
      label: label(p, 'WGPURenderPipelineDescriptor'),
      layout: layout === 0 ? 'auto' : get(layout),
      vertex: {
        module: get(ptr(at(v, 'WGPUVertexState.module'))),
        entryPoint: stringView(at(v, 'WGPUVertexState.entryPoint')),
        constants: constants(ptr(at(v, 'WGPUVertexState.constants')), u32(at(v, 'WGPUVertexState.constantCount'))),
        buffers: array(ptr(at(v, 'WGPUVertexState.buffers')), u32(at(v, 'WGPUVertexState.bufferCount')),
          o('WGPUVertexBufferLayout.$size'), b => ({
            arrayStride: u64(at(b, 'WGPUVertexBufferLayout.arrayStride')),
            stepMode: E.VertexStepMode[u32(at(b, 'WGPUVertexBufferLayout.stepMode'))],
            attributes: array(ptr(at(b, 'WGPUVertexBufferLayout.attributes')), u32(at(b, 'WGPUVertexBufferLayout.attributeCount')),
              o('WGPUVertexAttribute.$size'), a => ({
                format: E.VertexFormat[u32(at(a, 'WGPUVertexAttribute.format'))],
                offset: u64(at(a, 'WGPUVertexAttribute.offset')),
                shaderLocation: u32(at(a, 'WGPUVertexAttribute.shaderLocation')),
              })),
          })),
      },
      primitive: {
        topology: E.PrimitiveTopology[u32(at(pr, 'WGPUPrimitiveState.topology'))],
        stripIndexFormat: E.IndexFormat[u32(at(pr, 'WGPUPrimitiveState.stripIndexFormat'))],
        frontFace: E.FrontFace[u32(at(pr, 'WGPUPrimitiveState.frontFace'))],
        cullMode: E.CullMode[u32(at(pr, 'WGPUPrimitiveState.cullMode'))],
        unclippedDepth: u32(at(pr, 'WGPUPrimitiveState.unclippedDepth')) !== 0,
      },
      multisample: {
        count: u32(at(ms, 'WGPUMultisampleState.count')) || 1,
        mask: u32(at(ms, 'WGPUMultisampleState.mask')),
        alphaToCoverageEnabled: u32(at(ms, 'WGPUMultisampleState.alphaToCoverageEnabled')) !== 0,
      },
    };
    if (ds !== 0) {
      const write = u32(at(ds, 'WGPUDepthStencilState.depthWriteEnabled'));
      descriptor.depthStencil = {
        format: E.TextureFormat[u32(at(ds, 'WGPUDepthStencilState.format'))],
        depthWriteEnabled: write === 2 ? undefined : write === 1,
        depthCompare: E.CompareFunction[u32(at(ds, 'WGPUDepthStencilState.depthCompare'))],
        stencilFront: stencilFace(at(ds, 'WGPUDepthStencilState.stencilFront')),
        stencilBack: stencilFace(at(ds, 'WGPUDepthStencilState.stencilBack')),
        stencilReadMask: u32(at(ds, 'WGPUDepthStencilState.stencilReadMask')),
        stencilWriteMask: u32(at(ds, 'WGPUDepthStencilState.stencilWriteMask')),
        depthBias: i32(at(ds, 'WGPUDepthStencilState.depthBias')),
        depthBiasSlopeScale: f32(at(ds, 'WGPUDepthStencilState.depthBiasSlopeScale')),
        depthBiasClamp: f32(at(ds, 'WGPUDepthStencilState.depthBiasClamp')),
      };
    }
    if (fr !== 0) {
      descriptor.fragment = {
        module: get(ptr(at(fr, 'WGPUFragmentState.module'))),
        entryPoint: stringView(at(fr, 'WGPUFragmentState.entryPoint')),
        constants: constants(ptr(at(fr, 'WGPUFragmentState.constants')), u32(at(fr, 'WGPUFragmentState.constantCount'))),
        targets: array(ptr(at(fr, 'WGPUFragmentState.targets')), u32(at(fr, 'WGPUFragmentState.targetCount')),
          o('WGPUColorTargetState.$size'), t => {
            const blend = ptr(at(t, 'WGPUColorTargetState.blend'));
            return {
              format: E.TextureFormat[u32(at(t, 'WGPUColorTargetState.format'))],
              blend: blend === 0 ? undefined : {
                color: blendComponent(at(blend, 'WGPUBlendState.color')),
                alpha: blendComponent(at(blend, 'WGPUBlendState.alpha')),
              },
              writeMask: u64(at(t, 'WGPUColorTargetState.writeMask')),
            };
          }),
      };
    }
    return add(device.createRenderPipeline(descriptor));
  },

  createQuerySet: (d, p) => add(device.createQuerySet({
    label: label(p, 'WGPUQuerySetDescriptor'),
    type: E.QueryType[u32(at(p, 'WGPUQuerySetDescriptor.type'))],
    count: u32(at(p, 'WGPUQuerySetDescriptor.count')),
  }), true),

  createCommandEncoder: (d, p) => add(device.createCommandEncoder({ label: p === 0 ? undefined : label(p, 'WGPUCommandEncoderDescriptor') })),

  beginRenderPass: (enc, p) => {
    const colors = array(ptr(at(p, 'WGPURenderPassDescriptor.colorAttachments')),
      u32(at(p, 'WGPURenderPassDescriptor.colorAttachmentCount')), o('WGPURenderPassColorAttachment.$size'), c => {
        const clear = at(c, 'WGPURenderPassColorAttachment.clearValue');
        const view = ptr(at(c, 'WGPURenderPassColorAttachment.view'));
        if (view === 0) return null;
        return {
          view: get(view),
          depthSlice: optional(u32(at(c, 'WGPURenderPassColorAttachment.depthSlice'))),
          resolveTarget: get(ptr(at(c, 'WGPURenderPassColorAttachment.resolveTarget'))),
          loadOp: E.LoadOp[u32(at(c, 'WGPURenderPassColorAttachment.loadOp'))],
          storeOp: E.StoreOp[u32(at(c, 'WGPURenderPassColorAttachment.storeOp'))],
          clearValue: { r: f64(at(clear, 'WGPUColor.r')), g: f64(at(clear, 'WGPUColor.g')), b: f64(at(clear, 'WGPUColor.b')), a: f64(at(clear, 'WGPUColor.a')) },
        };
      });
    const descriptor = {
      label: label(p, 'WGPURenderPassDescriptor'),
      colorAttachments: colors,
      occlusionQuerySet: get(ptr(at(p, 'WGPURenderPassDescriptor.occlusionQuerySet'))),
      timestampWrites: timestampWrites(ptr(at(p, 'WGPURenderPassDescriptor.timestampWrites'))),
    };
    const ds = ptr(at(p, 'WGPURenderPassDescriptor.depthStencilAttachment'));
    if (ds !== 0) {
      const depthReadOnly = u32(at(ds, 'WGPURenderPassDepthStencilAttachment.depthReadOnly')) !== 0;
      const stencilReadOnly = u32(at(ds, 'WGPURenderPassDepthStencilAttachment.stencilReadOnly')) !== 0;
      const attachment = { view: get(ptr(at(ds, 'WGPURenderPassDepthStencilAttachment.view'))), depthReadOnly, stencilReadOnly };
      if (!depthReadOnly) {
        attachment.depthLoadOp = E.LoadOp[u32(at(ds, 'WGPURenderPassDepthStencilAttachment.depthLoadOp'))];
        attachment.depthStoreOp = E.StoreOp[u32(at(ds, 'WGPURenderPassDepthStencilAttachment.depthStoreOp'))];
        attachment.depthClearValue = f32(at(ds, 'WGPURenderPassDepthStencilAttachment.depthClearValue'));
      }
      if (!stencilReadOnly) {
        attachment.stencilLoadOp = E.LoadOp[u32(at(ds, 'WGPURenderPassDepthStencilAttachment.stencilLoadOp'))];
        attachment.stencilStoreOp = E.StoreOp[u32(at(ds, 'WGPURenderPassDepthStencilAttachment.stencilStoreOp'))];
        attachment.stencilClearValue = u32(at(ds, 'WGPURenderPassDepthStencilAttachment.stencilClearValue'));
      }
      descriptor.depthStencilAttachment = attachment;
    }
    return add(get(enc).beginRenderPass(descriptor));
  },

  beginComputePass: (enc, p) => add(get(enc).beginComputePass(p === 0 ? undefined : {
    label: label(p, 'WGPUComputePassDescriptor'),
    timestampWrites: timestampWrites(ptr(at(p, 'WGPUComputePassDescriptor.timestampWrites'))),
  })),

  copyBufferToBufferHandles: (enc, src, dst) => { Object.assign(passState(enc), { src, dst }); return 0; },
  copyBufferToTexture: (enc, src, dst, size) => { get(enc).copyBufferToTexture(bufferCopy(src), textureCopy(dst), extent(size)); return 0; },
  copyTextureToBuffer: (enc, src, dst, size) => { get(enc).copyTextureToBuffer(textureCopy(src), bufferCopy(dst), extent(size)); return 0; },
  copyTextureToTexture: (enc, src, dst, size) => { get(enc).copyTextureToTexture(textureCopy(src), textureCopy(dst), extent(size)); return 0; },
  resolveQuerySetHandles: (enc, set, dst) => { Object.assign(passState(enc), { set, dst }); return 0; },
  finish: enc => add(get(enc).finish()),

  setPipeline: (pass, pipeline) => { get(pass).setPipeline(get(pipeline)); return 0; },
  setBindGroup: (pass, index, group, count, offsetsPtr) => {
    get(pass).setBindGroup(index, get(group), array(offsetsPtr, count, 4, u32));
    return 0;
  },
  dispatch: (pass, x, y, z) => { get(pass).dispatchWorkgroups(x, y, z); return 0; },
  dispatchIndirect: (pass, buffer, offset) => { get(pass).dispatchWorkgroupsIndirect(get(buffer), offset); return 0; },
  pushDebugGroup: (pass, data, length) => {
    get(pass).pushDebugGroup(length === U32_UNDEFINED ? cString(data) : decoder.decode(heap().subarray(data, data + length)));
    return 0;
  },
  popDebugGroup: pass => { get(pass).popDebugGroup(); return 0; },
  end: pass => { get(pass).end(); return 0; },

  setVertexBufferHandle: (pass, slot, buffer) => { passState(pass).vertex = buffer; return 0; },
  setIndexBufferHandle: (pass, buffer, format) => { Object.assign(passState(pass), { index: buffer, format }); return 0; },
  draw: (pass, vertices, instances, first, firstInstance) => { get(pass).draw(vertices, instances, first, firstInstance); return 0; },
  drawIndexed: (pass, indices, instances, first, base, firstInstance) => {
    get(pass).drawIndexed(indices, instances, first, base | 0, firstInstance);
    return 0;
  },
  drawIndirect: (pass, buffer, offset) => { get(pass).drawIndirect(get(buffer), offset); return 0; },
  drawIndexedIndirect: (pass, buffer, offset) => { get(pass).drawIndexedIndirect(get(buffer), offset); return 0; },
  setScissorRect: (pass, x, y, width, height) => { get(pass).setScissorRect(x, y, width, height); return 0; },
  setBlendConstant: (pass, color) => {
    get(pass).setBlendConstant({ r: f64(at(color, 'WGPUColor.r')), g: f64(at(color, 'WGPUColor.g')), b: f64(at(color, 'WGPUColor.b')), a: f64(at(color, 'WGPUColor.a')) });
    return 0;
  },
  beginOcclusionQuery: (pass, index) => { get(pass).beginOcclusionQuery(index); return 0; },
  endOcclusionQuery: pass => { get(pass).endOcclusionQuery(); return 0; },

  unmap: buffer => { get(buffer).unmap(); return 0; },
  submit: (queue, count, list) => { get(queue).submit(array(list, count, 4, q => get(ptr(q)))); return 0; },
  // 1 = WGPUQueueWorkDoneStatus_Success, 3 = _Error.
  onSubmittedWorkDone: (queue, op) => {
    get(queue).onSubmittedWorkDone().then(
      () => completions.push([op, 1, 0]),
      () => completions.push([op, 3, 0]));
    return 0;
  },
  writeTexture: (queue, dst, data, size, layout, size3) => {
    get(queue).writeTexture(textureCopy(dst), heap().subarray(data, data + size), bufferLayout(layout), extent(size3));
    return 0;
  },

  createSurface: () => add({ context: canvas.getContext('webgpu') }),
  preferredFormat: () => V.TextureFormat[navigator.gpu.getPreferredCanvasFormat()],
  configure: (s, p) => {
    const width = u32(at(p, 'WGPUSurfaceConfiguration.width'));
    const height = u32(at(p, 'WGPUSurfaceConfiguration.height'));
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
    }
    get(s).context.configure({
      device,
      format: E.TextureFormat[u32(at(p, 'WGPUSurfaceConfiguration.format'))],
      usage: u64(at(p, 'WGPUSurfaceConfiguration.usage')),
      alphaMode: 'opaque',
      viewFormats: array(ptr(at(p, 'WGPUSurfaceConfiguration.viewFormats')), u32(at(p, 'WGPUSurfaceConfiguration.viewFormatCount')), 4,
        q => E.TextureFormat[u32(q)]),
    });
    return 0;
  },
  unconfigure: s => { get(s).context.unconfigure(); return 0; },
  currentTexture: s => add(get(s).context.getCurrentTexture()),

  release,
};

const doubleCalls = {
  mapAsync: (buffer, mode, offset, size, op) => {
    get(buffer).mapAsync(mode, offset, size < 0 ? undefined : size).then(
      () => completions.push([op, 1, 0]),
      () => completions.push([op, 3, 0]));
    return 0;
  },
  getMappedRange: (buffer, offset, size, into) => {
    heap().set(new Uint8Array(get(buffer).getMappedRange(offset, size)), into);
    return 0;
  },
  writeBuffer: (queue, buffer, offset, data, size) => {
    get(queue).writeBuffer(get(buffer), offset, heap(), data, size);
    return 0;
  },
  clearBuffer: (enc, buffer, offset, size) => { get(enc).clearBuffer(get(buffer), offset, size < 0 ? undefined : size); return 0; },
  copyBufferToBuffer: (enc, srcOffset, dstOffset, size) => {
    const { src, dst } = passState(enc);
    get(enc).copyBufferToBuffer(get(src), srcOffset, get(dst), dstOffset, size < 0 ? undefined : size);
    return 0;
  },
  resolveQuerySet: (enc, first, count, offset) => {
    const { set, dst } = passState(enc);
    get(enc).resolveQuerySet(get(set), first, count, get(dst), offset);
    return 0;
  },
  setVertexBuffer: (pass, slot, offset, size) => {
    get(pass).setVertexBuffer(slot, get(passState(pass).vertex), offset, size < 0 ? undefined : size);
    return 0;
  },
  setIndexBuffer: (pass, unused, offset, size) => {
    const { index, format } = passState(pass);
    get(pass).setIndexBuffer(get(index), E.IndexFormat[format], offset, size < 0 ? undefined : size);
    return 0;
  },
  setViewport: (pass, x, y, width, height) => { passState(pass).viewport = [x, y, width, height]; return 0; },
  setViewportDepth: (pass, min, max) => { get(pass).setViewport(...passState(pass).viewport, min, max); return 0; },
};

globalThis.OpenIgiWebGpu = {
  /** Called by the page with what it acquired before starting the runtime. */
  init(options) {
    ({ adapter, device, canvas } = options);
    adapterHandle = add(adapter);
    deviceHandle = add(device);
    queueHandle = add(device.queue);
  },
  attach(runtime) { heap = runtime.heap; },
  field(namePointer, offset) { offsets.set(cString(namePointer), offset); },
  call(namePointer, a, b, c, d, e, f) {
    const fn = calls[name(namePointer)];
    if (!fn) throw new Error(`The WebGPU bridge has no ${name(namePointer)}.`);
    // A uint32_t argument arrives as a signed number; every one is read back as the unsigned it was.
    return fn(a >>> 0, b >>> 0, c >>> 0, d >>> 0, e >>> 0, f >>> 0) ?? 0;
  },
  callD(namePointer, a, b, c, d, e) {
    const fn = doubleCalls[name(namePointer)];
    if (!fn) throw new Error(`The WebGPU bridge has no ${name(namePointer)}.`);
    return fn(a >>> 0, b, c, d, e) ?? 0;
  },
  pop(out) {
    const next = completions.shift();
    if (!next) return 0;
    next.forEach((value, i) => setU32(out + i * 4, value));
    return 1;
  },
};
