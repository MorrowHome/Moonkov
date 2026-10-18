Shader "Moonkov/Alien Void Skin"
{
    Properties
    {
        _BaseColor ("Obsidian tissue", Color) = (.012,.014,.019,1)
        [HDR] _VoidTint ("Ash fissures", Color) = (.32,.34,.40,1)
        _VoidCharge ("Contraction / charge", Range(0,2)) = 0
        _Smoothness ("Wetness", Range(0,1)) = .78
        _Metallic ("Mineral coat", Range(0,1)) = .16
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _VoidTint;
            float _VoidCharge, _Smoothness, _Metallic, _Cull;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 local : TEXCOORD2; };
        Varyings Vert(Attributes v)
        {
            Varyings o; o.local = v.positionOS.xyz;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            return o;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "AlienVoidStyle.hlsl"
            half4 Frag(Varyings v, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                InputData data = (InputData)0;
                data.positionWS = v.positionWS;
                data.normalWS = normalize(v.normalWS) * IS_FRONT_VFACE(face, 1, -1);
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(v.positionWS);
                data.shadowCoord = TransformWorldToShadowCoord(v.positionWS);
                data.bakedGI = SampleSH(data.normalWS); data.shadowMask = half4(1,1,1,1);
                data.vertexLighting = VertexLighting(v.positionWS, data.normalWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(v.positionCS);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb; surface.metallic = _Metallic;
                surface.smoothness = saturate(_Smoothness + .035 * sin(dot(v.local,float3(7,23,13))));
                surface.normalTS = half3(0,0,1); surface.occlusion = surface.alpha = 1;
                surface.emission = AlienVoidRadiance(v.local, data.normalWS, data.viewDirectionWS, _VoidCharge, _VoidTint.rgb);
                half4 color = UniversalFragmentPBR(data, surface);
                color.rgb = MixFog(color.rgb, ComputeFogFactor(v.positionCS.z));
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVert(Attributes v)
            {
                Varyings o = Vert(v);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction = normalize(_LightPosition - o.positionWS);
                #else
                    float3 direction = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, normalize(o.normalWS), direction));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 DepthFrag(Varyings v) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings v) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings v) : SV_Target
            {
                float3 n = normalize(v.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n) * .5 + .5)), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
