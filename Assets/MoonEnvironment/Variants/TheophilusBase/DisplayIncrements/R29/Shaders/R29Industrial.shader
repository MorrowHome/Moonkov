Shader "Moon/R29 Industrial Display"
{
    Properties
    {
        [MainTexture] _BaseMap("Atlas base color", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _MaskMap("R metallic G roughness B dust A smoothness", 2D) = "white" {}
        [Normal] _BumpMap("Macro OpenGL normal", 2D) = "bump" {}
        [Normal] _DetailNormal("Metric OpenGL normal", 2D) = "bump" {}
        _DetailRoughness("Metric roughness", 2D) = "gray" {}
        _FinishMode("0 R26, 1 R27, 2 R28", Float) = 0
        _AtlasUVChannel("Atlas channel", Float) = 2
        _DetailUVChannel("Metric channel", Float) = 0
        _DetailTiling("Metric tiling", Float) = 2
        _NormalStrength("Normal strength", Float) = 0.24
        _DetailFactor("R28 encoded detail factor", Float) = 0
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _FinishMode, _AtlasUVChannel, _DetailUVChannel, _DetailTiling;
            float _NormalStrength, _DetailFactor;
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
            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);
            TEXTURE2D(_DetailRoughness); SAMPLER(sampler_DetailRoughness);
            #include "R29Surface.hlsl"
            half4 R29Fragment(R29Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                MoonSetShadowPixel(input.positionCS.xy);
                float2 atlas = input.materialUV.xy;
                float2 detail = input.materialUV.zw * _DetailTiling;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,atlas).rgb * _BaseColor.rgb;
                half4 mask = SAMPLE_TEXTURE2D(_MaskMap,sampler_BaseMap,atlas);
                half roughness = mask.g;
                half3 normalTS;
                if (_FinishMode < 0.5)
                {
                    roughness = saturate(roughness + (SAMPLE_TEXTURE2D(_DetailRoughness,sampler_DetailNormal,detail).r - 0.5) * 0.07);
                    normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal,sampler_DetailNormal,detail),_NormalStrength);
                }
                else
                {
                    normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BaseMap,atlas),_NormalStrength);
                    if (_FinishMode > 1.5)
                    {
                        half3 fine = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal,sampler_DetailNormal,detail),1);
                        normalTS = normalize(normalTS + _DetailFactor * (fine - half3(0,0,1)));
                    }
                }
                half4 color = R29Light(input,albedo,mask.r,1-roughness,R29TangentToWorld(input,normalTS));
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
