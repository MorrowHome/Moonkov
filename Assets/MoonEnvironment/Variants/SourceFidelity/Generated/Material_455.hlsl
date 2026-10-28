

void EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)
{
SourceClosure b0=SourceDefaultClosure(c);
b0.baseColor=(float4(0.319999993,0.324999988,0.300000012,1.0)).xyz;
b0.metallic=0.0;
b0.roughness=0.899999976;
b0.ior=1.5;
b0.specular=0.5;
b0.coat=0.0;
b0.coatRoughness=0.0299999993;
b0.transmission=0.0;
b0.alpha=1.0;
b0.anisotropy=0.0;
b0.anisotropicRotation=0.0;
float3 v12=c.local;
float v13=SourceFBM(v12,0.0,5.0,2.0,0.5,2.0,false,true);
float3 v14=SourceBump(c.normal,v13,0.200000003,0.0549999997,c.position,false);
b0.normal=v14;
b0.emission=(float4(1.0,1.0,1.0,1.0)).xyz*0.0;
a=b0; b=b0; factor=0; a.alpha*=1; b.alpha*=1;
}
