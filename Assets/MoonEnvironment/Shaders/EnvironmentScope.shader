Shader "MoonEnvironment/EnvironmentScope"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "EnvironmentScope"
            Tags { "LightMode"="MoonEnvironmentScope" }
            ZWrite Off ZTest Always Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ScopeVertex
            #pragma fragment ScopeFragment
            #pragma multi_compile_instancing
            #include "EnvironmentScopeDepth.hlsl"
            struct ScopeAttributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ScopeVaryings { float4 positionCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            ScopeVaryings ScopeVertex(ScopeAttributes input)
            {
                ScopeVaryings output;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);return output;
            }
            half4 ScopeFragment(ScopeVaryings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                MoonEnvironmentDepthTest(input.positionCS);return 1;
            }
            ENDHLSL
        }
    }
}
