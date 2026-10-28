#ifndef MOON_SOURCE_WORLD_DEPTH_INCLUDED
#define MOON_SOURCE_WORLD_DEPTH_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
float3 _LightDirection,_LightPosition;
struct SourceDepthAttributes{float3 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
float4 SourceDepthVert(SourceDepthAttributes v):SV_POSITION
{
 UNITY_SETUP_INSTANCE_ID(v);return TransformObjectToHClip(v.positionOS);
}
float4 SourceShadowVert(SourceDepthAttributes v):SV_POSITION
{
 UNITY_SETUP_INSTANCE_ID(v);float3 p=TransformObjectToWorld(v.positionOS);float3 n=TransformObjectToWorldNormal(v.normalOS);
 #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
 float3 direction=normalize(_LightPosition-p);
 #else
 float3 direction=_LightDirection;
 #endif
 float4 clip=TransformWorldToHClip(ApplyShadowBias(p,n,direction));
 #if UNITY_REVERSED_Z
 clip.z=min(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
 #else
 clip.z=max(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
 #endif
 return clip;
}
half4 SourceDepthFrag():SV_Target{return 0;}
#endif
