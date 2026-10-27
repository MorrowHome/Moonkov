#ifndef MOON_STOCHASTIC_SHADOWS_INCLUDED
#define MOON_STOCHASTIC_SHADOWS_INCLUDED

// URP 17.5 extension point, included BEFORE Lighting.hlsl. The stock shadow
// library stays intact; only participating forward materials redirect lighting.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

#if defined(_MOON_STOCHASTIC_SHADOWS)
float4 _MoonStochasticShadowParams; // radius in atlas texels, pixel size, shadow taps, light selection probability
static float2 moonShadowPixel = 0;

void MoonSetShadowPixel(float2 pixel) { moonShadowPixel = floor(pixel / max(_MoonStochasticShadowParams.y, 1)); }

float2 MoonShadowOffset(float seed)
{
    // Fixed screen-space seed: no frame-to-frame random flicker at rest.
    float3 p = frac(float3(moonShadowPixel, seed) * float3(.1031, .1030, .0973));
    p += dot(p, p.yzx + 33.33);
    return (frac((p.xx + p.yz) * p.zy) * 2 - 1) * _MoonStochasticShadowParams.x;
}

half MoonSampleShadow(TEXTURE2D_SHADOW_PARAM(map, mapSampler), float4 coord,
    ShadowSamplingData sampling, half4 parameters, bool perspective, float seed)
{
    if (perspective) coord.xyz /= coord.w;
    if (BEYOND_SHADOW_FAR(coord)) return 1;
    float2 offset = MoonShadowOffset(seed) * sampling.shadowmapSize.xy;
    // Hard lights retain one unjittered comparison. No fake grain in lit/shadowed interiors.
    if (parameters.y <= SOFT_SHADOW_QUALITY_OFF) offset = 0;
    half visibility = SAMPLE_TEXTURE2D_SHADOW(map, mapSampler, float3(coord.xy + offset, coord.z));
    UNITY_BRANCH if (_MoonStochasticShadowParams.z > 1.5 && parameters.y > SOFT_SHADOW_QUALITY_OFF)
        visibility = (visibility + SAMPLE_TEXTURE2D_SHADOW(map, mapSampler, float3(coord.xy - offset, coord.z))) * .5h;
    return LerpWhiteTo(visibility, parameters.x);
}

half MoonStochasticMainShadow(float4 coord, float3 positionWS, half4 mask, half4 channels)
{
    // Screen-space shadows have already been filtered elsewhere and cannot save
    // source sampling here. Preserve that path exactly.
    #if !defined(MAIN_LIGHT_CALCULATE_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        return MainLightShadow(coord, positionWS, mask, channels);
    #else
        half4 parameters = GetMainLightShadowParams();
        half realtime = MoonSampleShadow(TEXTURE2D_SHADOW_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare),
            coord, GetMainLightShadowSamplingData(), parameters, false, 0);
        half baked = 1;
        #if defined(CALCULATE_BAKED_SHADOWS)
            baked = BakedShadow(mask, channels, parameters);
        #endif
        return MixRealtimeAndBakedShadows(realtime, baked, GetMainLightShadowFade(positionWS));
    #endif
}

half MoonStochasticAdditionalShadow(int index, float3 positionWS, half3 direction, half4 mask, half4 channels)
{
    #if !defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
        return AdditionalLightShadow(index, positionWS, direction, mask, channels);
    #else
        half4 parameters = GetAdditionalLightShadowParams(index);
        int slice = parameters.w;
        half realtime = 1;
        UNITY_BRANCH if (slice >= 0)
        {
            if (parameters.z > .5h) slice += CubeMapFaceID(-direction);
            #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
                float4 coord = mul(_AdditionalLightsWorldToShadow_SSBO[slice], float4(positionWS, 1));
            #else
                float4 coord = mul(_AdditionalLightsWorldToShadow[slice], float4(positionWS, 1));
            #endif
            realtime = MoonSampleShadow(TEXTURE2D_SHADOW_ARGS(_AdditionalLightsShadowmapTexture, sampler_LinearClampCompare),
                coord, GetAdditionalLightShadowSamplingData(index), parameters, true, index + 1);
        }
        half baked = 1;
        #if defined(CALCULATE_BAKED_SHADOWS)
            baked = BakedShadow(mask, channels, parameters);
        #endif
        return MixRealtimeAndBakedShadows(realtime, baked, GetAdditionalLightShadowFade(positionWS));
    #endif
}

