struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct _Array_std140_matrixx3Cfloatx2C4x2C4x3E4_0
{
    @align(16) data_1 : array<_MatrixStorage_float4x4_ColMajorstd140_0, i32(4)>,
};

struct FlashlightParams_std140_0
{
    @align(16) position_0 : vec4<f32>,
    @align(16) axis_0 : vec4<f32>,
    @align(16) up_0 : vec4<f32>,
    @align(16) colour_0 : vec4<f32>,
    @align(16) cone_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

struct FogParams_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) prevWorldToView_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) cascade_0 : _Array_std140_matrixx3Cfloatx2C4x2C4x3E4_0,
    @align(16) cascadeSplits_0 : vec4<f32>,
    @align(16) grid_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) prevUnproject_0 : vec4<f32>,
    @align(16) zParams_0 : vec4<f32>,
    @align(16) medium_0 : vec4<f32>,
    @align(16) march_0 : vec4<f32>,
    @align(16) albedo_0 : vec4<f32>,
    @align(16) sun_0 : vec4<f32>,
    @align(16) sunColour_0 : vec4<f32>,
    @align(16) ambient_0 : vec4<f32>,
    @align(16) camera_0 : vec4<f32>,
    @align(16) jitter_0 : vec4<f32>,
    @align(16) cameraWorld_0 : vec4<f32>,
    @align(16) shelter0_0 : vec4<f32>,
    @align(16) shelter1_0 : vec4<f32>,
    @align(16) shelter2_0 : vec4<f32>,
    @align(16) volumeCentre_0 : array<vec4<f32>, i32(8)>,
    @align(16) volumeExtent_0 : array<vec4<f32>, i32(8)>,
    @align(16) lampPosition_0 : array<vec4<f32>, i32(160)>,
    @align(16) lampColour_0 : array<vec4<f32>, i32(160)>,
    @align(16) flashlight_0 : FlashlightParams_std140_0,
};

@binding(9) @group(0) var<uniform> p_0 : FogParams_std140_0;
@binding(7) @group(0) var uShelterMap_0 : texture_2d<f32>;

@binding(0) @group(0) var uShadowMap_0 : texture_depth_2d_array;

@binding(2) @group(0) var uSkyIrradiance_0 : texture_2d<f32>;

@binding(4) @group(0) var uHistory_0 : texture_3d<f32>;

@binding(5) @group(0) var uHistorySampler_0 : sampler;

@binding(6) @group(0) var uScattering_0 : texture_storage_3d<rgba16float, write>;

fn SliceViewDepth_0( slice_0 : f32) -> f32
{
    return (exp2(slice_0 / p_0.zParams_0.z) - p_0.zParams_0.y) / p_0.zParams_0.x;
}

fn ViewPositionAt_0( uv_0 : vec2<f32>,  viewZ_0 : f32) -> vec3<f32>
{
    var ndc_0 : vec2<f32> = uv_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / p_0.unproject_0.x, - ndc_0.y * viewZ_0 / p_0.unproject_0.y, viewZ_0);
}

fn Indoors_0( world_0 : vec3<f32>) -> bool
{
    if((p_0.shelter1_0.w) < 0.5f)
    {
        return false;
    }
    var absolute_0 : vec3<f32> = world_0 + p_0.cameraWorld_0.xyz;
    var _S1 : vec2<i32> = vec2<i32>(floor((absolute_0.xy - p_0.shelter0_0.xy) * vec2<f32>(p_0.shelter0_0.z)));
    var _S2 : i32 = _S1.x;
    var _S3 : bool;
    if(_S2 < i32(0))
    {
        _S3 = true;
    }
    else
    {
        _S3 = (_S1.y) < i32(0);
    }
    if(_S3)
    {
        _S3 = true;
    }
    else
    {
        _S3 = _S2 >= i32(p_0.shelter2_0.x);
    }
    if(_S3)
    {
        _S3 = true;
    }
    else
    {
        _S3 = (_S1.y) >= i32(p_0.shelter2_0.y);
    }
    if(_S3)
    {
        return false;
    }
    var _S4 : vec3<i32> = vec3<i32>(_S1, i32(0));
    var texel_0 : vec4<f32> = (textureLoad((uShelterMap_0), ((_S4)).xy, ((_S4)).z));
    var packed_0 : f32 = floor(texel_0.z * 255.0f + 0.5f) + floor(texel_0.y * 255.0f + 0.5f) * 256.0f;
    if(packed_0 < 0.5f)
    {
        return false;
    }
    var surface_0 : f32 = p_0.shelter0_0.w + (packed_0 - 1.0f) * p_0.shelter1_0.x;
    var _S5 : f32 = absolute_0.z;
    if(surface_0 > (_S5 + p_0.shelter1_0.y))
    {
        _S3 = surface_0 < (_S5 + p_0.shelter1_0.z);
    }
    else
    {
        _S3 = false;
    }
    return _S3;
}

