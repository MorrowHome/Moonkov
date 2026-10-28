Shader "MoonEnvironment/Source/366"
{
 Properties {  }
 SubShader
 {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 Pass
 {
 Name "SourceMaterial" Tags {"LightMode"="UniversalForwardOnly"}
 Cull Off ZWrite On

 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex SourceVert
 #pragma fragment SourceFrag
 #define LIGHTMAP_ON 1
 #define _REFLECTION_PROBE_BLENDING 1
 #define _REFLECTION_PROBE_BOX_PROJECTION 1
 #define _CLUSTER_LIGHT_LOOP 1
 #define _ADDITIONAL_LIGHTS 1
 #define _ADDITIONAL_LIGHT_SHADOWS 1

 #include "../../../Shaders/SourceWorldSurface.hlsl"
 #include "Material_366.hlsl"
 half4 SourceFrag(SourceVaryings i,FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target
 {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 SourceContext c=(SourceContext)0;c.position=i.positionWS.xzy;c.normal=SourceSafeNormal(i.normalWS.xzy);c.tangent=i.tangent;
 c.view=GetWorldSpaceNormalizeViewDir(i.positionWS).xzy;c.local=i.localRandom.xyz;c.random=i.localRandom.w;c.generated=i.generatedAO.xyz;
 c.uv0=i.uv0.xy;c.uv1=i.uv0.zw;c.uv2=i.uv1.zw;c.bakeUV=i.uv1.xy;c.attributes0=i.attributes0;c.attributes1=i.attributes1;
 float2 statistics=SAMPLE_TEXTURE2D_ARRAY(_SourceAOAtlases,sampler_SourceAOAtlases,c.bakeUV,i.generatedAO.w).rg;
 c.ao=lerp(1,statistics.rrr,_SourceAOEnabled);c.relativeVariance=(exp2(statistics.g*16)-1)*_SourceAOEnabled;c.irradianceGradient=i.attributes1.yzw;
 c.viewDistance=distance(i.positionWS,_WorldSpaceCameraPos);
 SourceClosure a,b;float factor;EvaluateSourceMaterial(c,a,b,factor);
 float front=IS_FRONT_VFACE(facing,1,-1);a.normal*=front;a.coatNormal*=front;b.normal*=front;b.coatNormal*=front;
 float3 color=lerp(ShadeSourceClosure(a,c,i),ShadeSourceClosure(b,c,i),saturate(factor));
 float opacity=lerp(a.alpha,b.alpha,saturate(factor));clip(opacity-.001);
 return half4(color*opacity,opacity);
 }
 ENDHLSL
 }
 Pass
 {
 Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
 ZWrite On ZTest LEqual ColorMask 0 Cull Off
 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex SourceShadowVert
 #pragma fragment SourceDepthFrag
 #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
 #include "../../../Shaders/SourceWorldDepth.hlsl"
 ENDHLSL
 }

 Pass
 {
 Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
 ZWrite On ColorMask R Cull Off
 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex SourceDepthVert
 #pragma fragment SourceDepthFrag
 #include "../../../Shaders/SourceWorldDepth.hlsl"
 ENDHLSL
 }

 }
}
