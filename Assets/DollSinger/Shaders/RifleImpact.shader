Shader "Moonkov/Rifle Impact"
{
    Properties
    {
        [HideInInspector] _ImpactLayer ("Impact layer", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _ImpactLayer;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv - .5;
                half alpha;
                half3 color = input.color.rgb;
                if (_ImpactLayer < .5)
                {
                    // A clipped shale silhouette with two quiet facets, rather than glowing round beads.
                    float edge = max(abs(p.x) * .85 + abs(p.y) * .7, abs(p.y) * 1.18);
                    edge = max(edge, dot(p, float2(.82, -.57)) + .045);
                    float aa = max(fwidth(edge), .008);
                    alpha = 1 - smoothstep(.34 - aa, .34 + aa, edge);
                    color *= lerp(.64, 1.08, smoothstep(-.025, .025, p.y + p.x * .55));
                }
                else if (_ImpactLayer < 1.5)
                {
                    // Stretch renderer supplies motion; a thin hot core keeps the spark legible after bloom.
                    float radius = length(p * float2(2.2, 1));
                    alpha = 1 - smoothstep(.18, .49, radius);
                    half core = (1 - smoothstep(.015, .12, abs(p.x))) *
                        (1 - smoothstep(.15, .46, abs(p.y)));
                    color *= 1.25 + 2.5 * core;
                }
                else if (_ImpactLayer < 2.5)
                {
                    alpha = 1 - smoothstep(.12, .47, length(p * float2(1.1, .85)));
                }
                else if (_ImpactLayer < 3.5)
                {
                    // Fixed, texture-free density breaks up the ejecta edge without animated boiling.
                    float2 q = p * 12;
                    float density = sin(q.x + sin(q.y * 1.3)) * sin(q.y - q.x * .4);
                    float radius = length(p) + density * .035;
                    alpha = (1 - smoothstep(.08, .48, radius)) * (.76 + density * .24);
                }
                else
                {
                    float radius = length(p);
                    half core = 1 - smoothstep(0, .19, radius);
                    half rays = pow(saturate(1 - min(abs(p.x), abs(p.y)) * 24), 3) *
                        (1 - smoothstep(.1, .46, radius));
                    alpha = saturate(core + rays * .5);
                    color *= 1.5 + core * 2;
                }
                return half4(color, input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
