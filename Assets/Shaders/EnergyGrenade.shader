Shader "Moonkov/EnergyGrenade"
{
    Properties
    {
        [HDR] _Color ("Energy", Color) = (0.12, 1.1, 2, 1)
        _Opacity ("Opacity", Range(0,1)) = 1
        _Shell ("Envelope", Float) = 0
        _Mode ("0 Orb / 1 Ribbon / 2 Spark", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Opacity, _Shell, _Mode;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float3 local : TEXCOORD2; float2 uv : TEXCOORD3; float4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(world);
                output.local = input.positionOS.xyz;
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                if (_Mode > .5)
                {
                    float edge = saturate(1 - abs(input.uv.y * 2 - 1));
                    if (_Mode > 1.5) edge *= saturate(1 - abs(input.uv.x * 2 - 1));
                    return half4(_Color.rgb * input.color.rgb * (1 + edge), edge * input.color.a * _Opacity);
                }
                float facing = saturate(abs(dot(normalize(input.normalWS), normalize(input.viewWS))));
                float rim = pow(1 - facing, 2.3);
                float3 p = input.local;
                float waves = sin(dot(p, float3(17,23,13)) + _Time.y * 7) *
                    sin(dot(p, float3(-29,11,31)) - _Time.y * 9);
                float filaments = pow(saturate(1 - abs(waves) * 8), 5);
                float envelope = rim * .9 + filaments * .65 + .045;
                float core = .6 + facing * .8 + filaments * .3;
                float3 color = lerp(float3(1.7, 2.4, 2.8), _Color.rgb * 1.6, _Shell);
                return half4(color, lerp(core, envelope, _Shell) * _Opacity);
            }
            ENDHLSL
        }
    }
}
