Shader "DollSinger/RifleHaloLine"
{
    Properties { _Gain ("Energy brightness", Float) = 3 }
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
                half edge=saturate(1-abs(i.uv.y*2-1));
                half aura=pow(edge,1.65);
                half core=pow(edge,7);
                half alpha=i.color.a*aura;
                half3 energy=lerp(i.color.rgb,half3(1,1,1),core*.6);
                return half4(energy*alpha*_Gain,alpha);
            }
            ENDHLSL
        }
    }
}
