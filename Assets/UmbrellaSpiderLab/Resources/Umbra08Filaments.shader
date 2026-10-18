Shader "SpiderLab/Umbra08 Liquid Filaments"
{
    // A Color property receives Unity's sRGB-to-linear conversion, matching the
    // volume shader. An untyped HLSL float4 stays a vector and made threads lighter.
    Properties
    {
        _BaseColor("Liquid colour", Color) = (.035, .048, .055, 1)
        _Smoothness("Liquid smoothness", Range(0, 1)) = .94
        _FilmVariation("Wet film variation", Float) = .045
        _CoatMask("Wet clear coat", Range(0, 1)) = .45
        _CoatSmoothness("Clear coat smoothness", Range(0, 1)) = .88
        _VoidAmount("Gameplay black liquid", Range(0,1)) = 0
        _VoidCharge("Gameplay contraction", Range(0,2)) = 0
        [HideInInspector] _DensityTex("Particle density volume", 3D) = "white" {}
        [HideInInspector] _HalfVolume("Bounds half-size", Float) = 2.8
        [HideInInspector] _VolumeResolution("Volume resolution", Float) = 96
        [HideInInspector] _Threshold("Surface threshold", Float) = .5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="ComplexLit" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct Particle { float3 position; float pad0; float3 velocity; float pad1; };
        StructuredBuffer<Particle> _Particles;
        TEXTURE3D(_DensityTex); SAMPLER(sampler_DensityTex);
        CBUFFER_START(UnityPerMaterial)
        float3 _Gravity, _CarrierVelocity;
        half4 _BaseColor; half _Smoothness;
        float _FilmVariation, _CoatMask, _CoatSmoothness;
        float _ThreadDensity, _RadiusMin, _RadiusMax, _RadiusVariation, _Necking, _RootPooling;
        float _Sag, _VelocityLag, _BreakLength, _BreakStretch;
        float _Threshold, _HalfVolume, _VolumeResolution;
        float _VoidAmount, _VoidCharge;
        CBUFFER_END
        #include "Umbra08LiquidStyle.hlsl"
        struct Attributes { float3 parameter : POSITION; float2 endpoints : TEXCOORD0; float2 rest : TEXCOORD1; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float life : TEXCOORD2; };
        Varyings ThreadVertex(Attributes v)
        {
            Particle a = _Particles[(uint)v.endpoints.x], b = _Particles[(uint)v.endpoints.y];
            float3 delta = b.position - a.position; float length = max(.001, sqrt(dot(delta, delta)));
            float3 axis = length > .0011 ? delta / length : float3(0,1,0);
            float t = v.parameter.x;
            float sine = sin(t * 3.14159265);
            float variation = frac(sin(v.rest.y * 17.37) * 371.13);
            float3 sag = _Gravity * min(.28, length * length * _Sag * lerp(.25, 1.5, variation)) +
                         clamp((a.velocity + b.velocity) * .5 - _CarrierVelocity, -4, 4) * -_VelocityLag;
            float3 centre = lerp(a.position, b.position, t) + sag * sine;
            float3 derivative = delta + sag * (3.14159265 * cos(t * 3.14159265));
            float3 tangent = dot(derivative, derivative) > .00000001 ? normalize(derivative) : axis;
            float3 side = normalize(cross(tangent, abs(tangent.y) < .9 ? float3(0,1,0) : float3(1,0,0)));
            float3 other = cross(tangent, side);
            float3 radial = normalize(side * v.parameter.y + other * v.parameter.z);
            float breakLength = max(_BreakLength, v.rest.x * _BreakStretch);
            float life = length < .02 ? 0 : 1 - smoothstep(breakLength * .65, breakLength, length);
            life *= step(frac(v.rest.y * 13.71), _ThreadDensity);
            float radius = lerp(_RadiusMin, _RadiusMax, lerp(.5, pow(variation, 3), _RadiusVariation)) * sqrt(min(1.8, v.rest.x / length));
            float neck = lerp(.22, .78, frac(v.rest.y * 3.17));
            float width = lerp(.09, .30, frac(v.rest.y * 7.31));
            float neckShape = 1 - _Necking * exp(-pow((t - neck) / width, 2));
            float roots = 1 + _RootPooling * (exp(-t * 18) * lerp(.2, 1.3, variation) +
                              exp(-(1-t) * 22) * lerp(.9, .15, variation));
            float neckDerivative = 2 * _Necking * (t - neck) / (width * width) * exp(-pow((t - neck) / width, 2));
            float rootsDerivative = _RootPooling * (-18 * exp(-t * 18) * lerp(.2, 1.3, variation) +
                                    22 * exp(-(1-t) * 22) * lerp(.9, .15, variation));
            float radiusDerivative = radius * (neckDerivative * roots + neckShape * rootsDerivative) * life;
            radius *= neckShape * roots * life;
            float3 local = centre + radial * radius;
            Varyings o; o.positionWS = TransformObjectToWorld(local); o.positionCS = TransformWorldToHClip(o.positionWS);
            float3 normal = normalize(radial - tangent * radiusDerivative / max(.02, sqrt(dot(derivative, derivative))));
            o.normalWS = TransformObjectToWorldNormal(normal); o.life = life; return o;
        }
        float VolumeDensity(float3 local)
        { return SAMPLE_TEXTURE3D_LOD(_DensityTex, sampler_DensityTex, local / (_HalfVolume * 2) + .5, 0).r; }
        half3 ThreadNormal(Varyings v)
        {
            float3 local = TransformWorldToObject(v.positionWS); float h = _HalfVolume * 2 / _VolumeResolution * .65;
            float3 gradient = float3(VolumeDensity(local + float3(h,0,0)) - VolumeDensity(local - float3(h,0,0)),
                                    VolumeDensity(local + float3(0,h,0)) - VolumeDensity(local - float3(0,h,0)),
                                    VolumeDensity(local + float3(0,0,h)) - VolumeDensity(local - float3(0,0,h)));
            float blend = smoothstep(_Threshold * .55, _Threshold, VolumeDensity(local));
            blend *= dot(gradient, gradient) > .000001 ? 1 : 0;
            half3 volumeNormal = TransformObjectToWorldNormal(normalize(-gradient + float3(0,.00001,0)));
            return normalize(lerp(normalize(v.normalWS), volumeNormal, blend));
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ThreadVertex
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fog
            #define _CLEARCOAT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 Frag(Varyings v) : SV_Target
            {
                clip(v.life - .02);
                InputData data = (InputData)0; data.positionWS = v.positionWS; data.normalWS = ThreadNormal(v);
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(v.positionWS); data.bakedGI = SampleSH(data.normalWS);
                data.shadowCoord = TransformWorldToShadowCoord(v.positionWS); data.shadowMask = half4(1,1,1,1);
                data.vertexLighting = VertexLighting(v.positionWS, data.normalWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(v.positionCS);
                SurfaceData surface = (SurfaceData)0;
                UmbraLiquidStyle(TransformWorldToObject(v.positionWS), surface.albedo, surface.smoothness,
                                surface.clearCoatMask, surface.clearCoatSmoothness); surface.occlusion = surface.alpha = 1; surface.normalTS = half3(0,0,1);
                half4 color = UniversalFragmentPBR(data, surface);
                color.rgb += UmbraVoidEmission(TransformWorldToObject(v.positionWS), data.normalWS, data.viewDirectionWS);
                color.rgb = MixFog(color.rgb, ComputeFogFactor(TransformWorldToHClip(v.positionWS).z)); return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVertex(Attributes v)
            {
                Varyings o = ThreadVertex(v);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 light = normalize(_LightPosition - o.positionWS);
                #else
                float3 light = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, normalize(o.normalWS), light));
                #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z = max(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 ShadowFrag(Varyings v) : SV_Target { clip(v.life - .02); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ThreadVertex
            #pragma fragment NormalFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings v) : SV_Target
            {
                clip(v.life - .02); half3 normal = ThreadNormal(v);
                #if defined(_GBUFFER_NORMALS_OCT)
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal) * .5 + .5)), 0);
                #else
                return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ThreadVertex
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings v) : SV_Target { clip(v.life - .02); return 0; }
            ENDHLSL
        }
    }
}
