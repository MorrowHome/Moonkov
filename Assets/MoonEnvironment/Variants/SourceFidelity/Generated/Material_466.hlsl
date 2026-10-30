

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
float3 v1=c.local;
float v2=SourceFBM(v1,0.0,0.800000012,2.0,0.5,2.0,false,true);
float4 v3=((v2)<=1.0?lerp(float4(0.0923999995,0.127599999,0.125839993,1.0),float4(0.111299999,0.153699994,0.151580006,1.0),saturate((v2-0.0)/1.0)):float4(0.111299999,0.153699994,0.151580006,1.0));
b0.baseColor=(v3).xyz;
b0.metallic=0.649999976;
b0.roughness=0.629999995;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float v15=SourceFBM(v1,0.0,95.0,2.0,0.5,2.0,false,true);
float3 v16=SourceBump(c.normal,v15,0.119999997,0.000600000028,c.position,false);
b0.normal=v16;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
