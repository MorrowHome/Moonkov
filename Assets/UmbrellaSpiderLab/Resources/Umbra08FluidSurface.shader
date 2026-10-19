Shader "SpiderLab/Umbra08 Particle Fluid Surface"
{
    Properties
    {
        _BaseColor("Liquid colour", Color) = (.035, .048, .055, 1)
        _Threshold("Fluid density isosurface", Range(.05, 1)) = .28
        _Smoothness("Liquid smoothness", Range(0, 1)) = .94
        _HalfVolume("Fluid bounds half-size", Float) = 2.8
        _VolumeResolution("Volume resolution", Float) = 96
        _Predatory("Heavy viscous style", Range(0, 1)) = 1
        _VoidAmount("Gameplay black liquid", Range(0,1)) = 0
        _VoidCharge("Gameplay contraction", Range(0,2)) = 0
        [HideInInspector] _DensityTex("Particle density volume", 3D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="ComplexLit" }
        Cull Front ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE3D(_DensityTex); SAMPLER(sampler_DensityTex);
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor; float _Threshold; half _Smoothness; float _Predatory;
        float _FilmVariation, _CoatMask, _CoatSmoothness, _HalfVolume, _VolumeResolution;
        float _VoidAmount, _VoidCharge;
        CBUFFER_END
        #include "Umbra08LiquidStyle.hlsl"
        struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vert(Attributes v)
        {
            UNITY_SETUP_INSTANCE_ID(v); Varyings o = (Varyings)0; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o;
        }
        float DensityAt(float3 p) { return SAMPLE_TEXTURE3D_LOD(_DensityTex, sampler_DensityTex, p / (_HalfVolume * 2) + .5, 0).r; }
        bool Trace(float3 origin, float3 direction, out float3 hit)
        {
            float3 safeDirection = direction + (abs(direction) < .00001) * .00001;
            float3 a = (-_HalfVolume - origin) / safeDirection, b = (_HalfVolume - origin) / safeDirection;
            float3 near = min(a, b), far = max(a, b);
            float entry = max(0, max(near.x, max(near.y, near.z))), exit = min(far.x, min(far.y, far.z));
            float t = entry + .001, previous = entry;
            [loop] for (int i = 0; i < 1024 && t <= exit; i++)
            {
                float3 p = origin + direction * t;
                if (DensityAt(p) >= _Threshold)
                {
                    float low = previous, high = t;
                    [unroll] for (int j = 0; j < 5; j++)
                    {
                        float mid = (low + high) * .5;
                        if (DensityAt(origin + direction * mid) >= _Threshold) high = mid; else low = mid;
                    }
                    hit = origin + direction * high; return true;
                }
                previous = t; t += lerp(.042, .020, _Predatory);
            }
            hit = 0; return false;
        }
        float3 FluidNormal(float3 p)
        {
            float h = _HalfVolume * 2 / _VolumeResolution * .65;
            float3 gradient = float3(DensityAt(p + float3(h,0,0)) - DensityAt(p - float3(h,0,0)),
                                    DensityAt(p + float3(0,h,0)) - DensityAt(p - float3(0,h,0)),
                                    DensityAt(p + float3(0,0,h)) - DensityAt(p - float3(0,0,h)));
            return normalize(-gradient + float3(0, .00001, 0));
        }
        float DeviceDepth(float3 world)
        {
            float4 clip = TransformWorldToHClip(world); float depth = clip.z / clip.w;
            #if !UNITY_REVERSED_Z
            if (UNITY_NEAR_CLIP_VALUE < 0) depth = depth * .5 + .5;
            #endif
            return depth;
        }
        bool CameraTrace(Varyings v, out float3 world, out float3 normal)
        {
            float3 origin = TransformWorldToObject(GetCameraPositionWS());
            float3 direction = normalize(TransformWorldToObject(v.positionWS) - origin), hit;
            bool found = Trace(origin, direction, hit);
            world = TransformObjectToWorld(hit); normal = TransformObjectToWorldNormal(FluidNormal(hit)); return found;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
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
            struct FragmentOutput { half4 color : SV_Target; float depth : SV_Depth; };
            FragmentOutput Frag(Varyings v)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
                float3 world, normal;
                if (!CameraTrace(v, world, normal)) discard;
                InputData data = (InputData)0;
                data.positionWS = world; data.normalWS = normal; data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(world);
                data.shadowCoord = TransformWorldToShadowCoord(world); data.bakedGI = SampleSH(normal);
                data.vertexLighting = VertexLighting(world, normal); data.shadowMask = half4(1,1,1,1);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(v.positionCS);
                SurfaceData surface = (SurfaceData)0;
                float3 local = TransformWorldToObject(world);
                UmbraLiquidStyle(local, surface.albedo, surface.smoothness, surface.clearCoatMask, surface.clearCoatSmoothness);
                surface.occlusion = surface.alpha = 1; surface.normalTS = half3(0,0,1);
                surface.emission = UmbraVoidEmission(local, normal, data.viewDirectionWS);
                FragmentOutput o; o.color = UniversalFragmentPBR(data, surface);
                o.color.rgb = MixFog(o.color.rgb, ComputeFogFactor(TransformWorldToHClip(world).z));
                o.depth = DeviceDepth(world); return o;
            }
            ENDHLSL
        }
        Pass
        {
            Name "SilverPrintSubjectMask"
            Tags { "LightMode"="MoonSilverPrintSubjectMask" }
            ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment SubjectMaskFrag
            #include "Assets/MoonEnvironment/Shaders/SilverPrintSubjectDepth.hlsl"
            half4 SubjectMaskFrag(Varyings v) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
                float3 origin = TransformWorldToObject(GetCameraPositionWS());
                float3 direction = normalize(TransformWorldToObject(v.positionWS) - origin), hit;
                if (!Trace(origin, direction, hit)) discard;
                MoonSubjectDepthTest(v.positionCS, DeviceDepth(TransformObjectToWorld(hit)), true);
                return 1;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            float ShadowFrag(Varyings v) : SV_Depth
            {
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 origin = TransformWorldToObject(_LightPosition);
                float3 direction = normalize(TransformWorldToObject(v.positionWS) - origin);
                #else
                float3 direction = normalize(TransformWorldToObjectDir(-_LightDirection));
                float3 origin = TransformWorldToObject(v.positionWS) - direction * .005;
                #endif
                float3 hit; if (!Trace(origin, direction, hit)) discard;
                float3 world = TransformObjectToWorld(hit), normal = TransformObjectToWorldNormal(FluidNormal(hit));
                return DeviceDepth(ApplyShadowBias(world, normal, _LightDirection));
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
            #pragma vertex Vert
            #pragma fragment DepthFrag
            float DepthFrag(Varyings v) : SV_Depth
            { float3 world, normal; if (!CameraTrace(v, world, normal)) discard; return DeviceDepth(world); }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct NormalOutput { half4 color : SV_Target; float depth : SV_Depth; };
            NormalOutput NormalFrag(Varyings v)
            {
                float3 world, normal; if (!CameraTrace(v, world, normal)) discard; NormalOutput o;
                #if defined(_GBUFFER_NORMALS_OCT)
                o.color = half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal) * .5 + .5)), 0);
                #else
                o.color = half4(normal, 0);
                #endif
                o.depth = DeviceDepth(world); return o;
            }
            ENDHLSL
        }
    }
}
