Shader "MoonEnvironment/Ink Architecture"
{
    Properties
    {
        _PaperColor ("Paper", Color) = (0.8,0.8,0.78,1)
        _InkColor ("Ink", Color) = (0.003,0.003,0.004,1)
        _AccentColor ("Blue violet", Color) = (0.03,0.015,0.4,1)
        [Enum(Paper,0,Ink,1,Accent,2,Ground,3)] _Role ("Surface role", Float) = 0
        _Wear ("Print wear", Range(0,1)) = 0.28
        _Opacity ("Opacity", Range(0,1)) = 1
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        // The imported source architecture includes two-sided sheets.
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _PaperColor, _InkColor, _AccentColor;
            float _Role, _Wear, _Opacity;
            float _SrcBlend, _DstBlend, _ZWrite;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return o;
        }
        float Hash(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }
        float Grain(float3 p)
        {
            float3 cell = floor(p), w = frac(p);
            w = w*w*(3-2*w);
            return lerp(lerp(lerp(Hash(cell),Hash(cell+float3(1,0,0)),w.x),
                             lerp(Hash(cell+float3(0,1,0)),Hash(cell+float3(1,1,0)),w.x),w.y),
                        lerp(lerp(Hash(cell+float3(0,0,1)),Hash(cell+float3(1,0,1)),w.x),
                             lerp(Hash(cell+float3(0,1,1)),Hash(cell+1),w.x),w.y),w.z);
        }
        ENDHLSL

        Pass
        {
            Name "InkForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half4 Frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normalWS) * IS_FRONT_VFACE(facing,1,-1);
                // Shape comes from broad value planes, independently of the old PBR colours.
                float light = dot(n, normalize(float3(-0.15,0.5,-0.85))) * 0.5 + 0.5;
                float band = 0.16*smoothstep(0.32,0.35,light) + 0.84*smoothstep(0.70,0.73,light);
                float3 p = i.positionWS;
                float patch = smoothstep(0.47,0.76,Grain(p*0.24));
                float rub = Grain(p*float3(18,0.8,18));
                float detailFade = 1-saturate(length(fwidth(p*18))*0.35);
                float gritFade = 1-saturate(length(fwidth(p*70)));
                float grit = lerp(0.35,step(0.58,Hash(floor(p*70))),gritFade);
                float wear = patch * (lerp(0.5,rub,detailFade)*0.65+grit*0.35) * _Wear;
                float3 paper = lerp(_InkColor.rgb,_PaperColor.rgb,band) * (1-wear);
                float3 ink = _InkColor.rgb + band*0.009;
                float3 accent = _AccentColor.rgb * (0.72+0.28*band) * (1-wear*0.35);
                // Terrain stays quiet; large blotches would compete with the architecture.
                float3 ground = lerp(_InkColor.rgb,_PaperColor.rgb,0.01+0.018*band);
                float3 color = _Role < 0.5 ? paper : _Role < 1.5 ? ink : _Role < 2.5 ? accent : ground;
                return half4(color,_Opacity);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            #pragma multi_compile_instancing
            half Depth(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Normals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 Normals(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 n = normalize(i.normalWS) * IS_FRONT_VFACE(facing,1,-1);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct*0.5+0.5)),0);
                #else
                    return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
}
