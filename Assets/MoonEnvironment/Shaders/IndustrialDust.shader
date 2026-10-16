Shader "MoonEnvironment/Industrial Dust"
{
    Properties
    {
        [MainTexture] _BaseMap("Paint / metal base", 2D) = "white" {}
        [MainColor] _BaseColor("Base tint", Color) = (1,1,1,1)
        _MetallicGlossMap("Metallic (R), smoothness (A)", 2D) = "white" {}
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.35
        [Normal] _BumpMap("Original normal", 2D) = "bump" {}
        _BumpScale("Normal strength", Range(0,2)) = 1
        _OcclusionMap("Original occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion strength", Range(0,1)) = 1
        _DustMap("Metric dust / wear mask", 2D) = "gray" {}
        _DustColor("Regolith tint", Color) = (0.38,0.36,0.32,1)
        _DustAmount("Dust coverage", Range(0,1)) = 0.5
        _DustFloorY("Local floor world height", Float) = 211.64
        _DustTiling("Dust texture repeats per metre", Float) = 0.65
        _CoatStrength("Clean paint clear coat", Range(0,1)) = 0
        _CoatSmoothness("Clear coat smoothness", Range(0,1)) = 0.8
        [HideInInspector] _AtlasUVChannel("Base UV", Float) = 0
        [HideInInspector] _DetailUVChannel("Detail UV", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _DustColor;
            float _AtlasUVChannel, _DetailUVChannel;
            float _Metallic, _Smoothness, _BumpScale, _OcclusionStrength;
            float _DustAmount, _DustFloorY, _DustTiling, _Cull, _Cutoff;
            float _CoatStrength, _CoatSmoothness;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex R29Vertex
            #pragma fragment DustFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _METALLICSPECGLOSSMAP
            #pragma shader_feature_local _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _CLEARCOAT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_DustMap); SAMPLER(sampler_DustMap);
            // Share the project's existing lightmap, Forward+ and probe-aware vertex path.
            #include "../Variants/TheophilusBase/DisplayIncrements/R29/Shaders/R29Surface.hlsl"
            half4 DustFragment(R29Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv0 * _BaseMap_ST.xy + _BaseMap_ST.zw;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb;
                half metallic = _Metallic;
                half smoothness = _Smoothness;
                #if defined(_METALLICSPECGLOSSMAP)
                    half4 metal = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
                    metallic = metal.r;
                    smoothness *= metal.a;
                #endif
                half3 n = normalize(input.normalWS);
                #if defined(_NORMALMAP)
                    n = R29TangentToWorld(input, UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale));
                #endif
                // One extra texture sample. World scale keeps dust grain consistent across UVs.
                float2 dustUV = (input.positionWS.xz + input.positionWS.y * float2(.31,.17)) * _DustTiling;
                half3 mask = SAMPLE_TEXTURE2D(_DustMap, sampler_DustMap, dustUV).rgb;
                half top = smoothstep(.15, .8, n.y);
                half skirting = 1 - smoothstep(.05, .65, max(0, input.positionWS.y - _DustFloorY));
                half dust = saturate(_DustAmount * (top * .85 + skirting * .45) * smoothstep(.12,.82,mask.r));
                albedo = lerp(albedo, _DustColor.rgb * lerp(.8,1.15,mask.g), dust);
                metallic *= 1 - dust;
                smoothness = lerp(smoothness, .13, dust);
                smoothness = saturate(smoothness + (mask.b - .5) * .10 * (1-dust));
                half occlusion = 1;
                #if defined(_OCCLUSIONMAP)
                    occlusion = lerp(1, SAMPLE_TEXTURE2D(_OcclusionMap,sampler_OcclusionMap,uv).g, _OcclusionStrength);
                #endif
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = n;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = input.shadowCoord;
                lighting.fogCoord = input.fogAndVertexLight.x;
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, n);
                lighting.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = metallic;
                surface.smoothness = smoothness;
                surface.occlusion = occlusion;
                surface.normalTS = half3(0,0,1);
                surface.alpha = 1;
                #if defined(_CLEARCOAT)
                    // Dust breaks the coating rather than turning lunar powder into a wet surface.
                    surface.clearCoatMask = _CoatStrength * (1-dust);
                    surface.clearCoatSmoothness = _CoatSmoothness;
                #endif
                half4 color = UniversalFragmentPBR(lighting,surface);
                color.rgb = MixFog(color.rgb,lighting.fogCoord);
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
            #include "../Variants/TheophilusBase/DisplayIncrements/R29/Shaders/R29Depth.hlsl"
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
            #include "../Variants/TheophilusBase/DisplayIncrements/R29/Shaders/R29Depth.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex R29Vertex
            #pragma fragment DustNormals
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            #include "../Variants/TheophilusBase/DisplayIncrements/R29/Shaders/R29Surface.hlsl"
            half4 DustNormals(R29Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                #if defined(_NORMALMAP)
                    float2 uv = input.uv0 * _BaseMap_ST.xy + _BaseMap_ST.zw;
                    n = R29TangentToWorld(input, UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_BumpScale));
                #endif
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(octNormal*.5+.5)),0);
                #else
                    return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
