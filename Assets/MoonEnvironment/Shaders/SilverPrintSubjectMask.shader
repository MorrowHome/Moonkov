Shader "MoonEnvironment/SilverPrintSubjectMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Preserve subject"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X_FLOAT(_MoonSubjectSceneDepth);
            float4 _MoonSubjectDepthScaleBias;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                uv = uv * _MoonSubjectDepthScaleBias.xy + _MoonSubjectDepthScaleBias.zw;
                float raw = SAMPLE_TEXTURE2D_X(_MoonSubjectSceneDepth, sampler_PointClamp, uv).r;
                float sceneEye = LinearEyeDepth(raw, _ZBufferParams);
                float subjectEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                // Depth test against the resolved opaque scene avoids MSAA attachment
                // mismatches and prevents actors behind walls from punching holes in the grade.
                clip(sceneEye + max(.0005, sceneEye * .00001) - subjectEye);
                return 1;
            }
            ENDHLSL
        }
    }
}