#define MainLightShadow MoonStochasticMainShadow
#define AdditionalLightShadow MoonStochasticAdditionalShadow
#else
void MoonSetShadowPixel(float2 pixel) { }
#endif

// Build URP's light accessors with our low-sample shadow functions first.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#if defined(_MOON_STOCHASTIC_SHADOWS)
#undef MainLightShadow
#undef AdditionalLightShadow

bool MoonSelectLight(float seed)
{
    // Each light gets an independent, single-pixel Bernoulli sample. Material
    // textures, GI, emission and fog are evaluated for EVERY surface pixel.
    // Integer avalanche hash avoids the visible diagonal pattern of ordered noise.
    uint state = (uint)moonShadowPixel.x * 1973u + (uint)moonShadowPixel.y * 9277u + (uint)seed * 26699u + 911u;
    state = (state ^ (state >> 16)) * 0x7feb352du;
    state = (state ^ (state >> 15)) * 0x846ca68bu;
    state ^= state >> 16;
    float noise = (state & 0x00ffffffu) * (1.0 / 16777216.0);
    return noise < _MoonStochasticShadowParams.w;
}

Light MoonSampleMainLight() { return GetMainLight(); }
Light MoonSampleMainLight(float4 coord) { return GetMainLight(coord); }
Light MoonSampleMainLight(float4 coord, float3 position, half4 mask) { return GetMainLight(coord, position, mask); }
Light MoonSampleMainLight(InputData data, half4 mask, AmbientOcclusionFactor ao)
{
    UNITY_BRANCH if (_MoonStochasticShadowParams.w >= 1) return GetMainLight(data, mask, ao);
    // Subtractive GI needs a deterministic input. Main-light cookies use
    // implicit texture derivatives, so also preserve that path outside roulette.
    #if (defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK)) || defined(_LIGHT_COOKIES)
        return GetMainLight(data, mask, ao);
    #else
        UNITY_BRANCH if (!MoonSelectLight(0))
        {
            Light light = GetMainLight();
            light.color = 0;
            return light;
        }
        Light light = GetMainLight(data, mask, ao);
        light.color /= max(_MoonStochasticShadowParams.w, .25);
        return light;
    #endif
}

Light MoonSampleAdditionalLight(uint index, float3 position) { return GetAdditionalLight(index, position); }
Light MoonSampleAdditionalLight(uint index, float3 position, half4 mask) { return GetAdditionalLight(index, position, mask); }
Light MoonSampleAdditionalLight(uint index, InputData data, half4 mask, AmbientOcclusionFactor ao)
{
    UNITY_BRANCH if (_MoonStochasticShadowParams.w >= 1) return GetAdditionalLight(index, data, mask, ao);
    // Reject before shadow/cookie fetches. No implicit-derivative material
    // texture sampling is hidden in this divergent branch (cookies use LOD 0).
    UNITY_BRANCH if (!MoonSelectLight(index + 1))
    {
        Light light = (Light)0;
        light.direction = half3(0,1,0);
        return light;
    }
    Light light = GetAdditionalLight(index, data, mask, ao);
    light.color /= max(_MoonStochasticShadowParams.w, .25);
    return light;
}

#define GetMainLight MoonSampleMainLight
#define GetAdditionalLight MoonSampleAdditionalLight
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
// Do not leak remapping into other includes or passes.
#if defined(_MOON_STOCHASTIC_SHADOWS)
#undef GetMainLight
#undef GetAdditionalLight
#endif
#endif
