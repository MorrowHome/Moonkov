Shader "MoonEnvironment/SilverPrint"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _SilverTone; // exposure, black, white, contrast
            float4 _SilverTexture; // relief, grain, intensity
            float4 _SilverAccent; // strength, cool-chroma threshold
            float4 _SilverAccentColor;
            float4 _SilverOptics; // halation strength, threshold, radius, local adaptation
            float4 _SilverSourceTexel;
            float4 _SilverMeterScaleBias;
            TEXTURE2D_X(_SilverMeter);
            float4 _SilverDepthScaleBias;
            float4 _SilverSurface;
            float4 _SilverGraphic; // tonal separation, contour strength, pixel width
            float _SilverHasDepth;
            TEXTURE2D_X_FLOAT(_SilverDepth);

            float3 ReadDisplay(float2 uv)
            {
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                    c = LinearToSRGB(max(c, 0));
                #endif
                return c;
            }

            float Luma(float3 c) { return dot(c, float3(.2126, .7152, .0722)); }

            float Hash(float3 p)
            {
                p = frac(p * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float SurfaceNoise(float3 p)
            {
                float3 cell = floor(p), t = frac(p);
                t = t * t * (3 - 2 * t);
                return lerp(lerp(lerp(Hash(cell), Hash(cell + float3(1,0,0)), t.x),
                    lerp(Hash(cell + float3(0,1,0)), Hash(cell + float3(1,1,0)), t.x), t.y),
                    lerp(lerp(Hash(cell + float3(0,0,1)), Hash(cell + float3(1,0,1)), t.x),
                    lerp(Hash(cell + float3(0,1,1)), Hash(cell + 1), t.x), t.y), t.z);
            }

            float Accent(float3 c)
            {
                float peak = max(c.r, max(c.g, c.b));
                float cool = saturate((max(c.b, c.g * .85) - c.r) / max(peak, .001));
                float ochre = saturate((min(c.r, c.g * 1.35) - c.b) / max(peak, .001));
                return smoothstep(_SilverAccent.y, _SilverAccent.y + .16, max(cool, ochre))
                    * smoothstep(.06, .2, peak) * _SilverAccent.x;
            }

            float4 ExtractLight(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 offset = _SilverSourceTexel.xy;
                float3 a = ReadDisplay(input.texcoord + offset * float2(-1, -1));
                float3 b = ReadDisplay(input.texcoord + offset * float2(1, -1));
                float3 c = ReadDisplay(input.texcoord + offset * float2(-1, 1));
                float3 d = ReadDisplay(input.texcoord + offset * float2(1, 1));
                float4 brightness = float4(Luma(a), Luma(b), Luma(c), Luma(d));
                float4 highlights = smoothstep(_SilverOptics.y, 1, brightness);
                return float4((a + b + c + d) * .25, dot(highlights, .25));
            }

            float4 BlurLight(Varyings input, float2 axis)
            {
                float2 offset = axis * _SilverSourceTexel.xy * _SilverOptics.z;
                float4 result = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord) * .227027;
                result += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + offset * 1.384615) * .316216;
                result += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord - offset * 1.384615) * .316216;
                result += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + offset * 3.230769) * .070270;
                result += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord - offset * 3.230769) * .070270;
                return result;
            }

            float4 BlurHorizontal(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return BlurLight(input, float2(1, 0));
            }

            float4 BlurVertical(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return BlurLight(input, float2(0, 1));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float3 c = original.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                    c = LinearToSRGB(max(c, 0));
                #endif
                float luminance = Luma(c);
                float2 pixel = _SilverSourceTexel.xy;
                float neighbours = (Luma(ReadDisplay(uv + float2(pixel.x, 0)))
                    + Luma(ReadDisplay(uv - float2(pixel.x, 0)))
                    + Luma(ReadDisplay(uv + float2(0, pixel.y)))
                    + Luma(ReadDisplay(uv - float2(0, pixel.y)))) * .25;

                float2 screenUV = (uv - _BlitScaleBias.zw) / _BlitScaleBias.xy;
                float2 meterUV = screenUV * _SilverMeterScaleBias.xy + _SilverMeterScaleBias.zw;
                float4 local = SAMPLE_TEXTURE2D_X(_SilverMeter, sampler_LinearClamp, meterUV);
                float patina = 0;
                float contour = 0;
                if (_SilverHasDepth > .5)
                {
                    float2 depthUV = screenUV * _SilverDepthScaleBias.xy + _SilverDepthScaleBias.zw;
                    float depth = SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV).r;
                    #if UNITY_REVERSED_Z
                        bool hasSurface = depth > .00001;
                    #else
                        bool hasSurface = depth < .99999;
                        depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                    #endif
                    if (_SilverSurface.x > 0)
                    {
                        float3 world = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                        float footprint = max(length(ddx(world)), length(ddy(world)));
                        // Fade away subpixel texture instead of creating distance shimmer.
                        float coarse = (SurfaceNoise(world * 3) - .5) * saturate(1 - footprint * 3);
                        float fine = (SurfaceNoise(world * 14) - .5) * saturate(1 - footprint * 14);
                        patina = hasSurface ? (coarse * .7 + fine * .3) * _SilverSurface.x : 0;
                    }
                    if (_SilverGraphic.y > 0)
                    {
                        // Use raw device depth for LinearEyeDepth, including on non-reversed-Z platforms.
                        float rawDepth = SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV).r;
                        float eye = LinearEyeDepth(rawDepth, _ZBufferParams);
                        float2 stepUV = _SilverSourceTexel.xy * _SilverGraphic.z * abs(_SilverDepthScaleBias.xy / _BlitScaleBias.xy);
                        float right = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV + float2(stepUV.x, 0)).r, _ZBufferParams);
                        float left = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV - float2(stepUV.x, 0)).r, _ZBufferParams);
                        float up = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV + float2(0, stepUV.y)).r, _ZBufferParams);
                        float down = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV - float2(0, stepUV.y)).r, _ZBufferParams);
                        // Foreground side only: no thick black halo outside objects or around sky pixels.
                        float jump = max(0, max(max(right, left), max(up, down)) - eye) / max(eye, .1);
                        contour = hasSurface ? smoothstep(.015, .055, jump) * _SilverGraphic.y : 0;
                    }
                }
                // Spatially bounded dodging opens shaded geometry without a frame-wide exposure jump.
                float dodgeEV = clamp(log2(.32 / max(Luma(local.rgb), .04)) * _SilverOptics.w, -.2, .75);
                float value = max(0, (luminance * exp2(_SilverTone.x + dodgeEV) - _SilverTone.y)
                    / max(.01, _SilverTone.z - _SilverTone.y));
                // Compress the bright shoulder before shaping contrast: silver surfaces retain texture.
                value = saturate(value / (1 + max(0, value - .72) * .6));
                value = lerp(value, smoothstep(0, 1, value), .55);
                value = saturate((value - .5) * _SilverTone.w + .5);
                value = lerp(value, smoothstep(.28, .72, value), _SilverGraphic.x);
                value = saturate(value + patina * (4 * value * (1 - value)));
                value = saturate(value + (luminance - neighbours) * _SilverTexture.x * 3);
                // Stationary fine grain avoids animated noise/flicker during aiming.
                float noise = frac(52.9829189 * frac(dot(floor(input.positionCS.xy), float2(.06711056, .00583715)))) - .5;
                value = saturate(value + noise * _SilverTexture.y * (4 * value * (1 - value)));
                value *= 1 - contour;

                float peak = max(c.r, max(c.g, c.b));
                float accent = Accent(c);
                float3 cobalt = _SilverAccentColor.rgb * max(value, peak * .72);
                float3 styled = lerp(value.xxx, cobalt, accent);
                styled *= 1 - contour * accent;
                // A restrained optical halo is composited AFTER the print curve so it survives ink blacks.
                float3 haloColor = lerp(1.0.xxx, _SilverAccentColor.rgb, Accent(local.rgb));
                styled = 1 - (1 - styled) * (1 - saturate(local.a * _SilverOptics.x) * haloColor * (1 - contour));
                #ifndef UNITY_COLORSPACE_GAMMA
                    styled = SRGBToLinear(styled);
                #endif
                return half4(lerp(original.rgb, styled, _SilverTexture.z), original.a);
            }
        ENDHLSL
        Pass
        {
            Name "Silver print"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "Light extraction"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ExtractLight
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "Horizontal light blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurHorizontal
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "Vertical light blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurVertical
            #pragma target 3.5
            ENDHLSL
        }
    }
}
