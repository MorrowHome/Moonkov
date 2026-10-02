#ifndef MOON_ENVIRONMENT_INPUT_INCLUDED
#define MOON_ENVIRONMENT_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
// Identical material layout in every pass. Property names preserve the source materials.
CBUFFER_START(UnityPerMaterial)
float4 _MainTex_ST, _Color, _GroundColor, _GeologyRect;
float4 _FreshRockTint, _WeatheredRockTint, _GroundPlane;
float _DetailStrength, _LunarLighting, _LunarWeight, _IndirectStrength;
float _PhaseEnabled, _PhaseSlope, _ShadowHidingAmplitude, _ShadowHidingWidth;
float _CoherentAmplitude, _CoherentWidth, _GeologyStrength, _EjectaBrightness;
float _ScanColorWeight, _BlendHeight, _RockVariation, _RockFreshness;
CBUFFER_END
sampler2D _MainTex, _BumpMap, _Roughness, _HeightMap, _MicroTex, _MicroNormal;
sampler2D _GroundTex, _GeologyMap, _TerrainHolesTexture;
#include "MoonPhase.hlsl"
#include "MoonSurface.hlsl"
struct SurfaceOutputStandard
{
    half3 Albedo, Normal;
    half Metallic, Smoothness, Occlusion, Alpha;
};
struct Input { float3 worldPos, viewDir; float2 uv_MainTex, uv_TerrainHolesTexture; };
float2 MoonParallaxOffset(float height, float amount, float3 view)
{
    view = normalize(view);
    return (height * amount - amount * 0.5) * view.xy / max(view.z + 0.42, 0.01);
}
struct MoonAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct MoonVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half4 tangentWS : TEXCOORD2;
    float2 uv : TEXCOORD3;
    float4 shadowCoord : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
MoonVaryings MoonVertex(MoonAttributes input)
{
    MoonVaryings output = (MoonVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
#if defined(MOON_TERRAIN)
    // Unity's non-instanced Terrain mesh has no usable tangent attribute.
    output.tangentWS = half4(normalize(cross(output.normalWS, float3(0,0,1))), -1);
#else
    output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
#endif
    output.uv = input.uv;
    output.shadowCoord = GetShadowCoord(position);
    return output;
}
void MoonClipHoles(float2 uv)
{
#if defined(MOON_TERRAIN)
    clip(tex2D(_TerrainHolesTexture, uv).r - 0.5);
#endif
}
half3 MoonLight(SurfaceOutputStandard surface, half3 normal, half3 view, Light light)
{
    half mu0 = saturate(dot(normal, light.direction));
    half mu = saturate(dot(normal, view));
    half disk = lerp(mu0, 2 * mu0 / max(mu0 + mu, 0.05h), _LunarWeight);
    return surface.Albedo * light.color * disk * MoonPhaseReflectance(light.direction, view)
        * light.distanceAttenuation * light.shadowAttenuation;
}
float4 MoonFragmentShadowCoord(MoonVaryings input)
{
#if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
    // Terrain triangles can cross a cascade boundary. Select the cascade from
    // the actual fragment position, rather than interpolating atlas coordinates.
    return TransformWorldToShadowCoord(input.positionWS);
#else
    return input.shadowCoord;
#endif
}
Light MoonMainLight(MoonVaryings input)
{
    Light light = GetMainLight(MoonFragmentShadowCoord(input), input.positionWS, half4(1,1,1,1));
#if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
    int cascade = (int)ComputeCascadeIndex(input.positionWS);
    // The last cascade already fades with URP's shadow distance. Only blend real
    // neighbours, never the identity matrix slot outside the shadow distance.
    UNITY_BRANCH if (cascade < 3)
    {
        float4 spheres[4] = { _CascadeShadowSplitSpheres0, _CascadeShadowSplitSpheres1,
                             _CascadeShadowSplitSpheres2, _CascadeShadowSplitSpheres3 };
        float radius2 = _CascadeShadowSplitSphereRadii[cascade];
        float nextRadius2 = _CascadeShadowSplitSphereRadii[cascade + 1];
        float3 delta = input.positionWS - spheres[cascade].xyz;
        // Blend over the outer 12% of the sphere radius, with zero endpoint slope.
        float blend = smoothstep(0.88 * 0.88, 1.0, dot(delta,delta) / max(radius2,1e-5));
        float3 nextDelta = input.positionWS - spheres[cascade + 1].xyz;
        UNITY_BRANCH if (blend > 0 && nextRadius2 > 0 && dot(nextDelta,nextDelta) < nextRadius2)
        {
            float4 nextCoord = float4(mul(_MainLightWorldToShadow[cascade + 1],
                float4(input.positionWS,1)).xyz,0);
            Light next = GetMainLight(nextCoord, input.positionWS, half4(1,1,1,1));
            light.shadowAttenuation = lerp(light.shadowAttenuation, next.shadowAttenuation, blend);
        }
    }
#endif
    return light;
}
float3 _LightDirection, _LightPosition;
MoonVaryings MoonShadowVertex(MoonAttributes input)
{
    MoonVaryings output = MoonVertex(input);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirection = normalize(_LightPosition - output.positionWS);
#else
    float3 lightDirection = _LightDirection;
#endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, lightDirection));
    output.positionCS = ApplyShadowClamping(output.positionCS);
    return output;
}
half4 MoonDepth(MoonVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    MoonClipHoles(input.uv);
    return 0;
}
#endif
