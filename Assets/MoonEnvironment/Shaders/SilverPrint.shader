Shader "MoonEnvironment/SilverPrint"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Black white cobalt HDR finish"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _SilverFaithful;
            float4 _SilverTone; // EV, exposed-linear black/white reference, contrast (formal start: 1 / .025 / .26 / 1.15)
            float4 _SilverTexture; // real source detail, unused, blend, HDR highlight retention
            float4 _SilverAccent; // strength, saturation threshold
            float4 _SilverAccentColor; // authored sRGB cobalt swatch
            float4 _SilverGraphic; // block separation, contour strength/width, shading retention
            float4 _SilverSourceTexel;
            float4 _SilverDepthScaleBias;
            float _SilverHasDepth;
            TEXTURE2D_X_FLOAT(_SilverDepth);
            TEXTURE2D_X(_SilverProtection);
            float4 _SilverProtectionScaleBias;

            float3 ReadLinear(float2 uv)
            {
                float3 c = max(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb, 0);
                #ifdef UNITY_COLORSPACE_GAMMA
                    c = SRGBToLinear(c);
                #endif
                return c;
            }
            float Luma(float3 c) { return dot(c, float3(.2126, .7152, .0722)); }

            float BlueAccent(float3 c)
            {
                float peak = max(c.r, max(c.g, c.b));
                float minimum = min(c.r, min(c.g, c.b));
                float saturation = (peak - minimum) / max(peak, .001);
                float blueLead = (c.b - max(c.r, c.g)) / max(peak, .001);
                float threshold = clamp(_SilverAccent.y, .35, .82);
                // Only saturated blue-dominant source surfaces qualify. Neutral
                // cool whites, cyan, purple and yellow safety paint do not become blue.
                return smoothstep(threshold, min(.98, threshold + .18), saturation)
                    * smoothstep(.20, .42, blueLead)
                    * (1 - smoothstep(.30, .52, c.r / max(peak, .001)))
                    * smoothstep(.015, .06, peak) * saturate(_SilverAccent.x);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float2 screenUV = (uv - _BlitScaleBias.zw) / _BlitScaleBias.xy;
                float2 scopeUV = screenUV * _SilverProtectionScaleBias.xy + _SilverProtectionScaleBias.zw;
                // Keep positive native material scope, with no expansion over actors.
                if (_SilverFaithful > .5 || SAMPLE_TEXTURE2D_X(_SilverProtection, sampler_PointClamp, scopeUV).r < .5)
                    return original;

                float3 source = max(original.rgb, 0);
                #ifdef UNITY_COLORSPACE_GAMMA
                    source = SRGBToLinear(source);
                #endif
                float y = Luma(source);
                float edge = 0;
                float detailCoverage = 1;
                if (_SilverHasDepth > .5)
                {
                    float2 depthUV = screenUV * _SilverDepthScaleBias.xy + _SilverDepthScaleBias.zw;
                    float raw = SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV).r;
                    #if UNITY_REVERSED_Z
                        bool hasSurface = raw > .00001;
                    #else
                        bool hasSurface = raw < .99999;
                    #endif
                    float eye = LinearEyeDepth(raw, _ZBufferParams);
                    float2 stepUV = _SilverSourceTexel.xy * max(.5, _SilverGraphic.z)
                        * abs(_SilverDepthScaleBias.xy / _BlitScaleBias.xy);
                    float right = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV + float2(stepUV.x, 0)).r, _ZBufferParams);
                    float left = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV - float2(stepUV.x, 0)).r, _ZBufferParams);
                    float up = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV + float2(0, stepUV.y)).r, _ZBufferParams);
                    float down = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SilverDepth, sampler_PointClamp, depthUV - float2(0, stepUV.y)).r, _ZBufferParams);
                    // Foreground side only. No thick outline on background or sky.
                    float jump = max(0, max(max(right, left), max(up, down)) - eye) / max(eye, .1);
                    edge = hasSurface ? smoothstep(.015, .055, jump) : 0;
                    float discontinuity = max(max(abs(right - eye), abs(left - eye)),
                        max(abs(up - eye), abs(down - eye))) / max(eye, .1);
                    detailCoverage = 1 - smoothstep(.01, .04, discontinuity);
                }

                float adjusted = y * exp2(_SilverTone.x);
                float whiteReference = max(_SilverTone.z, _SilverTone.y + .04);
                float coordinate = saturate((adjusted - _SilverTone.y) / (whiteReference - _SilverTone.y));
                coordinate = saturate((coordinate - .5) * _SilverTone.w + .5);
                float shoulder = adjusted / (adjusted + .35);
                // Three connected tonal masses, each retaining a small continuous
                // lighting response. Paper white never becomes a clipped flat plateau.
                float ink = .004 + .018 * shoulder;
                float darkLayer = .055 + .09 * shoulder;
                float paper = .72 + .26 * shoulder;
                float graphic = lerp(lerp(ink, darkLayer, smoothstep(.12, .38, coordinate)),
                    paper, smoothstep(.42, .70, coordinate));
                float body = lerp(shoulder, graphic, saturate(_SilverGraphic.x));
                body = lerp(body, shoulder, saturate(_SilverGraphic.w));
                body *= smoothstep(max(.0001, _SilverTone.y * .25), max(.001, _SilverTone.y), adjusted);

                float2 pixel = _SilverSourceTexel.xy;
                float mean = (2 * y + Luma(ReadLinear(uv + float2(pixel.x, 0)))
                    + Luma(ReadLinear(uv - float2(pixel.x, 0)))
                    + Luma(ReadLinear(uv + float2(0, pixel.y)))
                    + Luma(ReadLinear(uv - float2(0, pixel.y)))) / 6;
                // Retain the actual source texture and BRDF response. There is no
                // generated cloud, marble interior, screen grain or new highlight.
                float detailEV = clamp(log2(max(y, .003) / max(mean, .003)), -.35, .35);
                body *= exp2(detailEV * saturate(_SilverTexture.x) * detailCoverage);
                float residual = max(0, adjusted - whiteReference);
                residual = residual * residual / (residual + .15);
                float highlight = residual * max(0, _SilverTexture.w);

                float accent = BlueAccent(source);
                float3 cobalt = SRGBToLinear(max(_SilverAccentColor.rgb, 0));
                float peak = max(source.r, max(source.g, source.b)) * exp2(_SilverTone.x);
                float blueBody = max(body, .16 + .6 * peak / (peak + .35));
                float blueResidual = max(0, peak - whiteReference);
                blueResidual = blueResidual * blueResidual / (blueResidual + .15);
                // Keep the real HDR bright-light residual above white so the
                // existing bloom and final tonemapper still see its optical energy.
                float3 styled = lerp(body.xxx + highlight.xxx,
                    cobalt * (blueBody + blueResidual * max(0, _SilverTexture.w)), accent);
                styled *= 1 - edge * saturate(_SilverGraphic.y);
                styled = min(max(styled, 0), 65000.0);
                #ifdef UNITY_COLORSPACE_GAMMA
                    styled = LinearToSRGB(styled);
                #endif
                return half4(lerp(original.rgb, styled, saturate(_SilverTexture.z)), original.a);
            }
            ENDHLSL
        }
    }
}
