Shader "Hidden/Moonkov/TeamOutline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Teammate mask"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings MaskVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 MaskFragment(Varyings input) : SV_Target { return 1; }
            ENDHLSL
        }
        Pass
        {
            Name "Teal outer contour"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullscreenVertex
            #pragma fragment OutlineFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_X(_BlitTexture);
            TEXTURE2D_X(_TeamMask);
            float4 _BlitScaleBias, _TeamMaskScaleBias, _TeamTexel;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings FullscreenVertex(uint vertexID : SV_VertexID)
            {
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }
            float Mask(float2 uv) { return SAMPLE_TEXTURE2D_X(_TeamMask, sampler_LinearClamp, uv).r; }
            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                    input.uv * _BlitScaleBias.xy + _BlitScaleBias.zw);
                float2 uv = input.uv * _TeamMaskScaleBias.xy + _TeamMaskScaleBias.zw;
                float2 d = _TeamTexel.xy * abs(_TeamMaskScaleBias.xy);
                float center = Mask(uv);
                float outer = max(max(Mask(uv + float2(d.x, 0)), Mask(uv - float2(d.x, 0))),
                    max(Mask(uv + float2(0, d.y)), Mask(uv - float2(0, d.y))));
                d *= .7071;
                outer = max(outer, max(max(Mask(uv + d), Mask(uv - d)),
                    max(Mask(uv + float2(d.x, -d.y)), Mask(uv + float2(-d.x, d.y)))));
                float contour = saturate(outer - center);
                return half4(lerp(original.rgb, half3(.18, .88, .78), contour * .95), original.a);
            }
            ENDHLSL
        }
    }
}
