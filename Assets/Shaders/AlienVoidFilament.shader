Shader "Moonkov/Alien Void Filament"
{
    Properties { _BaseColor ("Ink", Color) = (.006,.008,.013,1) _VoidTint ("Wet edge", Color) = (.3,.32,.4,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _VoidTint;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.color = v.color; return o; }
            half4 Frag(Varyings v) : SV_Target
            {
                float side = abs(v.uv.y * 2 - 1);
                float edge = pow(side, 4) * .55;
                float alpha = (1 - smoothstep(.7,1,side)) * v.color.a;
                return half4(lerp(_BaseColor.rgb, _VoidTint.rgb, edge), alpha);
            }
            ENDHLSL
        }
    }
}
