Shader "MoonEnvironment/Regolith"
{
    Properties
    {
        _Color("Regolith reflectance", Color) = (0.60,0.60,0.60,1)
        _MainTex("Grain", 2D) = "white" {}
        _BumpMap("Grain normal", 2D) = "bump" {}
        _MicroTex("Fine regolith albedo", 2D) = "white" {}
        _MicroNormal("Fine regolith normal", 2D) = "bump" {}
        _Roughness("PBR roughness", 2D) = "white" {}
        _HeightMap("Regolith displacement", 2D) = "gray" {}
        _DetailStrength("Synthetic detail", Range(0,1)) = 1
        [Toggle(_MOON_LUNAR_LIGHTING)] _LunarLighting("Lunar diffuse lighting", Float) = 1
        _LunarWeight("Lommel-Seeliger weight", Range(0,1)) = 0.55
        _IndirectStrength("Indirect light contribution", Range(0,1)) = 0
        _ReflectanceScale("Reflectance calibration (exposure independent)", Range(0.1,1.5)) = 0.8
        _LocalBounceStrength("Local terrain bounce", Range(0,2)) = 1
        _BounceAlbedo("Average source terrain reflectance", Range(0,0.3)) = 0.12
        _GrainNormalStrength("Grain normal strength", Range(0,1.5)) = 0.55
        _FineNormalStrength("Fine normal strength", Range(0,1)) = 0.18
        _ParallaxMetres("Near grain relief in metres", Range(0,0.025)) = 0.008
        _MacroContrast("Landscape material contrast", Range(0,1)) = 0.8
        _MaterialFadeStart("Preserve near material contrast to (m)", Float) = 35
        _MaterialFadeEnd("Distant material blend complete (m)", Float) = 180
        _DistantVariation("Distant macro contrast fraction", Range(0,1)) = 0.12
        _FaciesStrength("Powder / exposed rock separation", Range(0,1)) = 0.85
        _BedrockStrength("Metre-scale fractured surface", Range(0,1)) = 0.7
        _DustTint("Fine powder tint", Color) = (1.06,1.03,0.98,1)
        _BasaltTint("Exposed rock tint", Color) = (0.64,0.67,0.71,1)
        [HideInInspector] _ImpactGeology("Generated rim / rubble / basin", 2D) = "black" {}
        [HideInInspector] _ImpactReady("Generated geology enabled", Float) = 0
        [HideInInspector] _LocalTerrainGeometry("Runtime terrain geometry", 2D) = "black" {}
        [HideInInspector] _LocalTerrainRect("Runtime terrain bounds", Vector) = (0,0,1,1)
        [HideInInspector] _LocalTerrainReady("Runtime terrain ready", Float) = 0
        [Toggle] _PhaseEnabled("Phase / opposition reflectance", Float) = 1
        _PhaseSlope("Broad phase falloff per radian", Range(0,1)) = 0.12
        _ShadowHidingAmplitude("Shadow hiding peak amplitude", Range(0,1)) = 0.25
        _ShadowHidingWidth("Shadow hiding angle scale (degrees)", Range(0.05,30)) = 12
        _CoherentAmplitude("Coherent backscatter peak amplitude", Range(0,1)) = 0.08
        _CoherentWidth("Coherent angle scale (degrees)", Range(0.05,15)) = 3
        _GeologyMap("Synthetic ejecta / slope / basin masks", 2D) = "black" {}
        _GeologyRect("Mask world XZ origin / size", Vector) = (1792,1792,512,512)
        _GeologyStrength("Geological variation", Range(0,1)) = 0.65
        _EjectaBrightness("Fresh ejecta reflectance variation", Range(0,0.4)) = 0.16
        [HideInInspector] _TerrainHolesTexture("Terrain Holes Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-100" "TerrainCompatible"="True" }
        HLSLINCLUDE
        #pragma target 3.5
        #define MOON_TERRAIN 1
        #include "MoonInput.hlsl"
        void SampleGrain(float2 uv,float2 dx,float2 dy,float2 cell,float parallax,float3 viewDir,out float gray,out float3 normal,out float roughness)
        {
            float2 seed=MoonHash(cell),cs=float2(cos(seed.x*6.2831853),sin(seed.x*6.2831853));
            float2 p=MoonRotate(uv,cs)+seed*31.7,gx=MoonRotate(dx,cs),gy=MoonRotate(dy,cs);
            float3 localView=float3(MoonRotate(viewDir.xy,cs),viewDir.z);
            UNITY_BRANCH if(parallax>0.00001) p+=MoonParallaxOffset(tex2Dgrad(_HeightMap,p,gx,gy).r,parallax,localView);
            gray=dot(tex2Dgrad(_MainTex,p,gx,gy).rgb,float3(0.2126,0.7152,0.0722));
            normal=UnpackNormal(tex2Dgrad(_BumpMap,p,gx,gy));normal.xy=MoonRotate(normal.xy,float2(cs.x,-cs.y));
            roughness=tex2Dgrad(_Roughness,p,gx,gy).r;
        }
        void SampleFine(float2 uv,float2 dx,float2 dy,float2 cell,out float gray,out float3 normal)
        {
            float2 seed=MoonHash(cell+47.3),cs=float2(cos(seed.x*6.2831853),sin(seed.x*6.2831853));
            float2 p=MoonRotate(uv,cs)+seed*19.3,gx=MoonRotate(dx,cs),gy=MoonRotate(dy,cs);
            gray=dot(tex2Dgrad(_MicroTex,p,gx,gy).rgb,float3(0.2126,0.7152,0.0722));
            normal=UnpackNormal(tex2Dgrad(_MicroNormal,p,gx,gy));normal.xy=MoonRotate(normal.xy,float2(cs.x,-cs.y));
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            clip(tex2D(_TerrainHolesTexture, IN.uv_TerrainHolesTexture).r - 0.5);
            float distanceToCamera=distance(_WorldSpaceCameraPos,IN.worldPos);
            float2 uv = IN.worldPos.xz / 3.1;
            float parallax=_ParallaxMetres*_DetailStrength*saturate(1-distanceToCamera/22);
            float2 a,b,c;float3 w;MoonLattice(uv,a,b,c,w);
            float ga,gb,gc,ra,rb,rc;float3 na,nb,nc;
            SampleGrain(uv,ddx(uv),ddy(uv),a,parallax,IN.viewDir,ga,na,ra);
            SampleGrain(uv,ddx(uv),ddy(uv),b,parallax,IN.viewDir,gb,nb,rb);
            SampleGrain(uv,ddx(uv),ddy(uv),c,parallax,IN.viewDir,gc,nc,rc);
            float gray=dot(float3(ga,gb,gc),w),roughness=dot(float3(ra,rb,rc),w);
            float3 normal=normalize(na*w.x+nb*w.y+nc*w.z);
            float fineFade=saturate(1-distanceToCamera/18);
            float2 fine=IN.worldPos.xz/0.4,fdx=ddx(fine),fdy=ddy(fine);
            float microGray=0.5;float3 microNormal=float3(0,0,1);
            UNITY_BRANCH if(fineFade>0)
            {
                MoonLattice(fine,a,b,c,w);
                SampleFine(fine,fdx,fdy,a,ga,na);
                SampleFine(fine,fdx,fdy,b,gb,nb);
                SampleFine(fine,fdx,fdy,c,gc,nc);
                microGray=dot(float3(ga,gb,gc),w);
                microNormal=normalize(na*w.x+nb*w.y+nc*w.z);
            }
            float3 geology=MoonGeology(IN.worldPos.xz);
            float rock;
            float materialNear=MoonMaterialNearWeight(IN.worldPos);
            float3 facies=MoonFacies(IN.worldPos,1-saturate(IN.worldNormal.y),geology,rock);
            float macro=MoonMacro(IN.worldPos)*MoonGeologyTint(geology,materialNear);
            o.Albedo=_Color.rgb*lerp(0.45,lerp(0.38,gray,0.65)*lerp(1,microGray*2,0.18*fineFade)*macro,_DetailStrength);
            o.Albedo*=_ReflectanceScale*facies;
            // Exposed zones use a separate metre-scale sample. Broad rock plates
            // coexist with fine powder instead of the same grain everywhere.
            float2 bedUV=IN.worldPos.xz/1.15;
            float bed=MoonGray(tex2D(_MainTex,bedUV).rgb);
            float3 bedNormal=UnpackNormal(tex2D(_BumpMap,bedUV));
            float bedWeight=rock*_BedrockStrength*_DetailStrength;
            o.Albedo*=lerp(1,lerp(.65,1.25,smoothstep(.22,.7,bed)),bedWeight*lerp(.15,1,materialNear));
            // Let normal-map mip filtering reduce unresolved detail; avoid an
            // additional distance-based flattening of the distant landscape.
            normal.xy*=_GrainNormalStrength;
            normal.xy+=microNormal.xy*_FineNormalStrength*fineFade;
            normal.xy=lerp(normal.xy,bedNormal.xy*1.2,bedWeight);
            normal.xy*=1-geology.b*0.18;
            o.Normal=normalize(lerp(float3(0,0,1),normal,_DetailStrength));
            o.Metallic = 0;
            o.Smoothness = (1-roughness)*0.2*(1-geology.r*0.2);
            o.Occlusion = 1;
        }

SurfaceOutputStandard MoonSurface(MoonVaryings input, out half3 normalWS, out half3 viewWS)
{
    half3 n = normalize(input.normalWS), t = normalize(input.tangentWS.xyz);
    half3 b = cross(n,t) * input.tangentWS.w;
    viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    Input data;
    data.worldPos = input.positionWS;
    data.worldNormal = n;
    data.rockData = input.rockData;
    data.viewDir = float3(dot(viewWS,t),dot(viewWS,b),dot(viewWS,n));
    data.uv_MainTex = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
    data.uv_TerrainHolesTexture = input.uv;
    SurfaceOutputStandard surface = (SurfaceOutputStandard)0;
    surface.Normal = half3(0,0,1); surface.Occlusion=1; surface.Alpha=1;
    surf(data,surface);
    normalWS = normalize(t * surface.Normal.x + b * surface.Normal.y + n * surface.Normal.z);
    return surface;
}
half4 MoonFragment(MoonVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half3 normal, view;
    SurfaceOutputStandard surface = MoonSurface(input,normal,view);
    // URP's clustered LIGHT_LOOP macros require this exact variable name.
    InputData inputData = (InputData)0;
    inputData.positionWS=input.positionWS; inputData.normalWS=normal; inputData.viewDirectionWS=view;
    inputData.shadowCoord=MoonFragmentShadowCoord(input); inputData.bakedGI=SampleSH(normal) * _IndirectStrength;
    inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask=half4(1,1,1,1);
#if !defined(_MOON_LUNAR_LIGHTING)
    SurfaceData pbr = (SurfaceData)0;
    pbr.albedo=surface.Albedo; pbr.normalTS=surface.Normal; pbr.smoothness=surface.Smoothness;
    pbr.occlusion=surface.Occlusion; pbr.alpha=1;
    return UniversalFragmentPBR(inputData,pbr);
#else
    Light mainLight=MoonMainLight(input);
    half3 color=MoonLight(surface,normal,view,mainLight) + surface.Albedo *
        (inputData.bakedGI + MoonLocalBounce(input.positionWS,normal,mainLight));
    #if defined(_ADDITIONAL_LIGHTS)
    uint lightCount=GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    for (uint lightIndex=0; lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS); ++lightIndex)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light=GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1));
        color+=MoonLight(surface,normal,view,light);
    }
    #endif
    LIGHT_LOOP_BEGIN(lightCount)
        Light light=GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1));
        color+=MoonLight(surface,normal,view,light);
    LIGHT_LOOP_END
    #endif
    return half4(color,1);
#endif
}
half4 MoonDepthNormals(MoonVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    half3 normal,view;
    MoonSurface(input,normal,view);
    return half4(normal,0);
}
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex MoonVertex
            #pragma fragment MoonFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local _MOON_LUNAR_LIGHTING
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            Cull Off
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex MoonShadowVertex
            #pragma fragment MoonDepth
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            Cull Off
            ColorMask R
            HLSLPROGRAM
            #pragma vertex MoonVertex
            #pragma fragment MoonDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex MoonVertex
            #pragma fragment MoonDepthNormals
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
