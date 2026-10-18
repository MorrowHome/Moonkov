#ifndef MOON_R29_SURFACE_INCLUDED
#define MOON_R29_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

#if defined(MOON_SILVER_SAND)
#include "Assets/MoonEnvironment/Shaders/SilverSand.hlsl"
#endif

struct R29Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv0 : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
    float2 uv3 : TEXCOORD3;
    float2 uv4 : TEXCOORD4;
    float2 uv5 : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct R29Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half4 tangentWS : TEXCOORD2;
    float4 materialUV : TEXCOORD3;
    float2 uv0 : TEXCOORD4;
    float4 shadowCoord : TEXCOORD5;
    half4 fogAndVertexLight : TEXCOORD6;
    DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 7);
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float2 R29UV(R29Attributes input, float channel)
{
    if (channel > 4.5) return input.uv5;
    if (channel > 3.5) return input.uv4;
    if (channel > 2.5) return input.uv3;
    if (channel > 1.5) return input.uv2;
    return input.uv0;
}

R29Varyings R29Vertex(R29Attributes input)
{
    R29Varyings output = (R29Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.positionCS = p.positionCS;
    output.positionWS = p.positionWS;
    output.normalWS = n.normalWS;
    output.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.materialUV = float4(R29UV(input, _AtlasUVChannel), R29UV(input, _DetailUVChannel));
    output.uv0 = input.uv0;
    output.shadowCoord = GetShadowCoord(p);
    output.fogAndVertexLight = half4(ComputeFogFactor(p.positionCS.z), VertexLighting(p.positionWS, n.normalWS));
    OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
    OUTPUT_SH(n.normalWS, output.vertexSH);
    return output;
}

half3 R29TangentToWorld(R29Varyings input, half3 normalTS)
{
    half3 n = normalize(input.normalWS);
    half3 t = normalize(input.tangentWS.xyz);
    half3 b = input.tangentWS.w * cross(n, t);
    return normalize(mul(normalTS, half3x3(t, b, n)));
}

// Explicit UV-derivative basis for world-XZ reference maps, independent of UV0.
float3 R29WorldMappedNormal(float3 p, float3 n, float2 uv, float3 normalTS)
{
    float3 dx = ddx(p), dy = ddy(p);
    float2 ux = ddx(uv), uy = ddy(uv);
    float determinant = ux.x * uy.y - ux.y * uy.x;
    float safeDet = abs(determinant) > 1e-12 ? determinant : 1e-12;
    float3 t = (dx * uy.y - dy * ux.y) / safeDet;
    float3 b = (dy * ux.x - dx * uy.x) / safeDet;
    t = normalize(t - n * dot(t, n) + float3(1e-12,0,0));
    b = normalize(b - n * dot(b, n) - t * dot(b, t) + float3(0,0,1e-12));
    return normalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);
}

float3 R29HeightNormal(float3 p, float3 n, float height)
{
    float3 dx = ddx(p), dy = ddy(p);
    float3 r1 = cross(dy, n), r2 = cross(n, dx);
    float det = dot(dx, r1);
    float3 gradient = sign(det) * (ddx(height) * r1 + ddy(height) * r2);
    return normalize(max(abs(det), 1e-12) * n - gradient);
}

half4 R29Light(R29Varyings input, half3 albedo, half metallic, half smoothness, half3 normalWS)
{
    InputData data = (InputData)0;
    data.positionWS = input.positionWS;
    data.normalWS = normalize(normalWS);
    data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    data.shadowCoord = input.shadowCoord;
    data.fogCoord = input.fogAndVertexLight.x;
    data.vertexLighting = input.fogAndVertexLight.yzw;
    data.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, data.normalWS);
    data.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
    data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    surface.metallic = metallic;
    #if defined(MOON_SILVER_SAND)
    surface.smoothness = max(smoothness, _SilverSheen.y);
    #else
    surface.smoothness = smoothness;
    #endif
    surface.normalTS = half3(0,0,1);
    surface.occlusion = 1;
    surface.alpha = 1;
    half4 color = UniversalFragmentPBR(data, surface);
    #if defined(MOON_SILVER_SAND)
    color.rgb += SilverSandLighting(data);
    #endif
    color.rgb = MixFog(color.rgb, data.fogCoord);
    return color;
}
#endif
