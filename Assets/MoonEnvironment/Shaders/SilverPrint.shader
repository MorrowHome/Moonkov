Shader "MoonEnvironment/SilverPrint"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Silver print"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _SilverTone; // exposure, black, white, contrast
            float4 _SilverTexture; // relief, grain, intensity
            float4 _SilverAccent; // strength, cool-chroma threshold
            float4 _SilverAccentColor;

            float3 ReadDisplay(float2 uv)
            {
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                    c = LinearToSRGB(max(c, 0));
                #endif
                return c;
            }

            float Luma(float3 c) { return dot(c, float3(.2126, .7152, .0722)); }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float3 c = ReadDisplay(uv);
                float luminance = Luma(c);
                float2 pixel = _BlitTexture_TexelSize.xy;
                float neighbours = (Luma(ReadDisplay(uv + float2(pixel.x, 0)))
                    + Luma(ReadDisplay(uv - float2(pixel.x, 0)))
                    + Luma(ReadDisplay(uv + float2(0, pixel.y)))
                    + Luma(ReadDisplay(uv - float2(0, pixel.y)))) * .25;

                // Soft silver shoulder, deep ink toe, and local relief retain the source surface detail.
                float value = saturate((luminance * exp2(_SilverTone.x) - _SilverTone.y)
                    / max(.01, _SilverTone.z - _SilverTone.y));
                value = smoothstep(0, 1, value);
                value = saturate((value - .5) * _SilverTone.w + .5);
                value = saturate(value + (luminance - neighbours) * _SilverTexture.x * 3);
                // Stationary fine grain avoids animated noise/flicker during aiming.
                float noise = frac(52.9829189 * frac(dot(floor(input.positionCS.xy), float2(.06711056, .00583715)))) - .5;
                value = saturate(value + noise * _SilverTexture.y * (4 * value * (1 - value)));

                // Existing cool accents and ochre safety paint become pure cobalt; neutral walls stay silver.
                float peak = max(c.r, max(c.g, c.b));
                float coolChroma = saturate((max(c.b, c.g * .85) - c.r) / max(peak, .001));
                float ochreChroma = saturate((min(c.r, c.g * 1.35) - c.b) / max(peak, .001));
                float accent = smoothstep(_SilverAccent.y, _SilverAccent.y + .16, max(coolChroma, ochreChroma))
                    * smoothstep(.06, .2, peak) * _SilverAccent.x;
                float3 cobalt = _SilverAccentColor.rgb * max(value, peak * .72);
                float3 styled = lerp(value.xxx, cobalt, accent);
                #ifndef UNITY_COLORSPACE_GAMMA
                    styled = SRGBToLinear(styled);
                #endif
                return half4(lerp(original.rgb, styled, _SilverTexture.z), original.a);
            }
            ENDHLSL
        }
    }
}
