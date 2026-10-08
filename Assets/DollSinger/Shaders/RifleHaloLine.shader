Shader "DollSinger/RifleHaloLine"
{
    Properties { _Gain ("Energy brightness", Float) = 1.5 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off Blend One OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half _Gain;
            CBUFFER_END
            struct Input { float4 vertex:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Output { float4 position:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            Output vert(Input v) { Output o; o.position=TransformObjectToHClip(v.vertex.xyz); o.color=v.color; o.uv=v.uv; return o; }
            half4 frag(Output i):SV_Target
            {
                // Uniform luminous ink, matching the flat revolver / shotgun graphics.
                return half4(i.color.rgb*i.color.a*_Gain,i.color.a);
            }
            ENDHLSL
        }
    }
}