fn VolumeScaleAt_0( world_1 : vec3<f32>) -> f32
{
    var scale_0 : f32 = 1.0f;
    var i_0 : i32 = i32(0);
    for(;;)
    {
        if(i_0 < i32(8))
        {
        }
        else
        {
            break;
        }
        if((p_0.volumeExtent_0[i_0].x) <= 0.0f)
        {
            i_0 = i_0 + i32(1);
            continue;
        }
        var delta_0 : vec3<f32> = abs(world_1 - p_0.volumeCentre_0[i_0].xyz);
        var reach_0 : f32;
        if((p_0.volumeCentre_0[i_0].w) > 0.5f)
        {
            reach_0 = length(delta_0) / max(p_0.volumeExtent_0[i_0].x, 0.00100000004749745f);
        }
        else
        {
            reach_0 = max(delta_0.x / max(p_0.volumeExtent_0[i_0].x, 0.00100000004749745f), max(delta_0.y / max(p_0.volumeExtent_0[i_0].y, 0.00100000004749745f), delta_0.z / max(p_0.volumeExtent_0[i_0].z, 0.00100000004749745f)));
        }
        scale_0 = mix(scale_0, p_0.volumeExtent_0[i_0].w, 1.0f - smoothstep(0.69999998807907104f, 1.0f, reach_0));
        i_0 = i_0 + i32(1);
    }
    return scale_0;
}

fn DensityAt_0( world_2 : vec3<f32>) -> f32
{
    if(Indoors_0(world_2))
    {
        return 0.0f;
    }
    return max(p_0.medium_0.x * mix(exp(- max(world_2.z - p_0.medium_0.z, 0.0f) * p_0.medium_0.y), 1.0f, p_0.march_0.x) * VolumeScaleAt_0(world_2), 0.0f);
}

fn PhaseHG_0( cosTheta_0 : f32,  g_0 : f32) -> f32
{
    var gg_0 : f32 = g_0 * g_0;
    return (1.0f - gg_0) / (12.56637096405029297f * pow(max(1.0f + gg_0 - 2.0f * g_0 * cosTheta_0, 0.00009999999747379f), 1.5f));
}

fn Phase_0( cosTheta_1 : f32) -> f32
{
    return mix(PhaseHG_0(cosTheta_1, p_0.medium_0.w), 0.07957746833562851f, p_0.march_0.y);
}

