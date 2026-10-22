Shader "Moonkov/Melee Solid"
{
    Properties { _BaseColor("Color", Color) = (1,1,1,1) _Emission("Unlit blade", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; half _Emission;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output; output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS); return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half diffuse=.48h+.52h*saturate(dot(normalize(input.normalWS),normalize(float3(-.4,.8,-.5))));
                return half4(_BaseColor.rgb*lerp(diffuse,1,saturate(_Emission)),_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
