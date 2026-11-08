Shader "Hidden/Moonkov/RaidInteractionOutline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off

        Pass
        {
            Name "Visible silhouette"
            ZTest LEqual
            Offset -1, -1
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings MaskVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            half4 MaskFragment(Varyings input) : SV_Target { return 1; }
            ENDHLSL
        }

        Pass
        {
            Name "Pulsing interaction contour"
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullscreenVertex
            #pragma fragment OutlineFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_InteractionMask);
            float4 _InteractionMaskScaleBias, _InteractionEdge;

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings FullscreenVertex(uint vertexID : SV_VertexID)
            {
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }

            half Mask(float2 uv) { return SAMPLE_TEXTURE2D_X(_InteractionMask, sampler_LinearClamp, uv).r; }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv * _InteractionMaskScaleBias.xy + _InteractionMaskScaleBias.zw;
                float2 d = _InteractionEdge.xy * abs(_InteractionMaskScaleBias.xy);
                half outer = max(max(Mask(uv + float2(d.x, 0)), Mask(uv - float2(d.x, 0))),
                    max(Mask(uv + float2(0, d.y)), Mask(uv - float2(0, d.y))));
                d *= .7071;
                outer = max(outer, max(max(Mask(uv + d), Mask(uv - d)),
                    max(Mask(uv + float2(d.x, -d.y)), Mask(uv + float2(-d.x, d.y)))));
                return half4(.38, .78, 1, saturate((outer - Mask(uv)) * 1.4) * _InteractionEdge.z);
            }
            ENDHLSL
        }
    }
}
