Shader "Hidden/MoonEnvironment/Source AgX"
{
    Properties
    {
        _SourceLut("Source OCIO display LUT", 2D) = "white" {}
        _LutSize("LUT size", Float) = 64
        _LogDomain("Log2 domain", Vector) = (-12,16,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D(_SourceLut); SAMPLER(sampler_SourceLut);
            float _LutSize;
            float4 _LogDomain;
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 color=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,input.texcoord);
                float3 logColor=saturate((log2(max(color.rgb,exp2(_LogDomain.x)))-_LogDomain.x)/(_LogDomain.y-_LogDomain.x));
                float3 index=logColor*(_LutSize-1);
                float blue=floor(index.b);
                float2 uv=float2((blue*_LutSize+index.r+.5)/(_LutSize*_LutSize),(index.g+.5)/_LutSize);
                float3 a=SAMPLE_TEXTURE2D_LOD(_SourceLut,sampler_SourceLut,uv,0).rgb;
                uv.x=min(blue+1,_LutSize-1)/_LutSize+(index.r+.5)/(_LutSize*_LutSize);
                float3 b=SAMPLE_TEXTURE2D_LOD(_SourceLut,sampler_SourceLut,uv,0).rgb;
                // The LUT contains encoded display sRGB, imported as raw data.
                // Convert once for Unity's linear render target and final sRGB presentation.
                return half4(SRGBToLinear(lerp(a,b,frac(index.b))),color.a);
            }
            ENDHLSL
        }
    }
}
