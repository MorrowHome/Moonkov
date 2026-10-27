Shader "Moon/R29 Local Ground Display"
{
    Properties
    {
        _SilverSand("Mineral facets: strength / cells per m / density / fade m", Vector) = (0, 8, 0.48, 45)
        _SilverSheen("Sheen: strength / minimum smoothness / facet spread", Vector) = (0, 0, 1.6, 0)

        [MainTexture] _BaseMap("Local base color", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _RoughnessMap("Local roughness", 2D) = "white" {}
        [Normal] _BumpMap("Local OpenGL normal", 2D) = "bump" {}
        [Normal] _MicroNormal("Micro OpenGL normal", 2D) = "bump" {}
        _WorldBase("World 64m base color", 2D) = "white" {}
        _WorldRoughness("World 64m roughness", 2D) = "white" {}
        _WorldHeight("World 64m height", 2D) = "gray" {}
        _FineHeight("World 4m fine height", 2D) = "gray" {}
        _Macro256("World 256m macro", 2D) = "gray" {}
        _Macro32("Local 32m macro", 2D) = "gray" {}
        _EdgeMask("Local edge mask", 2D) = "black" {}
        _LocalMean("Local mean linear RGB", Vector) = (0.12584,0.12258,0.11762,0)
        _NormalStrength("Local normal strength", Float) = 0.65
        [HideInInspector] _AtlasUVChannel("Atlas channel", Float) = 0
        [HideInInspector] _DetailUVChannel("Metric channel", Float) = 0
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _SilverSand;
            float4 _SilverSheen;
            half4 _BaseColor;
            float4 _LocalMean;
            float _NormalStrength, _AtlasUVChannel, _DetailUVChannel;
        CBUFFER_END
        ENDHLSL
        // Explicit opaque environment material domain; stock/actor/VFX shaders never opt in.
        UsePass "MoonEnvironment/EnvironmentScope/EnvironmentScope"
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex R29Vertex
            #pragma fragment R29Fragment
            #pragma multi_compile_fragment _ _MOON_STOCHASTIC_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MicroNormal); SAMPLER(sampler_MicroNormal);
            TEXTURE2D(_WorldBase); SAMPLER(sampler_WorldBase);
            TEXTURE2D(_WorldRoughness); SAMPLER(sampler_WorldRoughness);
            TEXTURE2D(_WorldHeight); SAMPLER(sampler_WorldHeight);
            TEXTURE2D(_FineHeight); SAMPLER(sampler_FineHeight);
            TEXTURE2D(_Macro256); SAMPLER(sampler_Macro256);
            TEXTURE2D(_Macro32); SAMPLER(sampler_Macro32);
            TEXTURE2D(_EdgeMask); SAMPLER(sampler_EdgeMask);
            #define MOON_SILVER_SAND 1
            #include "R29Surface.hlsl"
            half4 R29Fragment(R29Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                MoonSetShadowPixel(input.positionCS.xy);
                float3 p = GetAbsolutePositionWS(input.positionWS);
                half3 n = normalize(input.normalWS);
                float distanceToView = distance(p,GetCameraPositionWS());
                float2 worldUV = p.xz / 64;
                float2 localUV = input.uv0 / 4;
                half macro = SAMPLE_TEXTURE2D(_Macro256,sampler_BaseMap,p.xz/256).r;
                half3 referenceColor = SAMPLE_TEXTURE2D(_WorldBase,sampler_BaseMap,worldUV).rgb * (0.86+0.28*macro) * (0.94+0.06*max(n.y,0));
                half localMacro = SAMPLE_TEXTURE2D(_Macro32,sampler_BaseMap,p.xz/32).r;
                half3 localColor = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,localUV).rgb * (0.92+0.16*localMacro);
                float2 edgeUV = (p.xz-float2(1928,2038))/float2(186,140);
                half inside = all(edgeUV>=0) && all(edgeUV<=1) ? 1 : 0;
                half mask = SAMPLE_TEXTURE2D(_EdgeMask,sampler_EdgeMask,edgeUV).r * inside;
                half3 albedo = referenceColor * lerp(half3(1,1,1),localColor/max(_LocalMean.rgb,0.0001),mask) * _BaseColor.rgb;
                half roughness = lerp(SAMPLE_TEXTURE2D(_WorldRoughness,sampler_BaseMap,worldUV).r,SAMPLE_TEXTURE2D(_RoughnessMap,sampler_BaseMap,localUV).r,mask);
                float meso = SAMPLE_TEXTURE2D(_WorldHeight,sampler_BaseMap,worldUV).r * (0.33418717980384827+0.22833238542079926);
                float fine = SAMPLE_TEXTURE2D(_FineHeight,sampler_BaseMap,p.xz/4).r * 0.03775 * lerp(0.55,0,saturate((distanceToView-12)/108));
                float3 referenceNormal = R29HeightNormal(p,n,meso*0.95+fine);
                half3 mainNormal = R29TangentToWorld(input,UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BaseMap,localUV),_NormalStrength));
                half microStrength = lerp(0.25,0,saturate((distanceToView-8)/27));
                half3 microNormal = R29TangentToWorld(input,UnpackNormalScale(SAMPLE_TEXTURE2D(_MicroNormal,sampler_BaseMap,input.uv0/0.25),microStrength));
                half3 localNormal = normalize(mainNormal+microNormal-n);
                half3 normalWS = normalize(lerp(referenceNormal,normalize(localNormal+referenceNormal-n),mask));
                half4 color = R29Light(input,albedo,0,1-saturate(roughness),normalWS);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex R29ShadowVertex
            #pragma fragment R29DepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "R29Depth.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex R29DepthVertex
            #pragma fragment R29DepthFragment
            #pragma multi_compile_instancing
            #include "R29Depth.hlsl"
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