fn SunVisibility_0( viewPos_0 : vec3<f32>,  world_3 : vec3<f32>) -> f32
{
    var count_0 : i32 = i32(p_0.albedo_0.w + 0.5f);
    if(count_0 <= i32(0))
    {
        return 1.0f;
    }
    var last_0 : f32 = p_0.cascadeSplits_0[clamp(count_0 - i32(1), i32(0), i32(3))];
    var beyond_0 : f32;
    if((p_0.cascadeSplits_0[clamp(count_0 - i32(1), i32(0), i32(3))]) > 0.0f)
    {
        beyond_0 = clamp((viewPos_0.z - last_0 * 0.55000001192092896f) / max(last_0 * 0.44999998807907104f, 0.00100000004749745f), 0.0f, 1.0f);
    }
    else
    {
        beyond_0 = 1.0f;
    }
    if(beyond_0 >= 1.0f)
    {
        return 1.0f;
    }
    var i_1 : i32 = i32(0);
    for(;;)
    {
        if(i_1 < i32(4))
        {
        }
        else
        {
            break;
        }
        var _S6 : bool;
        if(i_1 >= count_0)
        {
            _S6 = true;
        }
        else
        {
            _S6 = (viewPos_0.z) > (p_0.cascadeSplits_0[i_1]);
        }
        if(_S6)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var clip_0 : vec4<f32> = (((vec4<f32>(world_3, 1.0f)) * (mat4x4<f32>(p_0.cascade_0.data_1[i_1].data_0[i32(0)][i32(0)], p_0.cascade_0.data_1[i_1].data_0[i32(1)][i32(0)], p_0.cascade_0.data_1[i_1].data_0[i32(2)][i32(0)], p_0.cascade_0.data_1[i_1].data_0[i32(3)][i32(0)], p_0.cascade_0.data_1[i_1].data_0[i32(0)][i32(1)], p_0.cascade_0.data_1[i_1].data_0[i32(1)][i32(1)], p_0.cascade_0.data_1[i_1].data_0[i32(2)][i32(1)], p_0.cascade_0.data_1[i_1].data_0[i32(3)][i32(1)], p_0.cascade_0.data_1[i_1].data_0[i32(0)][i32(2)], p_0.cascade_0.data_1[i_1].data_0[i32(1)][i32(2)], p_0.cascade_0.data_1[i_1].data_0[i32(2)][i32(2)], p_0.cascade_0.data_1[i_1].data_0[i32(3)][i32(2)], p_0.cascade_0.data_1[i_1].data_0[i32(0)][i32(3)], p_0.cascade_0.data_1[i_1].data_0[i32(1)][i32(3)], p_0.cascade_0.data_1[i_1].data_0[i32(2)][i32(3)], p_0.cascade_0.data_1[i_1].data_0[i32(3)][i32(3)]))));
        var ndc_1 : vec3<f32> = clip_0.xyz / vec3<f32>(clip_0.w);
        if((any(((abs(ndc_1.xy)) > vec2<f32>(1.0f, 1.0f)))))
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var uv_1 : vec2<f32> = vec2<f32>(ndc_1.x * 0.5f + 0.5f, 0.5f - ndc_1.y * 0.5f);
        var _S7 : f32 = ndc_1.z * 0.5f + 0.5f - p_0.sunColour_0.w;
        var mapWidth_0 : u32;
        var mapHeight_0 : u32;
        var mapLayers_0 : u32;
        {var dim = textureDimensions((uShadowMap_0));((mapWidth_0)) = dim.x;((mapHeight_0)) = dim.y;((mapLayers_0)) = textureNumLayers((uShadowMap_0));};
        var mapSize_0 : vec2<f32> = vec2<f32>(f32(mapWidth_0), f32(mapHeight_0));
        var texel_1 : vec2<f32> = uv_1 * mapSize_0 - vec2<f32>(0.5f);
        var _S8 : vec2<i32> = vec2<i32>(floor(texel_1));
        var _S9 : vec2<f32> = fract(texel_1);
        var y_0 : i32 = i32(0);
        var sum_0 : f32 = 0.0f;
        for(;;)
        {
            if(y_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            var x_0 : i32 = i32(0);
            for(;;)
            {
                if(x_0 < i32(2))
                {
                }
                else
                {
                    break;
                }
                var _S10 : vec4<i32> = vec4<i32>(clamp(_S8 + vec2<i32>(x_0, y_0), vec2<i32>(i32(0), i32(0)), vec2<i32>(mapSize_0) - vec2<i32>(i32(1))), i_1, i32(0));
                var lit_0 : f32;
                if(_S7 <= (textureLoad((uShadowMap_0), ((_S10)).xy, i32(((_S10)).z), ((_S10)).w)))
                {
                    lit_0 = 1.0f;
                }
                else
                {
                    lit_0 = 0.0f;
                }
                var _S11 : f32 = _S9.x;
                var _S12 : f32 = _S9.y;
                var sum_1 : f32 = sum_0 + lit_0 * mix(1.0f - _S11, _S11, f32(x_0)) * mix(1.0f - _S12, _S12, f32(y_0));
                x_0 = x_0 + i32(1);
                sum_0 = sum_1;
            }
            y_0 = y_0 + i32(1);
        }
        return mix(sum_0, 1.0f, beyond_0);
    }
    return 1.0f;
}

fn SkyIrradianceAt_0( direction_0 : vec3<f32>) -> vec3<f32>
{
    var n_0 : vec4<f32> = vec4<f32>(direction_0, 1.0f);
    const _S13 : vec3<i32> = vec3<i32>(i32(0), i32(0), i32(0));
    const _S14 : vec3<i32> = vec3<i32>(i32(1), i32(0), i32(0));
    const _S15 : vec3<i32> = vec3<i32>(i32(2), i32(0), i32(0));
    var q_0 : vec4<f32> = n_0.xyzz * n_0.yzzx;
    const _S16 : vec3<i32> = vec3<i32>(i32(3), i32(0), i32(0));
    const _S17 : vec3<i32> = vec3<i32>(i32(4), i32(0), i32(0));
    const _S18 : vec3<i32> = vec3<i32>(i32(5), i32(0), i32(0));
    const _S19 : vec3<i32> = vec3<i32>(i32(6), i32(0), i32(0));
    var _S20 : f32 = n_0.x;
    var _S21 : f32 = n_0.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot((textureLoad((uSkyIrradiance_0), ((_S13)).xy, ((_S13)).z)), n_0), dot((textureLoad((uSkyIrradiance_0), ((_S14)).xy, ((_S14)).z)), n_0), dot((textureLoad((uSkyIrradiance_0), ((_S15)).xy, ((_S15)).z)), n_0)) + vec3<f32>(dot((textureLoad((uSkyIrradiance_0), ((_S16)).xy, ((_S16)).z)), q_0), dot((textureLoad((uSkyIrradiance_0), ((_S17)).xy, ((_S17)).z)), q_0), dot((textureLoad((uSkyIrradiance_0), ((_S18)).xy, ((_S18)).z)), q_0)) + (textureLoad((uSkyIrradiance_0), ((_S19)).xy, ((_S19)).z)).xyz * vec3<f32>((_S20 * _S20 - _S21 * _S21)));
}

