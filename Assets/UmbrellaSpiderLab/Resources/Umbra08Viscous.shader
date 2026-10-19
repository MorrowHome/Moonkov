Shader "SpiderLab/Umbra08 Viscous"
{
    Properties
    {
        _BaseColor("Wet black skin", Color) = (.022, .027, .030, 1)
        _Smoothness("Skin smoothness", Range(0, 1)) = .81
        _Coat("Wet coat", Range(0, 1)) = .48
        _FlowAmplitude("Surface flow / metres", Range(0, .008)) = .004
        _FlowTime("Simulation time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="ComplexLit" }
        Cull Back
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half _Smoothness, _Coat;
        float _FlowAmplitude, _FlowTime;
        CBUFFER_END
        struct SkinInput
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float2 restXZ : TEXCOORD0;
            float2 restY : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float3 RestPosition(SkinInput v) { return float3(v.restXZ.x, v.restY.x, v.restXZ.y); }
        float Flow(float3 p)
        {
            return sin(dot(p, float3(5.1, 3.7, 6.4)) - _FlowTime * 1.65) * .65
                 + sin(dot(p, float3(-8.2, 7.3, 2.9)) - _FlowTime * 2.3) * .35;
        }
        float3 FlowPosition(SkinInput v)
        {
            // Vertex red fades to zero around authored toe contacts. Shadow/depth use the same flow.
            return v.positionOS.xyz + v.normalOS * (Flow(RestPosition(v)) * _FlowAmplitude * v.color.r);
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #define _CLEARCOAT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct SkinOutput
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 rest : TEXCOORD2;
                half flowMask : TEXCOORD3;
                half fog : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            SkinOutput Vert(SkinInput v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                SkinOutput o = (SkinOutput)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(FlowPosition(v));
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.rest = RestPosition(v); o.flowMask = v.color.r; o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(SkinOutput v) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
                float phaseA = dot(v.rest, float3(5.1, 3.7, 6.4)) - _FlowTime * 1.65;
                float phaseB = dot(v.rest, float3(-8.2, 7.3, 2.9)) - _FlowTime * 2.3;
                float3 gradient = (cos(phaseA) * float3(5.1, 3.7, 6.4) * .65
                                 + cos(phaseB) * float3(-8.2, 7.3, 2.9) * .35) * _FlowAmplitude * v.flowMask;
                half3 normal = normalize(v.normalWS);
                float3 gradientWS = TransformObjectToWorldDir(gradient, false);
                normal = normalize(normal - (gradientWS - normal * dot(normal, gradientWS)));
                InputData data = (InputData)0;
                data.positionWS = v.positionWS; data.normalWS = normal;
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(v.positionWS);
                data.shadowCoord = TransformWorldToShadowCoord(v.positionWS);
                data.bakedGI = SampleSH(normal);
                data.vertexLighting = VertexLighting(v.positionWS, normal);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(v.positionCS);
                data.shadowMask = half4(1, 1, 1, 1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb;
                surface.smoothness = saturate(_Smoothness + Flow(v.rest) * .035 * v.flowMask);
                surface.occlusion = 1; surface.alpha = 1; surface.normalTS = half3(0, 0, 1);
                surface.clearCoatMask = _Coat; surface.clearCoatSmoothness = .905;
                half4 color = UniversalFragmentPBR(data, surface);
                color.rgb = MixFog(color.rgb, v.fog); return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "SilverPrintSubjectMask"
            Tags { "LightMode"="MoonSilverPrintSubjectMask" }
            ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SubjectMaskVert
            #pragma fragment SubjectMaskFrag
            #pragma multi_compile_instancing
            #include "Assets/MoonEnvironment/Shaders/SilverPrintSubjectDepth.hlsl"
            struct SubjectMaskOutput { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            SubjectMaskOutput SubjectMaskVert(SkinInput v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                SubjectMaskOutput o = (SubjectMaskOutput)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(FlowPosition(v)); return o;
            }
            half4 SubjectMaskFrag(SubjectMaskOutput v) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
                MoonSubjectDepthTest(v.positionCS, v.positionCS.z, true); return 1;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            float4 ShadowVert(SkinInput v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 position = TransformObjectToWorld(FlowPosition(v));
                float3 normal = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 direction = normalize(_LightPosition - position);
                #else
                float3 direction = _LightDirection;
                #endif
                float4 clip = TransformWorldToHClip(ApplyShadowBias(position, normal, direction));
                #if UNITY_REVERSED_Z
                clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE * clip.w);
                #else
                clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE * clip.w);
                #endif
                return clip;
            }
            half4 ShadowFrag() : SV_TARGET { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            float4 DepthVert(SkinInput v) : SV_POSITION
            { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(FlowPosition(v)); }
            half4 DepthFrag() : SV_TARGET { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NormalVert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct NormalOutput { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            NormalOutput NormalVert(SkinInput v)
            {
                UNITY_SETUP_INSTANCE_ID(v); NormalOutput o;
                o.positionCS = TransformObjectToHClip(FlowPosition(v));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o;
            }
            half4 NormalFrag(NormalOutput v) : SV_TARGET
            {
                half3 normal = normalize(v.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normal);
                return half4(PackFloat2To888(saturate(oct * .5 + .5)), 0);
                #else
                return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
