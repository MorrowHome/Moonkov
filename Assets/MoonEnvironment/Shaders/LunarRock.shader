Shader "MoonEnvironment/LunarRock"
{
    Properties
    {
        _MainTex("Scanned lunar analogue albedo",2D)="white"{}
        _BumpMap("Normal",2D)="bump"{}
        _Roughness("Roughness",2D)="white"{}
        _Color("Reflectance",Color)=(0.7,0.7,0.7,1)
        [Toggle(_MOON_LUNAR_LIGHTING)] _LunarLighting("Lunar diffuse lighting", Float) = 1
        _LunarWeight("Lommel-Seeliger weight", Range(0,1)) = 0.55
        _IndirectStrength("Indirect light contribution", Range(0,1)) = 0
        _ReflectanceScale("Reflectance calibration (exposure independent)", Range(0.1,1.5)) = 0.8
        _LocalBounceStrength("Local terrain bounce", Range(0,2)) = 1
        _BounceAlbedo("Average source terrain reflectance", Range(0,0.3)) = 0.12
        _MacroContrast("Landscape material contrast", Range(0,1)) = 0.8
        _MaterialFadeStart("Preserve near material contrast to (m)", Float) = 35
        _MaterialFadeEnd("Distant material blend complete (m)", Float) = 180
        _DistantVariation("Distant macro contrast fraction", Range(0,1)) = 0.12
        _FaciesStrength("Powder / exposed rock separation", Range(0,1)) = 0.85
        _DustTint("Fine powder tint", Color) = (1.06,1.03,0.98,1)
        _BasaltTint("Exposed rock tint", Color) = (0.64,0.67,0.71,1)
        [HideInInspector] _ImpactGeology("Generated rim / rubble / basin", 2D) = "black" {}
        [HideInInspector] _ImpactReady("Generated geology enabled", Float) = 0
        [HideInInspector] _ProceduralRock("Vertex-authored rock variation", Float) = 0
        [HideInInspector] _LocalTerrainGeometry("Runtime terrain geometry", 2D) = "black" {}
        [HideInInspector] _LocalTerrainRect("Runtime terrain bounds", Vector) = (0,0,1,1)
        [HideInInspector] _LocalTerrainReady("Runtime terrain ready", Float) = 0
        [HideInInspector] _FragmentMode("Small fragment distance fade", Float) = 0
        [Toggle] _PhaseEnabled("Phase / opposition reflectance", Float) = 1
        _PhaseSlope("Broad phase falloff per radian", Range(0,1)) = 0.12
        _ShadowHidingAmplitude("Shadow hiding peak amplitude", Range(0,1)) = 0.15
        _ShadowHidingWidth("Shadow hiding angle scale (degrees)", Range(0.05,30)) = 5
        _CoherentAmplitude("Coherent backscatter peak amplitude", Range(0,1)) = 0.08
        _CoherentWidth("Coherent angle scale (degrees)", Range(0.05,15)) = 3
        _ScanColorWeight("Scanned colour contribution", Range(0,1)) = 0.35
        _FreshRockTint("Less weathered rock tint", Color) = (0.96,0.98,1,1)
        _WeatheredRockTint("Weathered rock tint", Color) = (1,0.97,0.94,1)
        _GroundTex("Ground grain",2D)="white"{}
        _GroundColor("Ground reflectance",Color)=(0.65,0.65,0.65,1)
        _GeologyMap("Ground geology",2D)="black"{}
        _GeologyRect("Mask world XZ origin / size",Vector)=(1792,1792,512,512)
        _GeologyStrength("Geological variation",Range(0,1))=0.65
        _EjectaBrightness("Ground ejecta reflectance variation", Range(0,0.4)) = 0.16
        [HideInInspector] _GroundPlane("Ground contact plane",Vector)=(0,1,0,0)
        [HideInInspector] _BlendHeight("Dust transition metres",Float)=0.10
        [HideInInspector] _RockVariation("Reflectance variation",Float)=1
        [HideInInspector] _RockFreshness("Synthetic freshness proxy",Float)=0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-100" "TerrainCompatible"="True" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "MoonInput.hlsl"
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float3 diffuse=tex2D(_MainTex,IN.uv_MainTex).rgb;
            if (_ProceduralRock>.5)
            {
                float3 w=pow(abs(IN.worldNormal),4);w/=max(dot(w,float3(1,1,1)),.001);
                diffuse=tex2D(_MainTex,IN.worldPos.zy/.7).rgb*w.x
                    +tex2D(_MainTex,IN.worldPos.xz/.7).rgb*w.y+tex2D(_MainTex,IN.worldPos.xy/.7).rgb*w.z;
            }
            float gray=dot(diffuse,float3(0.2126,0.7152,0.0722));
            float4 plane=_GroundPlane;
            float height=dot(plane.xyz,IN.worldPos)+plane.w;
            float blendHeight=_BlendHeight;
            float dust=1-smoothstep(0,max(blendHeight,0.001),height);
            float variation=_RockVariation;
            float freshness=_RockFreshness;
            if (_ProceduralRock>.5)
            {
                variation=IN.rockData.r;
                freshness=IN.rockData.g;
                dust=IN.rockData.b;
            }
            float2 uv=IN.worldPos.xz/3.1,dx=ddx(uv),dy=ddy(uv);
            float3 ground=0;
            UNITY_BRANCH if(dust>0.001)
            {
                float2 a,b,c;float3 w;MoonLattice(uv,a,b,c,w);
                float groundGray=dot(float3(MoonGrainGray(_GroundTex,uv,dx,dy,a),MoonGrainGray(_GroundTex,uv,dx,dy,b),MoonGrainGray(_GroundTex,uv,dx,dy,c)),w);
                float groundTint=MoonMacro(IN.worldPos)*MoonGeologyTint(MoonGeology(IN.worldPos.xz),MoonMaterialNearWeight(IN.worldPos));
                ground=_GroundColor.rgb*lerp(0.38,groundGray,0.65)*groundTint;
            }
            // A thin dust coat joins the surface without hiding the rock silhouette.
            float3 scan=lerp(gray.xxx,diffuse,_ScanColorWeight);
            float3 tint=lerp(_WeatheredRockTint.rgb,_FreshRockTint.rgb,saturate(freshness));
            // Large scanned rocks retain their authored UVs, but no longer all
            // share one shade. Changes follow world position and surface exposure.
            tint*=lerp(.72,1.12,MoonNoise(IN.worldPos.xz/2.7));
            tint*=lerp(float3(.83,.86,.91),float3(1.02,1,.96),saturate(IN.worldNormal.y));
            o.Albedo=lerp(scan*_Color.rgb*tint*variation,ground,dust*0.85);
            o.Albedo*=1-0.10*(1-smoothstep(0,0.018,height));
            o.Albedo*=_ReflectanceScale;
            float3 normal=UnpackNormal(tex2D(_BumpMap,IN.uv_MainTex));
            normal.xy*=1-dust*0.3;o.Normal=normalize(normal);
            o.Metallic=0;
            o.Smoothness=(1-tex2D(_Roughness,IN.uv_MainTex).r)*0.3*(1-dust*0.65);
        }

SurfaceOutputStandard MoonSurface(MoonVaryings input, out half3 normalWS, out half3 viewWS)
{
    MoonFragmentFade(input.positionWS);
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
            Cull Back
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
            Cull Back
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
            Cull Back
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
            Cull Back
            HLSLPROGRAM
            #pragma vertex MoonVertex
            #pragma fragment MoonDepthNormals
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
