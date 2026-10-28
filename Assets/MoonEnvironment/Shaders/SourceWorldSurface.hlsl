#ifndef MOON_SOURCE_WORLD_SURFACE_INCLUDED
#define MOON_SOURCE_WORLD_SURFACE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "SourceMaterialNodes.hlsl"
TEXTURE2D_ARRAY(_SourceAOAtlases);SAMPLER(sampler_SourceAOAtlases);
float _SourceSpecularSamples,_SourceAOEnabled,_SourceDiffuseSamples;
struct SourceVertex
{
 float3 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangent:TANGENT;
 float4 uv0:TEXCOORD0;float4 uv1:TEXCOORD1;
 float4 localRandom:TEXCOORD2;float4 generatedAO:TEXCOORD3;
 float4 attributes0:TEXCOORD4;float4 attributes1:TEXCOORD5;
 UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct SourceVaryings
{
 float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;
 float4 tangent:TEXCOORD2;float4 uv0:TEXCOORD3;float4 uv1:TEXCOORD4;
 float4 localRandom:TEXCOORD5;float4 generatedAO:TEXCOORD6;
 float4 attributes0:TEXCOORD7;float4 attributes1:TEXCOORD8;
 UNITY_VERTEX_OUTPUT_STEREO
};
SourceVaryings SourceVert(SourceVertex v)
{
 SourceVaryings o=(SourceVaryings)0;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
 o.positionWS=TransformObjectToWorld(v.positionOS);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(v.normalOS);
 o.tangent=v.tangent;o.uv0=v.uv0;o.uv1=v.uv1;o.uv1.xy=v.uv1.xy*unity_LightmapST.xy+unity_LightmapST.zw;
 o.localRandom=v.localRandom;o.generatedAO=v.generatedAO;o.attributes0=v.attributes0;o.attributes1=v.attributes1;return o;
}
uint SourcePixelHash(uint v){v=(v^(v>>16))*0x7feb352du;v=(v^(v>>15))*0x846ca68bu;return v^(v>>16);}
float SourceSampleRandom(uint2 pixel,uint sampleIndex){return (SourcePixelHash(pixel.x*1973u+pixel.y*9277u+sampleIndex*26699u+911u)&0x00ffffffu)*(1.0/16777216.0);}
float SourceGGXVisibility(float3 v,float3 n,float3 tangent,float3 bitangent,float ax,float ay)
{
 float nv=max(dot(n,v),0);float tx=dot(tangent,v)*ax,by=dot(bitangent,v)*ay;return 2*nv/max(nv+sqrt(nv*nv+tx*tx+by*by),1e-8);
}
float3 SourceGlossy(SourceClosure b,SourceContext c,SourceVaryings input,float3 normal,float3 f0,float roughness,uint seed,float anisotropy)
{
 float3 n=normal.xzy,v=c.view.xzy;float nv=dot(n,v);if(nv<=0)return 0;
 float3 t=c.tangent.xyz.xzy;t=SourceSafeNormal(t-n*dot(t,n));
 if(dot(t,t)<.1)t=SourceSafeNormal(cross(abs(n.z)<.999?float3(0,0,1):float3(1,0,0),n));
 float3 bitangent=cross(n,t);float rotation=b.anisotropicRotation*6.28318530718;
 float3 rotated=t*cos(rotation)+bitangent*sin(rotation);bitangent=-t*sin(rotation)+bitangent*cos(rotation);t=rotated;
 float aspect=sqrt(1-.9*saturate(anisotropy));float alpha=max(roughness*roughness,.001);float ax=alpha/aspect,ay=alpha*aspect;
 uint count=(uint)clamp(_SourceSpecularSamples,1,16);float3 sum=0;uint2 pixel=(uint2)input.positionCS.xy;
 float2 screenUV=GetNormalizedScreenSpaceUV(input.positionCS);
 [loop]for(uint index=0;index<count;index++)
 {
  float u=SourceSampleRandom(pixel,index*2+seed),phi=6.28318530718*SourceSampleRandom(pixel,index*2+seed+1);
  float radius=sqrt(u/max(1-u,1e-8));float3 h=normalize(t*(ax*radius*cos(phi))+bitangent*(ay*radius*sin(phi))+n);
  float vh=dot(v,h),nh=dot(n,h);float3 l=reflect(-v,h);float nl=dot(n,l);
  if(nl>0&&vh>0)
  {
   float3 f=f0+(1-f0)*pow(1-saturate(vh),5);
   float g=SourceGGXVisibility(v,n,t,bitangent,ax,ay)*SourceGGXVisibility(l,n,t,bitangent,ax,ay);
   sum+=GlossyEnvironmentReflection(l,input.positionWS,0,1,screenUV)*f*g*vh/max(nv*nh,1e-6);
  }
 }
 return sum/count;
}
float SourceDiffuseSampleWeight(SourceContext c,SourceVaryings input)
{
 if(c.relativeVariance<.0001||_SourceDiffuseSamples<=0)return 1;
 // Variance is measured from Cycles' raw path samples against its guided mean.
 // Gamma sampling preserves positive radiance and the mean, including the
 // sparse bright paths of low-sample indirect light. The screen seed is fixed.
 float shape=clamp(_SourceDiffuseSamples/c.relativeVariance,.01,1000000);
 float adjusted=shape<1?shape+1:shape;float d=adjusted-1.0/3.0;float scale=rsqrt(9*d);
 uint2 pixel=(uint2)input.positionCS.xy;float result=adjusted;
 [loop]for(uint attempt=0;attempt<4;attempt++)
 {
  float u=max(SourceSampleRandom(pixel,317+attempt*4),1e-7);float angle=6.28318530718*SourceSampleRandom(pixel,318+attempt*4);
  float gaussian=sqrt(-2*log(u))*cos(angle);float v=1+scale*gaussian;
  if(v>0)
  {
   v=v*v*v;float acceptance=max(SourceSampleRandom(pixel,319+attempt*4),1e-7);
   if(acceptance<1-.0331*gaussian*gaussian*gaussian*gaussian||log(acceptance)<.5*gaussian*gaussian+d*(1-v+log(v)))
   {result=d*v;break;}
  }
 }
 if(shape<1)result*=pow(max(SourceSampleRandom(pixel,401),1e-7),1/shape);
 return result/shape;
}
float3 ShadeSourceClosure(SourceClosure b,SourceContext c,SourceVaryings input)
{
 float metallic=saturate(b.metallic),transmission=saturate(b.transmission);
 float dielectric=pow((b.ior-1)/max(b.ior+1,.001),2)*2*b.specular;
 float3 f0=lerp(dielectric,b.baseColor,metallic);
 float3 irradiance=SampleLightmap(input.uv1.xy,float2(0,0),b.normal.xzy);
 // Cycles diffuse irradiance already includes the source shader normal.
 // Applying the same normal response again would double the bump lighting.
 float directionalCorrection=1;
 float3 color=irradiance*directionalCorrection*SourceDiffuseSampleWeight(c,input)*b.baseColor*(1-metallic)*(1-transmission)*(1-dielectric);
 color+=SourceGlossy(b,c,input,b.normal,f0,b.roughness,17,b.anisotropy);
 if(b.coat>0)
 {
  float fresnel=.04+.96*pow(1-saturate(dot(b.coatNormal,c.view)),5);
  color=color*(1-fresnel*b.coat)+b.coat*SourceGlossy(b,c,input,b.coatNormal,.04,b.coatRoughness,101,0);
 }
 #if defined(_SOURCE_TRANSMISSION)
 if(transmission>0)
 {
  float2 uv=GetNormalizedScreenSpaceUV(input.positionCS);
  float3 viewNormal=TransformWorldToViewDir(b.normal.xzy);
  float2 offset=viewNormal.xy*(1-1/max(b.ior,1.001))*.012;
  float fresnel=SourceFresnel(b.normal,c.view,b.ior);
  color+=SampleSceneColor(saturate(uv+offset))*b.baseColor*transmission*(1-metallic)*(1-fresnel);
 }
 #endif
 // Source suns and area lights are already in irradiance. Only gameplay lights
 // are realtime additional lights; they do not replace the baked source rig.
 #if defined(_ADDITIONAL_LIGHTS)
 InputData inputData=(InputData)0;inputData.positionWS=input.positionWS;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
 BRDFData brdf;half alpha=1;InitializeBRDFData(b.baseColor,metallic,0,1-b.roughness,alpha,brdf);
 uint pixelLightCount=GetAdditionalLightsCount();
 LIGHT_LOOP_BEGIN(pixelLightCount)
  Light light=GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1));
  color+=LightingPhysicallyBased(brdf,light,b.normal.xzy,c.view.xzy);
 LIGHT_LOOP_END
 #endif
 return color+b.emission;
}
#endif
