#ifndef MOON_SOURCE_MATERIAL_NODES_INCLUDED
#define MOON_SOURCE_MATERIAL_NODES_INCLUDED
// Source-compatible Perlin/fBm and Jenkins hash adaptation.
// Noise: Copyright 2009-2010 Sony Pictures Imageworks Inc., et al.;
// Copyright 2011-2022 Blender Foundation. BSD-3-Clause.
// Hash/fBm: Copyright 2011-2022 Blender Foundation. Apache-2.0.
// See SourceData/Models/SourceFidelity20261028/Licenses.
uint SourceRotate(uint v,uint n){return (v<<n)|(v>>(32-n));}
uint SourceHashFinal(uint a,uint b,uint c)
{
 c^=b;c-=SourceRotate(b,14);a^=c;a-=SourceRotate(c,11);b^=a;b-=SourceRotate(a,25);
 c^=b;c-=SourceRotate(b,16);a^=c;a-=SourceRotate(c,4);b^=a;b-=SourceRotate(a,14);c^=b;c-=SourceRotate(b,24);return c;
}
uint SourceHash3(uint3 p){uint a=0xdeadbeefu+25u;return SourceHashFinal(a+p.x,a+p.y,a+p.z);}
uint SourceHash4(uint4 p)
{
 uint a=0xdeadbeefu+29u+p.x,b=0xdeadbeefu+29u+p.y,c=0xdeadbeefu+29u+p.z;
 a-=c;a^=SourceRotate(c,4);c+=b;b-=a;b^=SourceRotate(a,6);a+=c;c-=b;c^=SourceRotate(b,8);b+=a;
 a-=c;a^=SourceRotate(c,16);c+=b;b-=a;b^=SourceRotate(a,19);a+=c;c-=b;c^=SourceRotate(b,4);b+=a;
 return SourceHashFinal(a+p.w,b,c);
}
float SourceGradient3(uint h,float3 p)
{
 h&=15;float u=h<8?p.x:p.y;float v=h<4?p.y:((h==12||h==14)?p.x:p.z);
 return ((h&1)!=0?-u:u)+((h&2)!=0?-v:v);
}
float SourceGradient4(uint h,float4 p)
{
 h&=31;float u=h<24?p.x:p.y,v=h<16?p.y:p.z,w=h<8?p.z:p.w;
 return ((h&1)!=0?-u:u)+((h&2)!=0?-v:v)+((h&4)!=0?-w:w);
}
float SourcePerlin(float4 position,bool four)
{
 float4 offset=step(1000000.0,abs(position))*.5;position=fmod(position,100000.0)+offset;
 int4 cell=(int4)floor(position);float4 p=position-floor(position);float4 fade=p*p*p*(p*(p*6-15)+10);
 float sum=0;
 // Keep the lattice loop compact. Full unrolling duplicates Jenkins hashes
 // throughout every source graph and exhausts Metal's shader compiler memory.
 [loop]for(int index=0;index<(four?16:8);index++)
 {
  int4 corner=int4(index&1,(index>>1)&1,(index>>2)&1,(index>>3)&1);
  uint hash=four?SourceHash4((uint4)(cell+corner)):SourceHash3((uint3)(cell.xyz+corner.xyz));
  float gradient=four?SourceGradient4(hash,p-corner):SourceGradient3(hash,p.xyz-corner.xyz);
  float4 weight=lerp(1-fade,fade,(float4)corner);
  sum+=gradient*weight.x*weight.y*weight.z*(four?weight.w:1);
 }
 return sum*(four?.8344:.982);
}
float SourceFBM(float3 p,float w,float scale,float detail,float roughness,float lacunarity,bool four,bool normalized)
{
 float4 position=float4(p,w)*scale;float sum=0,amplitude=1,maximum=0,frequency=1;
 detail=clamp(detail,0,15);roughness=clamp(roughness,0,1);
 [loop]for(int octave=0;octave<=(int)detail;octave++)
 {sum+=SourcePerlin(position*frequency,four)*amplitude;maximum+=amplitude;amplitude*=roughness;frequency*=lacunarity;}
 float remainder=frac(detail);float result=normalized?.5*sum/maximum+.5:sum;
 if(remainder>0){float next=sum+SourcePerlin(position*frequency,four)*amplitude;result=lerp(result,normalized?.5*next/(maximum+amplitude)+.5:next,remainder);}
 return result;
}
uint SourceHash2(uint2 p){uint a=0xdeadbeefu+21u;return SourceHashFinal(a+p.x,a+p.y,a);}
float4 SourceNoiseOffset(float seed)
{
 return 100+float4(SourceHash2(asuint(float2(seed,0))),SourceHash2(asuint(float2(seed,1))),SourceHash2(asuint(float2(seed,2))),SourceHash2(asuint(float2(seed,3))))*(100.0/4294967295.0);
}
float4 SourceNoiseColor(float3 p,float w,float scale,float detail,float roughness,float lacunarity,bool four,bool normalized)
{
 float4 position=float4(p,w)*scale;float4 a=SourceNoiseOffset(four?4:3),b=SourceNoiseOffset(four?5:4);
 return float4(SourceFBM(p,w,scale,detail,roughness,lacunarity,four,normalized),SourceFBM(position.xyz+a.xyz,position.w+a.w,1,detail,roughness,lacunarity,four,normalized),SourceFBM(position.xyz+b.xyz,position.w+b.w,1,detail,roughness,lacunarity,four,normalized),1);
}
float SourceWhiteNoise1(float w){uint a=0xdeadbeefu+17u;return (float)SourceHashFinal(a+asuint(w),a,a)*(1.0/4294967295.0);}
float SourceWhiteNoise(float3 p,float w,bool four){return (float)(four?SourceHash4(asuint(float4(p,w))):SourceHash3(asuint(p)))*(1.0/4294967295.0);}
float SourceDivide(float a,float b){return b!=0?a/b:0;}
float3 SourceDivide(float3 a,float3 b){return float3(SourceDivide(a.x,b.x),SourceDivide(a.y,b.y),SourceDivide(a.z,b.z));}
float SourcePower(float a,float b){return a<0&&frac(b)!=0?0:pow(abs(a),b)*(a<0&&fmod(abs(b),2)==1?-1:1);}
float SourceModulo(float a,float b){return b!=0?fmod(a,b):0;}
float SourcePingPong(float a,float scale){return scale!=0?abs(frac((a-scale)/(2*scale))*2*scale-scale):0;}
float SourceLuminance(float3 c){return dot(c,float3(.2126,.7152,.0722));}
float SourceMean(float3 c){return dot(c,1.0/3.0);}
float3 SourceSafeNormal(float3 n){return n*rsqrt(max(dot(n,n),1e-20));}
float SourceFresnel(float3 n,float3 view,float ior)
{
 float c=abs(dot(n,view));float eta=max(ior,1e-5);float g=eta*eta-1+c*c;if(g<=0)return 1;g=sqrt(g);
 float a=(g-c)/(g+c);float b=(c*(g+c)-1)/(c*(g-c)+1);return .5*a*a*(1+b*b);
}
float3 SourceTangentNormal(float3 normal,float3 color,float strength,float3 position,float2 uv,float4 sourceTangent)
{
 float3 dpdx=ddx(position),dpdy=ddy(position);float2 duvdx=ddx(uv),duvdy=ddy(uv);
 float determinant=duvdx.x*duvdy.y-duvdx.y*duvdy.x;float signUV=determinant<0?-1:1;
 float3 tangent=dot(sourceTangent.xyz,sourceTangent.xyz)>1e-10?SourceSafeNormal(sourceTangent.xyz):SourceSafeNormal((dpdx*duvdy.y-dpdy*duvdx.y)*signUV);
 if(dot(sourceTangent.xyz,sourceTangent.xyz)>1e-10)signUV=sourceTangent.w;
 tangent=SourceSafeNormal(tangent-normal*dot(normal,tangent));float3 bitangent=cross(normal,tangent)*signUV;
 float3 map=color*2-1;map.xy*=max(strength,0);map.z=lerp(1,map.z,saturate(strength));
 return SourceSafeNormal(tangent*map.x+bitangent*map.y+normal*map.z);
}
float3 SourceBump(float3 normal,float height,float strength,float distance,float3 position,bool invert)
{
 float3 dx=ddx(position),dy=ddy(position);float3 r1=cross(dy,normal),r2=cross(normal,dx);float determinant=dot(dx,r1);
 float3 gradient=(ddx(height)*r1+ddy(height)*r2)*(determinant<0?-1:1);
 float3 bumped=SourceSafeNormal(abs(determinant)*normal-distance*(invert?-1:1)*gradient);
 return SourceSafeNormal(lerp(normal,bumped,saturate(strength)));
}
struct SourceContext
{
 float3 position,normal,local,generated,view;
 float4 tangent;float random;float2 uv0,uv1,uv2,bakeUV;float4 attributes0,attributes1;
 float3 ao,irradianceGradient;float viewDistance,relativeVariance;
};
struct SourceClosure
{
 float3 baseColor,normal,coatNormal,emission;float metallic,roughness,ior,specular,coat,coatRoughness,transmission,alpha,anisotropy,anisotropicRotation;
};
SourceClosure SourceDefaultClosure(SourceContext c)
{
 SourceClosure b=(SourceClosure)0;b.baseColor=.8;b.normal=c.normal;b.coatNormal=c.normal;b.roughness=.5;b.ior=1.5;b.specular=.5;b.coatRoughness=.03;b.alpha=1;return b;
}
#endif