fn ViewDepthSlice_0( viewZ_1 : f32) -> f32
{
    return p_0.zParams_0.z * log2(max(p_0.zParams_0.x * viewZ_1 + p_0.zParams_0.y, 9.99999997475242708e-07f));
}

fn HistoryCoordinate_0( world_4 : vec3<f32>,  uvw_0 : ptr<function, vec3<f32>>) -> bool
{
    const _S22 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*uvw_0) = _S22;
    var prevView_0 : vec3<f32> = (((vec4<f32>(world_4 + p_0.camera_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).xyz;
    var _S23 : f32 = prevView_0.z;
    if(_S23 <= (p_0.prevUnproject_0.z))
    {
        return false;
    }
    var _S24 : vec2<f32> = vec2<f32>(0.5f);
    var _S25 : vec3<f32> = vec3<f32>(vec2<f32>(prevView_0.x * p_0.prevUnproject_0.x / _S23, - prevView_0.y * p_0.prevUnproject_0.y / _S23) * _S24 + _S24, ViewDepthSlice_0(_S23) * p_0.zParams_0.w);
    (*uvw_0) = _S25;
    var _S26 : bool;
    if((all((_S25 >= _S22))))
    {
        _S26 = (all(((*uvw_0) <= vec3<f32>(1.0f, 1.0f, 1.0f))));
    }
    else
    {
        _S26 = false;
    }
    return _S26;
}

@compute
@workgroup_size(4, 4, 4)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S27 : vec3<i32> = vec3<i32>(id_0);
    if((any((_S27 >= vec3<i32>(p_0.grid_0.xyz + vec3<f32>(0.5f))))))
    {
        return;
    }
    var _S28 : vec2<f32> = vec2<f32>(_S27.xy);
    var _S29 : f32 = f32(_S27.z);
    var viewPos_1 : vec3<f32> = ViewPositionAt_0((_S28 + p_0.jitter_0.xy) / p_0.grid_0.xy, SliceViewDepth_0(_S29 + p_0.jitter_0.z));
    var world_5 : vec3<f32> = (((vec4<f32>(viewPos_1, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
    var density_0 : f32 = DensityAt_0(world_5);
    var rayView_0 : vec3<f32> = normalize(viewPos_1);
    const _S30 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var scattering_0 : vec3<f32>;
    if(density_0 > 0.0f)
    {
        var light_0 : vec3<f32> = p_0.sunColour_0.xyz * vec3<f32>(p_0.march_0.z) * vec3<f32>(Phase_0(dot(rayView_0, p_0.sun_0.xyz))) * vec3<f32>(SunVisibility_0(viewPos_1, world_5));
        var rayWorld_0 : vec3<f32> = (((vec4<f32>(rayView_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
        if((p_0.ambient_0.w) > 0.5f)
        {
            scattering_0 = SkyIrradianceAt_0((vec3<f32>(0) - rayWorld_0) * vec3<f32>(p_0.medium_0.w));
        }
        else
        {
            scattering_0 = p_0.ambient_0.xyz;
        }
        var light_1 : vec3<f32> = light_0 + scattering_0 * vec3<f32>(p_0.march_0.w);
        var _S31 : i32 = min(i32(round(p_0.camera_0.w)), i32(160));
        var i_2 : i32 = i32(0);
        var light_2 : vec3<f32> = light_1;
        for(;;)
        {
            if(i_2 < _S31)
            {
            }
            else
            {
                break;
            }
            var _S32 : vec3<f32> = p_0.lampPosition_0[i_2].xyz;
            var toLight_0 : vec3<f32> = _S32 - viewPos_1;
            var distanceSquared_0 : f32 = dot(toLight_0, toLight_0);
            var reach_1 : f32 = p_0.lampPosition_0[i_2].w;
            var _S33 : bool;
            if(reach_1 <= 0.0f)
            {
                _S33 = true;
            }
            else
            {
                _S33 = distanceSquared_0 >= (reach_1 * reach_1);
            }
            if(_S33)
            {
                i_2 = i_2 + i32(1);
                continue;
            }
            if(Indoors_0((((vec4<f32>(_S32, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz - vec3<f32>(0.0f, 0.0f, 1.0f)))
            {
                i_2 = i_2 + i32(1);
                continue;
            }
            var ratio_0 : f32 = distanceSquared_0 / (reach_1 * reach_1);
            var window_0 : f32 = saturate(1.0f - ratio_0 * ratio_0);
            light_2 = light_2 + p_0.lampColour_0[i_2].xyz * vec3<f32>(p_0.lampColour_0[i_2].w) * vec3<f32>((window_0 * window_0 / (distanceSquared_0 + 1.0f))) * vec3<f32>(Phase_0(dot(rayView_0, toLight_0 / vec3<f32>(sqrt(max(distanceSquared_0, 9.99999993922529029e-09f))))));
            i_2 = i_2 + i32(1);
        }
        scattering_0 = light_2 * p_0.albedo_0.xyz * vec3<f32>(density_0);
    }
    else
    {
        scattering_0 = _S30;
    }
    var result_0 : vec4<f32> = vec4<f32>(scattering_0, density_0);
    var result_1 : vec4<f32>;
    if((p_0.jitter_0.w) > 0.5f)
    {
        var historyUvw_0 : vec3<f32>;
        var _S34 : bool = HistoryCoordinate_0((((vec4<f32>(ViewPositionAt_0((_S28 + vec2<f32>(0.5f)) / p_0.grid_0.xy, SliceViewDepth_0(_S29 + 0.5f)), 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz, &(historyUvw_0));
        if(_S34)
        {
            result_1 = mix(result_0, (textureSampleLevel((uHistory_0), (uHistorySampler_0), (historyUvw_0), (0.0f))), vec4<f32>(p_0.grid_0.w));
        }
        else
        {
            result_1 = result_0;
        }
    }
    else
    {
        result_1 = result_0;
    }
    textureStore((uScattering_0), (vec3<u32>(_S27)), (max(result_1, vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f))));
    return;
}

