Shader "Moonkov/RaidInventoryBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Live world blur"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Blur
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float2 _BlurStep;

            half4 Blur(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 0.227027;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + _BlurStep * 1.384615) * 0.316216;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - _BlurStep * 1.384615) * 0.316216;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + _BlurStep * 3.230769) * 0.0702705;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - _BlurStep * 3.230769) * 0.0702705;
                return color;
            }
            ENDHLSL
        }
    }
}
